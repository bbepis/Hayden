using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Cache;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Hayden.Proxy;
using Nito.AsyncEx;
using Polly.Timeout;
using Serilog;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	/// <summary>
	/// Handles the core archival logic, independent of any API or consumer implementations.
	/// </summary>
	public class BoardArchiver
	{
		public SourceConfig SourceConfig { get; }
		public ConsumerConfig ConsumerConfig { get; }

		protected IThreadConsumer ThreadConsumer { get; }
		protected IFrontendApi FrontendApi { get; }
		protected IStateStore StateStore { get; }
		protected ProxyProvider ProxyProvider { get; }
		protected IFileSystem FileSystem { get; }

		protected List<ThreadPointer> ThreadIdBlacklist { get; } = new();
		protected SortedList<ThreadPointer, TrackedThread> TrackedThreads { get; } = new();

		protected Dictionary<string, BoardRules> BoardRules { get; } = new();

		protected virtual bool LoopArchive { get; }

		protected virtual bool NeedsToDelayThreadApiCall => true;

		/// <summary>
		/// The minimum amount of time the archiver should wait before checking the boards for any thread updates.
		/// </summary>
		public TimeSpan BoardUpdateTimespan { get; set; }

		/// <summary>
		/// The minimum amount of time that should be waited in-between API calls.
		/// </summary>
		public TimeSpan ApiCooldownTimespan { get; set; }

		/// <summary>
		/// Keeps track of the last time we scraped a board, and therefore determines which threads we should scrape.
		/// </summary>
		protected SortedList<string, DateTimeOffset> LastBoardCheckTimes;


		/// <summary>
		/// The download client to use when downloading images.
		/// </summary>
		protected HttpClientProxy ImageDownloadClient;


		public BoardArchiver(SourceConfig sourceConfig, ConsumerConfig consumerConfig, IFrontendApi frontendApi,
			IThreadConsumer threadConsumer, IFileSystem fileSystem, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		{
			SourceConfig = sourceConfig;
			ConsumerConfig = consumerConfig;
			FileSystem = fileSystem;

			FrontendApi = frontendApi;
			ThreadConsumer = threadConsumer;
			ProxyProvider = proxyProvider ?? new NullProxyProvider();
			StateStore = stateStore ?? new NullStateStore();

			ApiCooldownTimespan = TimeSpan.FromSeconds(sourceConfig.ApiDelay ?? 1);
			BoardUpdateTimespan = TimeSpan.FromSeconds(sourceConfig.BoardScrapeDelay ?? 30);

			foreach (var (board, boardConfig) in sourceConfig.Boards)
			{
				BoardRules[board] = new BoardRules(boardConfig);
			}

			LastBoardCheckTimes = new SortedList<string, DateTimeOffset>(SourceConfig.Boards.Count);
			ImageDownloadClient = new HttpClientProxy(ProxyProvider.CreateNewClient(), "baseconnection/image");

			LoopArchive = !sourceConfig.SingleScan;
		}

		/// <summary>
		/// Performs the main archival loop.
		/// </summary>
		/// <param name="token">Token to safely cancel the execution.</param>
		public async Task Execute(CancellationToken token)
		{
			bool firstRun = true;

			// We only loop if cancellation has not been requested (i.e. "Q" has not been pressed)
			// Every time you see "token" mentioned, its performing a check

			MaybeAsyncEnumerable<ThreadPointer> queuedThreads = null;
			var queuedImages = new List<QueuedImageDownload>();

			while (!token.IsCancellationRequested)
			{
				if (firstRun || LoopArchive)
				{
					queuedThreads = await ReadBoards(firstRun, token);
				}
				
				if (queuedThreads.IsListBacked)
					Log.Information("{queuedThreadsCount} threads have been queued total", queuedThreads.Count);

				var (requeuedThreads, requeuedImages) = await PerformScrape(firstRun, queuedThreads, queuedImages, token);
				
				queuedThreads = new MaybeAsyncEnumerable<ThreadPointer>(requeuedThreads);
				queuedImages = requeuedImages;

				firstRun = false;

				if (!LoopArchive && queuedThreads.Count == 0 && queuedImages.Count == 0)
					break;
			}
		}

		protected virtual async Task<MaybeAsyncEnumerable<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
		{
			var threadQueue = new List<MaybeAsyncEnumerable<ThreadPointer>>();
			int currentBoardCount = 0;

			// For each board (maximum of 8 concurrently), retrieve a list of threads that need to be scraped
			await SourceConfig.Boards.Keys.ForEachAsync(8, async board =>
			{
				token.ThrowIfCancellationRequested();

				DateTimeOffset lastDateTimeCheck;

				lock (LastBoardCheckTimes)
					if (!LastBoardCheckTimes.TryGetValue(board, out lastDateTimeCheck))
						lastDateTimeCheck = DateTimeOffset.MinValue;

				// Set this time now before we do the network calls, as it's safer and we won't miss any threads that update in-between
				DateTimeOffset beforeCheckTime = DateTimeOffset.Now;

				// Get a list of threads to be scraped
				var (threads, lastReportedTimestamp) = await GetBoardThreads(token, board, lastDateTimeCheck, firstRun);

				if (threads != null)
					lock (threadQueue)
						threadQueue.Add(threads);

				if (firstRun && SourceConfig.ReadArchive && FrontendApi.SupportsArchive)
				{
					// Get a list of archived threads to include to be scraped.
					var archivedThreads = await GetArchivedBoardThreads(token, board, lastDateTimeCheck);

					lock (threadQueue)
						threadQueue.Add(new MaybeAsyncEnumerable<ThreadPointer>(archivedThreads));
				}

				lock (LastBoardCheckTimes)
				{
					if (threads != null)
						LastBoardCheckTimes[board] = lastReportedTimestamp.HasValue ? Utility.ConvertGMTTimestamp((uint)lastReportedTimestamp.Value) : beforeCheckTime;

					if (++currentBoardCount % 5 == 0 || currentBoardCount == SourceConfig.Boards.Count)
					{
						Log.Information("{currentBoardCount} / {SourceConfigBoardsCount} boards polled", currentBoardCount, SourceConfig.Boards.Count);
					}
				}
			});

			if (threadQueue.All(x => x.IsListBacked))
				return new MaybeAsyncEnumerable<ThreadPointer>(threadQueue.SelectMany(x => x.SourceList).ToList());

			return new MaybeAsyncEnumerable<ThreadPointer>(threadQueue.Aggregate((a, b) => new MaybeAsyncEnumerable<ThreadPointer>(a.Concat(b))));
		}

		protected virtual async Task<(List<ThreadPointer> requeuedThreads, List<QueuedImageDownload> requeuedImages)> PerformScrape(
			bool firstRun, MaybeAsyncEnumerable<ThreadPointer> threadQueue, List<QueuedImageDownload> additionalImages, CancellationToken token)
		{
			var enqueuedImages = new ConcurrentQueue<QueuedImageDownload>(additionalImages);

			var requeuedImages = new List<QueuedImageDownload>();

			if (token.IsCancellationRequested)
				return (null, null);

			// We've finished scraping board listings, now we move onto scraping threads/images

			var beginTime = DateTime.UtcNow;
			var waitTask = Task.Delay(BoardUpdateTimespan, token);

			// The list of threads that have failed, or otherwise need to be requeued.
			var requeuedThreads = new List<ThreadPointer>();


			int threadCompletedCount = 0;
			int imageCompletedCount = 0;

			// Downloads a single image.
			async Task<int> DownloadEnqueuedImage(HttpClientProxy client, QueuedImageDownload queuedDownload)
			{
				if (queuedDownload == null)
				{
					// If the queued image is null then we need to try and dequeue one ourselves

					if (!enqueuedImages.TryDequeue(out queuedDownload))
					{
						// There were no images available so return
						return imageCompletedCount;
					}
				}

				// Ensure that an image download takes at least as long as 100ms
				// We don't want to download images too fast
				var waitTask = Task.Delay(100, token);

				string tempFilePath = null, tempThumbPath = null;

				try
				{
					// Perform the actual download.

					if (queuedDownload.FullImageUri != null)
					{
						tempFilePath = await DownloadFileTask(queuedDownload.FullImageUri, client.Client);
					}

					if (queuedDownload.ThumbnailImageUri != null)
					{
						tempThumbPath = await DownloadFileTask(queuedDownload.ThumbnailImageUri, client.Client);
					}

					await ThreadConsumer.ProcessFileDownload(queuedDownload, tempFilePath, tempThumbPath);
				}
				catch (Exception ex)
				{
					// Errored out. Log it and requeue the image
					Log.Error(ex, "Could not download image. Will try again next board update\nClient name: {clientName}", client.Name);

					lock (requeuedImages)
						requeuedImages.Add(queuedDownload);
				}
				finally
				{
					if (tempFilePath != null && FileSystem.File.Exists(tempFilePath))
						FileSystem.File.Delete(tempFilePath);

					if (tempThumbPath != null && FileSystem.File.Exists(tempThumbPath))
						FileSystem.File.Delete(tempThumbPath);
				}

				await waitTask;

				// Increment the downloaded images count and return it
				return Interlocked.Increment(ref imageCompletedCount);
			}

			if (firstRun)
			{
				// This is the first board loop, so we want to get a list of all unfinished queued image downloads from the Cache layer
				// If any exist, they would've been saved from a previous instance of Hayden that did not shut down cleanly.
				// Hayden would not otherwise detect these downloads, unless the thread they originated from had updated

				foreach (var queuedImage in await StateStore.GetDownloadQueue())
					enqueuedImages.Enqueue(queuedImage);

				Log.Information("{enqueuedImagesCount} media items loaded from queue cache", enqueuedImages.Count);
			}

			if (threadQueue.IsListBacked)
			{
				var tempList = threadQueue.SourceList
					.Where(x => x.Board != null && x != default) // Very rarely a null value can slip into here. Not sure why, but just added for safety
					.ToList();
					 
				// We create a round-robin queue that each worker task/thread is able to consume.
				// Round-robin is used specifically since we want to balance out downloaded threads per boards, otherwise it would be downloading a single board at a time
				threadQueue = new MaybeAsyncEnumerable<ThreadPointer>(
					tempList.RoundRobin(x => x.Board)
					.ToList());
			}

			var asyncLock = new AsyncLock();

			await using var threadEnumerator = threadQueue
				.GetAsyncEnumerator(token);

			// This is really only used for debugging purposes
			IDictionary<int, string> workerStatuses = new ConcurrentDictionary<int, string>();

			// Represents a single worker task. In reality this means a thread is spun up for this task
			async Task WorkerTask(int id, bool prioritizeImages)
			{
				// The worker ID, useful for debugging
				var idString = id.ToString();

				// The next two blocks are function definitions. The actual loop code is below them

				// The unit of work involving checking if any images are available, and downloading a single one.
				async Task<bool> CheckImages()
				{
					bool success = enqueuedImages.TryDequeue(out var nextImage);

					if (!success)
						// Exit if no images are available
						return false;

					workerStatuses[id] = $"Downloading image {nextImage.FullImageUri.AbsoluteUri}";

					int completedCount = await DownloadEnqueuedImage(ImageDownloadClient, nextImage);

					if (completedCount % 10 == 0 || enqueuedImages.Count == 0)
					{
						Log.Information($"{"[Image]",-9} [{{completedCount}}/{{enqueuedImagesCount}}]", completedCount, enqueuedImages.Count);
					}

					return true;
				}

				// The unit of work involving checking if any threads are available, and downloading a single one + enqueuing those images.
				async Task<bool> CheckThreads()
				{
					bool success;
					ThreadPointer nextThread;

					// Grab the next thread from the round robin queue. Complex because we want to do this in a thread-safe way
					using (await asyncLock.LockAsync())
					{
						success = await threadEnumerator.MoveNextAsync();
						nextThread = threadEnumerator.Current;
					}

					if (!success)
						// Exit if there are no threads available
						return false;

					workerStatuses[id] = $"Scraping thread /{nextThread.Board}/{nextThread.ThreadId}";

					// Add a timeout for the scrape to 2 minutes, so it doesn't hang forever
					using var timeoutToken = new CancellationTokenSource(TimeSpan.FromMinutes(2));

					await AsyncProxyCall(NeedsToDelayThreadApiCall, async client =>
					{
						string board = nextThread.Board;
						ulong threadNumber = nextThread.ThreadId;
						
						ThreadUpdateTaskResult result;

						try
						{
							var apiResponse = await RetrieveThreadAsync(nextThread, client, timeoutToken.Token);
							result = await ThreadUpdateTask(timeoutToken.Token, idString, board, threadNumber, apiResponse);
						}
						catch (Exception ex)
						{
							Log.Error(ex, "Exception when polling thread");
							result = new ThreadUpdateTaskResult(false, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Error, 0);
						}

						int newCompletedCount = Interlocked.Increment(ref threadCompletedCount);

						string threadStatus;

						switch (result.Status)
						{
							case ThreadUpdateStatus.Ok:				threadStatus = " "; break;
							case ThreadUpdateStatus.Archived:		threadStatus = "A"; break;
							case ThreadUpdateStatus.Deleted:		threadStatus = "D"; break;
							case ThreadUpdateStatus.NotModified:	threadStatus = "N"; break;
							case ThreadUpdateStatus.DoNotArchive:	threadStatus = "S"; break;
							case ThreadUpdateStatus.Error:			threadStatus = "E"; break;
							default:								threadStatus = "?"; break;
						}

						if (!result.Success)
						{
							lock (requeuedThreads)
								requeuedThreads.Add(nextThread);
						}

						// TODO: config option to requeue threads that have not been modified, up to a set amount of retries

						if (result.ImageDownloads.Count > 0)
						{
							foreach (var imageDownload in result.ImageDownloads)
								enqueuedImages.Enqueue(imageDownload);

							// Add detected images to the cache layer image collection
							await StateStore.InsertToDownloadQueue(new ReadOnlyCollection<QueuedImageDownload>(result.ImageDownloads));
						}

						// Log the status of the scraped thread
						Log.Information($"{"[Thread]",-9} {$"/{nextThread.Board}/{nextThread.ThreadId}",-17} {threadStatus} {$"+({result.ImageDownloads.Count}/{result.PostCountChange})",-13} [{enqueuedImages.Count}/{newCompletedCount}/{threadQueue.Count?.ToString() ?? "?"}]");
					});

					return true;
				}

				// This is our actual loop code for this task/thread.

				while (true)
				{
					workerStatuses[id] = "Idle";

					// Exit if user has requested cancellation
					if (token.IsCancellationRequested)
						break;
					
					if (await CheckImages())
						continue;

					// Scrape the next enqueued thread, and return back to the start of the loop
					if (await CheckThreads())
						continue;

					// If we're here, then it means that all threads have been scraped and only images remain.
					// Use our remaining non-marked tasks to help download the rest of the images
					if (await CheckImages())
						continue;

					// From the perspective of this worker, all threads and images have been downloaded.
					break;
				}

				// Debug logging
				Log.Verbose("Worker ID {idString} finished", idString);

				workerStatuses[id] = "Finished";

				// TODO: fix when introducing serilog
				//if (Program.HaydenConfig.DebugLogging)
				//{
				//	lock (workerStatuses)
				//		foreach (var kv in workerStatuses)
				//		{
				//			Program.Log($"ID {kv.Key,-2} => {kv.Value}", true);
				//		}
				//}
			}

			// Spawn each worker task and wait until they've all completed
			List<Task> workerTasks = new List<Task>();

			int id = 1;

			for (int i = 0; i < ProxyProvider.ProxyCount; i++)
			{
				workerTasks.Add(WorkerTask(id++, i % 3 == 0));
			}

			await Task.WhenAll(workerTasks);

			var secondsRemaining = (BoardUpdateTimespan - (DateTime.UtcNow - beginTime)).TotalSeconds;
			secondsRemaining = Math.Max(secondsRemaining, 0);

			Log.Information("");
			Log.Information($"Completed {threadCompletedCount} / {threadQueue.Count ?? threadCompletedCount} threads");
			Log.Information($"Waiting for next board update interval ({secondsRemaining:0.0}s)");
			Log.Information("");

			// Add any images that were previously requeued from failure
			foreach (var queuedImage in requeuedImages)
				enqueuedImages.Enqueue(queuedImage);

			requeuedImages.Clear();

			await StateStore.WriteDownloadQueue(enqueuedImages);

			Log.Debug($" --> Cleared queued image cache");

			firstRun = false;

			// A bit overkill but force a compacting GC collect here to make sure that the heap doesn't expand too much over time
			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect();

			await waitTask;

			return (requeuedThreads, enqueuedImages.ToList());
		}
		
		#region Network

		/// <summary>
		/// Reserve a proxy connection, and perform the action under the context of that proxy.
		/// </summary>
		/// <param name="action">The action to perform.</param>
		private async Task AsyncProxyCall(bool delay, Func<HttpClientProxy, Task> action)
		{
			await using var client = await ProxyProvider.RentHttpClient();

			Task threadWaitTask = null;

			if (delay)
				threadWaitTask = Task.Delay(ApiCooldownTimespan);

			try
			{
				await action(client.Object);
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Network operation failed, and was unhandled. Inconsistencies may arise in continued use of program");
			}

			if (threadWaitTask != null)
				await threadWaitTask;
		}

		#endregion

		#region Filter-related

		private bool ThreadFilter(string subject, string html, string board)
		{
			var rules = BoardRules[board];

			var result = false;

			if (rules.AnyBlacklist != null)
			{
				if (subject != null && rules.AnyBlacklist.IsMatch(subject))
					return false;

				if (html != null && rules.AnyBlacklist.IsMatch(html))
					return false;
			}

			if (rules.ThreadTitleRegex == null
				&& rules.OPContentRegex == null
				&& rules.AnyFilter == null)
				return true;

			if (!result && rules.ThreadTitleRegex != null && subject != null && rules.ThreadTitleRegex.IsMatch(subject))
				result = true;

			if (!result && rules.OPContentRegex != null
						&& html != null && rules.OPContentRegex.IsMatch(html))
				result = true;

			if (!result && rules.AnyFilter != null 
						&& ((html != null && rules.AnyFilter.IsMatch(html))
							|| (subject != null && rules.AnyFilter.IsMatch(subject))))
				result = true;

			return result;
		}

		private bool ThreadIdFilter(ThreadPointer threadPointer)
		{
			lock (ThreadIdBlacklist)
				return !ThreadIdBlacklist.Contains(threadPointer);
		}

		private void HandleThreadRemoval(ThreadPointer threadPointer)
		{
			lock (TrackedThreads)
				TrackedThreads.Remove(threadPointer);

			lock (ThreadIdBlacklist)
				if (ThreadIdBlacklist.Contains(threadPointer))
					ThreadIdBlacklist.Remove(threadPointer);
		}

		#endregion

		#region Worker

		/// <summary>
		/// Retrieves a list of threads that are present on the board's archive, but only ones updated after the specified time.
		/// </summary>
		/// <param name="token">Token to cancel the request.</param>
		/// <param name="board">The board to retrieve threads from.</param>
		/// <param name="lastDateTimeCheck">The time to compare the thread's updated time to.</param>
		/// <returns>A list of thread IDs.</returns>
		private async Task<IList<ThreadPointer>> GetArchivedBoardThreads(CancellationToken token, string board, DateTimeOffset lastDateTimeCheck)
		{
			var cooldownTask = Task.Delay(ApiCooldownTimespan, token);

			var threadQueue = new List<ThreadPointer>();

			var archiveRequest = await NetworkPolicies.GenericRetryPolicy<ApiResponse<ulong[]>>(12).ExecuteAsync(async () =>
			{
				token.ThrowIfCancellationRequested();
				await using var boardClient = await ProxyProvider.RentHttpClient();
				return await FrontendApi.GetArchive(board, boardClient.Object.Client, lastDateTimeCheck, token);
			});

			switch (archiveRequest.ResponseType)
			{
				case ResponseType.Ok:

					var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.Data, board, false, true);

					Log.Information("Found {existingArchivedThreadsCount} existing archived threads for board /{board}/", existingArchivedThreads.Count, board);

					// TODO: this is an optimization that needs a config flag for opting out,
					// i.e. if you change your filter options you might want to rescan all archived posts

					var lastKnownArchivedThread = existingArchivedThreads
						.Where(x => x.Archived)
						.OrderByDescending(x => x.ThreadId)
						.FirstOrDefault();

					var lastKnownArchivedThreadId = lastKnownArchivedThread.ThreadId; // this will default to 0 if none was found, which is what we want

					var filteredArchivedIds = archiveRequest.Data
						.Except(existingArchivedThreads.Select(x => x.ThreadId))
						.Where(x => x > lastKnownArchivedThreadId)
						.Select(x => new ThreadPointer(board, x))
						.Where(ThreadIdFilter)
						.ToArray();

					var filteredMaybeLiveThreads = existingArchivedThreads.Where(x => !x.Archived);

					foreach (var nonExistingThread in filteredArchivedIds)
					{
						threadQueue.Add(nonExistingThread);
					}

					foreach (var maybeLiveThreadInfo in filteredMaybeLiveThreads)
					{
						var pointer = new ThreadPointer(board, maybeLiveThreadInfo.ThreadId);

						threadQueue.Add(pointer);

						lock (TrackedThreads)
							TrackedThreads[pointer] = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, maybeLiveThreadInfo);
					}

					Log.Information("Enqueued {threadQueueCount} threads from board archive /{board}/", threadQueue.Count, board);

					break;

				case ResponseType.NotModified:
					break;

				case ResponseType.NotFound:
				default:
					Log.Warning("Unable to index the archive of board /{board}/, is there a connection error?", board);
					break;
			}

			await cooldownTask;

			return threadQueue;
		}

		/// <summary>
		/// Retrieves a list of threads that are present on the board, but only ones updated after the specified time.
		/// </summary>
		/// <param name="token">Token to cancel the request.</param>
		/// <param name="board">The board to retrieve threads from.</param>
		/// <param name="lastDateTimeCheck">The time to compare the thread's updated time to.</param>
		/// <param name="firstRun">True if this is the first cycle in the archival loop, otherwise false. Controls whether or not the database is called to find existing threads</param>
		/// <returns>A list of thread IDs.</returns>
		protected async Task<(MaybeAsyncEnumerable<ThreadPointer> enumerable, ulong? lastTimestamp)> GetBoardThreads(CancellationToken token, string board, DateTimeOffset lastDateTimeCheck, bool firstRun)
		{
			var cooldownTask = Task.Delay(ApiCooldownTimespan, token);

			MaybeAsyncEnumerable<ThreadPointer> threads = null;
			ulong? lastTimestamp = null;

			var pagesRequest = await NetworkPolicies.GenericRetryPolicy<ApiResponse<MaybeAsyncEnumerable<PageThread>>>(12).ExecuteAsync(async (requestToken) =>
			{
				requestToken.ThrowIfCancellationRequested();
				Log.Information("Requesting threads from board /{board}/...", board);
				await using var boardClient = await ProxyProvider.RentHttpClient();

				if (FrontendApi is IPaginatedFrontEndApi paginatedApi)
				{
					var response = await paginatedApi.GetBoardPaginated(board,
						boardClient.Object.Client,
						lastDateTimeCheck,
						requestToken);

					return new ApiResponse<MaybeAsyncEnumerable<PageThread>>(response.ResponseType,
						response.Data == null ? null : new MaybeAsyncEnumerable<PageThread>(response.Data));
				}

				var collectionResponse = await FrontendApi.GetBoard(board,
					boardClient.Object.Client,
					lastDateTimeCheck,
					requestToken);

				return new ApiResponse<MaybeAsyncEnumerable<PageThread>>(collectionResponse.ResponseType,
					collectionResponse.Data == null ? null : new MaybeAsyncEnumerable<PageThread>(collectionResponse.Data));
			}, token);

			switch (pagesRequest.ResponseType)
			{
				case ResponseType.Ok:

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetGMTTimestamp(lastDateTimeCheck);

					async IAsyncEnumerable<ThreadPointer> ProcessThreadPointers()
					{
						var allThreadIds = new HashSet<ulong>();

						// Flatten all threads.
						var threadList = pagesRequest.Data
							.Where(x =>
								ThreadIdFilter(new ThreadPointer(board,
									x.ThreadNumber)) // Exclude any that are already blacklisted
								&& ThreadFilter(x.Subject, x.Html, board)); // and exclude any that don't conform to our filter(s)
						
						await foreach (var thread in threadList)
						{
							if (firstRun)
							{
								// Check for threads that have already been downloaded by the consumer, noting the last time they were downloaded.
								// TODO: this should be batched to make it not bound to database calls, however it's a bit difficult here
								var existingThreads = await ThreadConsumer.CheckExistingThreads(new[] {thread.ThreadNumber}, //threadList.Select(x => x.ThreadNumber)
									board,
									false,
									true);

								bool skipThread = false;

								foreach (var existingThread in existingThreads)
								{
									// Skip threads that we 100% know haven't been changed since they were archived.
									// This is much less lenient than the last modified check down below, in a regular loop
									if (thread.LastModified <= Utility.GetGMTTimestamp(existingThread.LastPostTime) && thread.LastModified > 0)
									{
										skipThread = true;
										break;
									}

									// Start tracking the thread

									lock (TrackedThreads)
										TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] =
											TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existingThread);
								}

								if (skipThread)
									continue;
							}

							allThreadIds.Add(thread.ThreadNumber);

							// Perform a last modified time check, remove any threads that have not changed since the last time we've checked (passed in via lastCheckTimestamp)
							if (thread.LastModified < lastCheckTimestamp && thread.LastModified > 0)
							{
								Log.Verbose("Thread /{board}/{threadId} has not changed (timestamp {timestamp}, last {lastCheckTimestamp}, current {currentTimestamp})",
									board, thread.ThreadNumber, thread.LastModified, lastCheckTimestamp, Utility.GetGMTTimestamp(DateTimeOffset.Now));

								continue;
							}

							Log.Verbose("Thread /{board}/{threadId} has changed (timestamp {timestamp}, last {lastCheckTimestamp}, current {currentTimestamp})",
								board, thread.ThreadNumber, thread.LastModified, lastCheckTimestamp, Utility.GetGMTTimestamp(DateTimeOffset.Now));

							yield return new ThreadPointer(board, thread.ThreadNumber);
						}
						
						// Examine the threads we are tracking to find any that have fallen off.
						// This is the only way we can find out if a thread has been archived or deleted in this polling model
						IEnumerable<KeyValuePair<ThreadPointer, TrackedThread>> missingTrackedThreads;

						lock (TrackedThreads)
							missingTrackedThreads = TrackedThreads
								.Where(x => x.Key.Board == board && !allThreadIds.Contains(x.Key.ThreadId))
								.ToArray();

						// This thread is missing from the board listing, but the last time we checked it it was still alive.
						// Add it to the re-examination queue
						foreach (var missingThread in missingTrackedThreads)
						{
							Log.Debug("Thread /{board}/{threadId} has been detected as missing",
								board, missingThread.Key.ThreadId);

							yield return missingThread.Key;
						}
					}

					var threadList = ProcessThreadPointers();

					if (pagesRequest.Data.IsListBacked)
					{
						var computedThreads = await threadList.ToListAsync();

						lastTimestamp = await pagesRequest.Data.MaxAsync(x => x.LastModified);

						Log.Information("Enqueued {computedThreadsCount} threads from board /{board}/ past timestamp {lastCheckTimestamp}",
							computedThreads.Count, board, lastCheckTimestamp);

						threads = new MaybeAsyncEnumerable<ThreadPointer>(computedThreads);
					}
					else
					{
						threads = new MaybeAsyncEnumerable<ThreadPointer>(threadList);
					}
					
					break;

				case ResponseType.NotModified:
					Log.Information($"Board /{board}/ has not changed");
					// There are no updates for this board
					break;

				case ResponseType.NotFound:
				default:
					Log.Warning($"Unable to index board /{board}/, is there a connection error?");
					break;
			}

			await cooldownTask;

			return (threads, lastTimestamp);
		}

		protected virtual async Task<ApiResponse<Thread>> RetrieveThreadAsync(ThreadPointer pointer, HttpClientProxy client,
			CancellationToken token)
		{
			//Program.Log($"{workerId,-2}: Polling thread /{board}/{threadNumber}", true);
			return await FrontendApi.GetThread(pointer.Board, pointer.ThreadId, client.Client, null, token);
		}

		/// <summary>
		/// Polls a thread, and passes it to the consumer if the thread has been detected as updated.
		/// </summary>
		/// <param name="token">The cancellation token associated with this request.</param>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread to poll.</param>
		/// <param name="apiResponse"></param>
		/// <returns></returns>
		private async Task<ThreadUpdateTaskResult> ThreadUpdateTask(CancellationToken token, string workerId, string board, ulong threadNumber, ApiResponse<Thread> apiResponse)
		{
			try
			{
				// We should be passing in the last scrape time here, but I don't remember why we don't
				// I think it's because we only get to this point when we know for sure that the thread has changed?
				// Or maybe there are properties that *can* change without updating last_modified
				var response = apiResponse;

				token.ThrowIfCancellationRequested();

				switch (response.ResponseType)
				{
					case ResponseType.Ok:

						var threadPointer = new ThreadPointer(board, threadNumber);

						var opPost = response.Data.Posts.FirstOrDefault();

						if (response.Data != null && !ThreadFilter(response.Data.Title, opPost?.ContentRendered ?? opPost?.ContentRaw, board))
						{
							Log.Debug($"{workerId,-2}: Blacklisting thread /{board}/{threadNumber} due to title filter");
							
							lock (ThreadIdBlacklist)
								if (!ThreadIdBlacklist.Contains(threadPointer))
									ThreadIdBlacklist.Add(threadPointer);

							return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.DoNotArchive, 0);
						}

						Log.Debug($"{workerId,-2}: Downloading changes from thread /{board}/{threadNumber}");


						if (response.Data == null
							|| response.Data.Posts.Length == 0
							|| response.Data.Posts.FirstOrDefault() == null
							|| response.Data.Posts[0].PostNumber == 0)
						{
							// This is a very strange edge case.
							// The 4chan API can return a malformed thread object if the thread has been (incorrectly?) deleted
							
							// For example, this is the JSON returned by post /g/83700099 after it was removed for DMCA infringement:
							// {"posts":[{"resto":0,"replies":0,"images":0,"unique_ips":1,"semantic_url":null}]}
							
							// If it's returning this then the assumption is that the thread has been deleted

							Log.Warning($"Thread /{board}/{threadNumber} is malformed (DMCA?)");

							HandleThreadRemoval(threadPointer);
							await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

							return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Deleted, 0);
						}

						// Process the thread data with its assigned TrackedThread instance, then pass the results to the consumer

						TrackedThread trackedThread;

						bool isNewThread;

						lock (TrackedThreads)
							isNewThread = !TrackedThreads.TryGetValue(threadPointer, out trackedThread);
						
						bool isTracked = !isNewThread;

						if (isNewThread)
						{
							// this is a brand new thread that hasn't been tracked yet
							// check if the thread already exists in the database, just to be sure

							var existingThread = await ThreadConsumer.CheckExistingThreads(new[] { threadPointer.ThreadId },
								threadPointer.Board,
								false,
								true);

							if (existingThread.Count > 0)
							{
								trackedThread = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existingThread.First());
								isNewThread = false;
							}
							else
								trackedThread = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash);

							if (!SourceConfig.SingleScan)
								lock (TrackedThreads)
									TrackedThreads[threadPointer] = trackedThread;
						}

						Log.Debug($"/{board}/{threadNumber} new thread? {isNewThread}, previously tracked? {isTracked}");

						var threadUpdateInfo = trackedThread.ProcessThreadUpdates(threadPointer, response.Data);

						Log.Verbose($"{workerId,-2}: Thread /{board}/{threadNumber}: New {threadUpdateInfo.NewPosts.Count} / updated {threadUpdateInfo.UpdatedPosts.Count} / deleted {threadUpdateInfo.DeletedPosts.Count}");

						threadUpdateInfo.IsNewThread = isNewThread;

						if (!threadUpdateInfo.HasChanges && !threadUpdateInfo.Thread.IsArchived)
						{
							return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.NotModified, 0);
						}
						
						// TODO: handle failures from this call
						// Right now if this call fails, Hayden's state will assume that it has succeeded because the
						//   TrackedThread instance's state hasn't rolled back

						var images = await ThreadConsumer.ConsumeThread(threadUpdateInfo);

						if (response.Data.IsArchived)
						{
							Log.Debug($"{workerId,-2}: Thread /{board}/{threadNumber} has been archived");

							HandleThreadRemoval(threadPointer);
							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						return new ThreadUpdateTaskResult(true,
							images,
							response.Data.IsArchived ? ThreadUpdateStatus.Archived : ThreadUpdateStatus.Ok,
							threadUpdateInfo.NewPosts.Count - threadUpdateInfo.DeletedPosts.Count);

					case ResponseType.NotModified:
						// There are no updates for this thread
						return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.NotModified, 0);

					case ResponseType.NotFound:
						// This thread returned a 404, indicating a deletion

						Log.Debug($"{workerId,-2}: Thread /{board}/{threadNumber} has been pruned or deleted");

						HandleThreadRemoval(new ThreadPointer(board, threadNumber));
						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

						return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Deleted, 0);

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Log.Error(exception, $"Could not poll or update thread /{board}/{threadNumber}. Will try again next board update");

				return new ThreadUpdateTaskResult(false, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Error, 0);
			}
		}

		/// <summary>
		/// Creates a task to download an image to a specified path, using a specific HttpClient. Skips if the file already exists.
		/// </summary>
		/// <param name="imageUrl">The <see cref="Uri"/> of the image.</param>
		/// <param name="httpClient">The client to use for the request.</param>
		private async Task<string> DownloadFileTask(Uri imageUrl, HttpClient httpClient)
		{
			Log.Debug("Downloading image {filename}", imageUrl.Segments.Last());
			
			using var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(() =>
				{
					var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);

					if (imageUrl.Host == "8chan.moe")
					{
						// dumb bot check
						request.Headers.Add("Cookie", "splash=1");
					}

					return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
				})
				.ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return null;

			response.EnsureSuccessStatusCode();

			var tempFilePath = FileSystem.Path.Combine(ConsumerConfig.DownloadLocation, "hayden", Guid.NewGuid().ToString("N") + ".temp");
			
			await using (var webStream = await response.Content.ReadAsStreamAsync())
			await using (var tempFileStream = FileSystem.FileStream.Create(tempFilePath, System.IO.FileMode.CreateNew))
			{
				await webStream.CopyToAsync(tempFileStream);
			}

			return tempFilePath;
		}

		#endregion
	}
}