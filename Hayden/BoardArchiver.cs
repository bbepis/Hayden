using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Cache;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using Hayden.Proxy;

namespace Hayden
{
	/// <summary>
	/// Handles the core archival logic, independent of any API or consumer implementations.
	/// </summary>
	public class BoardArchiver
	{
		/// <summary>
		/// Configuration for the Yotsuba API given by the constructor.
		/// </summary>
		public YotsubaConfig Config { get; }

		protected IThreadConsumer ThreadConsumer { get; }
		protected IStateStore StateStore { get; }
		protected ProxyProvider ProxyProvider { get; }

		protected List<ThreadPointer> ThreadIdBlacklist { get; } = new();
		protected SortedList<ThreadPointer, TrackedThread> TrackedThreads { get; } = new();

		protected Dictionary<string, BoardRules> BoardRules { get; } = new();

		/// <summary>
		/// The minimum amount of time the archiver should wait before checking the boards for any thread updates.
		/// </summary>
		public TimeSpan BoardUpdateTimespan { get; set; }

		/// <summary>
		/// The minimum amount of time that should be waited in-between API calls.
		/// </summary>
		public TimeSpan ApiCooldownTimespan { get; set; }

		public BoardArchiver(YotsubaConfig config, IThreadConsumer threadConsumer, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		{
			Config = config;
			ThreadConsumer = threadConsumer;
			ProxyProvider = proxyProvider ?? new NullProxyProvider();
			StateStore = stateStore ?? new NullStateStore();

			ApiCooldownTimespan = TimeSpan.FromSeconds(config.ApiDelay ?? 1);
			BoardUpdateTimespan = TimeSpan.FromSeconds(config.BoardScrapeDelay ?? 30);

			foreach (var (board, boardConfig) in config.Boards)
			{
				BoardRules[board] = new BoardRules(boardConfig);
			}
		}

		/// <summary>
		/// Performs the main archival loop.
		/// </summary>
		/// <param name="token">Token to safely cancel the execution.</param>
		public async Task Execute(CancellationToken token)
		{
			bool firstRun = true;

			// We always add the current, non-proxy connection as an available connection to use
			var imageDownloadClient = new HttpClientProxy(ProxyProvider.CreateNewClient(), "baseconnection/image");

			List<ThreadPointer> threadQueue = new List<ThreadPointer>();
			ConcurrentQueue<QueuedImageDownload> enqueuedImages = new ConcurrentQueue<QueuedImageDownload>();
			List<QueuedImageDownload> requeuedImages = new List<QueuedImageDownload>();
			
			// These keep track of the last time we scraped a board, and therefore determines which threads we should scrape
			SortedList<string, DateTimeOffset> lastBoardCheckTimes = new SortedList<string, DateTimeOffset>(Config.Boards.Count);

			// We only loop if cancellation has not been requested (i.e. "Q" has not been pressed)
			// Every time you see "token" mentioned, its performing a check

			while (!token.IsCancellationRequested)
			{
				int currentBoardCount = 0;

				// For each board (maximum of 8 concurrently), retrieve a list of threads that need to be scraped
				await Config.Boards.Keys.ForEachAsync(8, async board =>
				{
					token.ThrowIfCancellationRequested();

					DateTimeOffset lastDateTimeCheck;

					lock (lastBoardCheckTimes)
						if (!lastBoardCheckTimes.TryGetValue(board, out lastDateTimeCheck))
							lastDateTimeCheck = DateTimeOffset.MinValue;

					// Set this time now before we do the network calls, as it's safer and we won't miss any threads that update in-between
					DateTimeOffset beforeCheckTime = DateTimeOffset.Now;
					
					// Get a list of threads to be scraped
					var (modifiedThreads, allThreads) = await GetBoardThreads(token, board, lastDateTimeCheck, firstRun);

					lock (threadQueue)
						threadQueue.AddRange(modifiedThreads);

					// If allThreads is null, then the board hasn't changed since we last checked
					if (allThreads != null)
					{
						// Examine the threads we are tracking to find any that have fallen off.
						// This is the only way we can find out if a thread has been archived or deleted in this polling model

						// Locking on the tracking sortedlist is not required since all operations at this point in time are read-only

						var missingTrackedThreads = TrackedThreads.Where(x => x.Key.Board == board && !allThreads.Contains(x.Key));

						foreach (var (pointer, _) in missingTrackedThreads)
						{
							// This thread is missing from the board listing, but the last time we checked it it was still alive.
							// Add it to the re-examination queue
							threadQueue.Add(pointer);
						}
					}

					if (firstRun && Config.ReadArchive)
					{
						// Get a list of archived threads to include to be scraped.
						var archivedThreads = await GetArchivedBoardThreads(token, board, lastDateTimeCheck);

						lock (threadQueue)
							threadQueue.AddRange(archivedThreads);
					}

					lock (lastBoardCheckTimes)
					{
						lastBoardCheckTimes[board] = beforeCheckTime;

						if (++currentBoardCount % 5 == 0 || currentBoardCount == Config.Boards.Count)
						{
							Program.Log($"{currentBoardCount} / {Config.Boards.Count} boards polled");
						}
					}
				});

				if (token.IsCancellationRequested)
					break;

				Program.Log($"{threadQueue.Count} threads have been queued total");
				threadQueue.TrimExcess();

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
					
					if (File.Exists(queuedDownload.DownloadPath))
					{
						// The file already exists. Return here after incrementing the amount of downloaded images
						return Interlocked.Increment(ref imageCompletedCount);
					}

					// Ensure that an image download takes at least as long as 100ms
					// We don't want to download images too fast
					var waitTask = Task.Delay(100, token);

					try
					{
						// Perform the actual download.
						await DownloadFileTask(queuedDownload.DownloadUri, queuedDownload.DownloadPath, client.Client);
					}
					catch (Exception ex)
					{
						// Errored out. Log it and requeue the image
						Program.Log($"ERROR: Could not download image. Will try again next board update\nClient name: {client.Name}\nException: {ex}");

						lock (requeuedImages)
							requeuedImages.Add(queuedDownload);
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

					Program.Log($"{enqueuedImages.Count} media items loaded from queue cache");
				}

				// Add any images that were previously requeued from failure
				foreach (var queuedImage in requeuedImages)
					enqueuedImages.Enqueue(queuedImage);

				requeuedImages.Clear();

				threadQueue = threadQueue
					.Where(x => x.Board != null && x != default) // Very rarely a null value can slip into here. Not sure why, but just added for safety
					.ToList();

				// We create a round-robin queue that each worker task/thread is able to consume.
				// Round-robin is used specifically since we want to balance out downloaded threads per boards, otherwise it would be downloading a single board at a time
				using var roundRobinQueue = threadQueue
					.RoundRobin(x => x.Board)
					.GetEnumerator();

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

						workerStatuses[id] = $"Downloading image {nextImage.DownloadUri}";

						int completedCount = await DownloadEnqueuedImage(imageDownloadClient, nextImage);

						if (completedCount % 10 == 0 || enqueuedImages.Count == 0)
						{
							Program.Log($"{"[Image]",-9} [{completedCount}/{enqueuedImages.Count}]");
						}

						return true;
					}

					// The unit of work involving checking if any threads are available, and downloading a single one + enqueuing those images.
					async Task<bool> CheckThreads()
					{
						bool success;
						ThreadPointer nextThread;
						
						// Grab the next thread from the round robin queue. Complex because we want to do this in a thread-safe way
						lock (roundRobinQueue)
						{
							success = roundRobinQueue.MoveNext();
							nextThread = roundRobinQueue.Current;
						}

						if (!success)
							// Exit if there are no threads available
							return false;

						workerStatuses[id] = $"Scraping thread /{nextThread.Board}/{nextThread.ThreadId}";
						
						bool outerSuccess = true;

						// Add a timeout for the scrape to 2 minutes, so it doesn't hang forever
						using var timeoutToken = new CancellationTokenSource(TimeSpan.FromMinutes(2));

						await AsyncProxyCall(async client =>
						{
							var result = await ThreadUpdateTask(timeoutToken.Token, idString, nextThread.Board, nextThread.ThreadId, client);

							int newCompletedCount = Interlocked.Increment(ref threadCompletedCount);

							string threadStatus;

							switch (result.Status)
							{
								case ThreadUpdateStatus.Ok:           threadStatus = " "; break;
								case ThreadUpdateStatus.Archived:     threadStatus = "A"; break;
								case ThreadUpdateStatus.Deleted:      threadStatus = "D"; break;
								case ThreadUpdateStatus.NotModified:  threadStatus = "N"; break;
								case ThreadUpdateStatus.DoNotArchive: threadStatus = "S"; break;
								case ThreadUpdateStatus.Error:        threadStatus = "E"; break;
								default:                              threadStatus = " "; break;
							}

							if (!success)
							{
								lock (requeuedThreads)
									requeuedThreads.Add(nextThread);

								outerSuccess = false;
								return;
							}

							if (result.ImageDownloads.Count > 0)
							{
								foreach (var imageDownload in result.ImageDownloads)
									enqueuedImages.Enqueue(imageDownload);

								// Add detected images to the cache layer image collection
								await StateStore.InsertToDownloadQueue(new ReadOnlyCollection<QueuedImageDownload>(result.ImageDownloads));
							}

							// Log the status of the scraped thread
							Program.Log($"{"[Thread]",-9} {$"/{nextThread.Board}/{nextThread.ThreadId}",-17} {threadStatus} {$"+({result.ImageDownloads.Count}/{result.PostCountChange})",-13} [{enqueuedImages.Count}/{newCompletedCount}/{threadQueue.Count}]");
						});

						return outerSuccess;
					}

					// This is our actual loop code for this task/thread.

					while (true)
					{
						workerStatuses[id] = "Idle";

						// Exit if user has requested cancellation
						if (token.IsCancellationRequested)
							break;

						// If this task has been marked to prioritize images, download them immediately
						if (prioritizeImages)
						{
							if (await CheckImages())
								continue;
						}

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
					Program.Log($"Worker ID {idString} finished", true);

					workerStatuses[id] = "Finished";

					if (Program.HaydenConfig.DebugLogging)
					{
						lock (workerStatuses)
							foreach (var kv in workerStatuses)
							{
								Program.Log($"ID {kv.Key,-2} => {kv.Value}", true);
							}
					}
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

				Program.Log("");
				Program.Log($"Completed {threadCompletedCount} / {threadQueue.Count} threads");
				Program.Log($"Waiting for next board update interval ({secondsRemaining:0.0}s)");
				Program.Log("");


				await StateStore.WriteDownloadQueue(enqueuedImages);

				Program.Log($" --> Cleared queued image cache", true);

				firstRun = false;

				// A bit overkill but force a compacting GC collect here to make sure that the heap doesn't expand too much over time
				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect();


				threadQueue.Clear();

				threadQueue.AddRange(requeuedThreads);

				await waitTask;
			}
		}
		
		#region Network

		/// <summary>
		/// Reserve a proxy connection, and perform the action under the context of that proxy.
		/// </summary>
		/// <param name="action">The action to perform.</param>
		private async Task AsyncProxyCall(Func<HttpClientProxy, Task> action)
		{
			await using var client = await ProxyProvider.RentHttpClient();

			var threadWaitTask = Task.Delay(ApiCooldownTimespan);

			try
			{
				await action(client.Object);
			}
			catch (Exception ex)
			{
				Program.Log($"ERROR: Network operation failed, and was unhandled. Inconsistencies may arise in continued use of program\r\n" + ex.ToString());
			}

			await threadWaitTask;
		}

		#endregion

		#region Filter-related

		private bool ThreadFilter(string subject, string html, string board)
		{
			var rules = BoardRules[board];

			var result = false;

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
				return await YotsubaApi.GetArchive(board, boardClient.Object.Client, lastDateTimeCheck, token);
			});

			switch (archiveRequest.ResponseType)
			{
				case ResponseType.Ok:

					var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.Data, board, true, false);

					Program.Log($"Found {existingArchivedThreads.Count} existing archived threads for board /{board}/");

					var filteredIds = archiveRequest.Data
						.Except(existingArchivedThreads.Select(x => x.ThreadId))
						.Where(x => ThreadIdFilter(new ThreadPointer(board, x)));

					foreach (ulong nonExistingThreadId in filteredIds)
					{
						threadQueue.Add(new ThreadPointer(board, nonExistingThreadId));
					}

					Program.Log($"Enqueued {threadQueue.Count} threads from board archive /{board}/");

					break;

				case ResponseType.NotModified:
					break;

				case ResponseType.NotFound:
				default:
					Program.Log($"Unable to index the archive of board /{board}/, is there a connection error?");
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
		public async Task<(IList<ThreadPointer> updatedThreads, IList<ThreadPointer> allThreads)> GetBoardThreads(CancellationToken token, string board, DateTimeOffset lastDateTimeCheck, bool firstRun)
		{
			var cooldownTask = Task.Delay(ApiCooldownTimespan, token);

			var threads = new List<ThreadPointer>();
			List<ThreadPointer> allThreads = null;

			var pagesRequest = await NetworkPolicies.GenericRetryPolicy<ApiResponse<Page[]>>(12).ExecuteAsync(async () =>
			{
				token.ThrowIfCancellationRequested();
				Program.Log($"Requesting threads from board /{board}/...");
				await using var boardClient = await ProxyProvider.RentHttpClient();
				return await YotsubaApi.GetBoard(board,
					boardClient.Object.Client,
					lastDateTimeCheck,
					token);
			});

			switch (pagesRequest.ResponseType)
			{
				case ResponseType.Ok:

					// Flatten all threads.
					var threadList = pagesRequest.Data.SelectMany(x => x.Threads)
						.Where(x => ThreadIdFilter(new ThreadPointer(board, x.ThreadNumber)) // Exclude any that are already blacklisted
									&& ThreadFilter(x.Subject, x.Html, board)) // and exclude any that don't conform to our filter(s)
						.ToList();

					if (firstRun)
					{
						// Check for threads that have already been downloaded by the consumer, noting the last time they were downloaded.
						var existingThreads = await ThreadConsumer.CheckExistingThreads(threadList.Select(x => x.ThreadNumber),
							board,
							false,
							true);

						foreach (var existingThread in existingThreads)
						{
							var thread = threadList.First(x => x.ThreadNumber == existingThread.ThreadId);

							// Only remove threads to be downloaded if the downloaded thread is already up-to-date by comparing last post times
							// This can't be done below as "last post time" is different to "last modified time"
							if (thread.LastModified <= Utility.GetGMTTimestamp(existingThread.LastPostTime))
							{
								threadList.Remove(thread);
							}

							// Start tracking the thread

							lock (TrackedThreads)
								TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] = TrackedThread.StartTrackingThread(existingThread);
						}
					}

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetGMTTimestamp(lastDateTimeCheck);

					foreach (var thread in threadList)
					{
						// Perform a last modified time check, remove any threads that have not changed since the last time we've checked (passed in via lastCheckTimestamp)
						if (thread.LastModified > lastCheckTimestamp)
						{
							threads.Add(new ThreadPointer(board, thread.ThreadNumber));
						}
					}

					allThreads = threadList.Select(x => new ThreadPointer(board, x.ThreadNumber)).ToList();

					Program.Log($"Enqueued {threads.Count} threads from board /{board}/ past timestamp {lastCheckTimestamp}");

					break;

				case ResponseType.NotModified:
					Program.Log($"Board /{board}/ has not changed");
					// There are no updates for this board
					break;

				case ResponseType.NotFound:
				default:
					Program.Log($"Unable to index board /{board}/, is there a connection error?");
					break;
			}

			await cooldownTask;

			return (threads, allThreads);
		}

		// Cache this instead of having to create a new one every time we want to return an empty array
		private static readonly QueuedImageDownload[] emptyImageQueue = new QueuedImageDownload[0];

		/// <summary>
		/// Polls a thread, and passes it to the consumer if the thread has been detected as updated.
		/// </summary>
		/// <param name="token">The cancellation token associated with this request.</param>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread to poll.</param>
		/// <param name="client">The <see cref="HttpClientProxy"/> to use for the poll request.</param>
		/// <returns></returns>
		private async Task<ThreadUpdateTaskResult> ThreadUpdateTask(CancellationToken token, string workerId, string board, ulong threadNumber, HttpClientProxy client)
		{
			try
			{
				Program.Log($"{workerId,-2}: Polling thread /{board}/{threadNumber}", true);

				// We should be passing in the last scrape time here, but I don't remember why we don't
				// I think it's because we only get to this point when we know for sure that the thread has changed?
				// Or maybe there are properties that *can* change without updating last_modified
				var response = await YotsubaApi.GetThread(board, threadNumber, client.Client, null, token);

				token.ThrowIfCancellationRequested();

				switch (response.ResponseType)
				{
					case ResponseType.Ok:

						var threadPointer = new ThreadPointer(board, threadNumber);

						if (response.Data != null && !ThreadFilter(response.Data.OriginalPost?.Subject, response.Data.OriginalPost?.Comment, board))
						{
							Program.Log($"{workerId,-2}: Blacklisting thread /{board}/{threadNumber} due to title filter", true);
							
							lock (ThreadIdBlacklist)
								if (!ThreadIdBlacklist.Contains(threadPointer))
									ThreadIdBlacklist.Add(threadPointer);

							return new ThreadUpdateTaskResult(true, emptyImageQueue, ThreadUpdateStatus.DoNotArchive, 0);
						}

						Program.Log($"{workerId,-2}: Downloading changes from thread /{board}/{threadNumber}", true);


						if (response.Data == null
						    || response.Data.Posts.Count == 0
						    || response.Data.OriginalPost == null
						    || response.Data.OriginalPost.PostNumber == 0)
						{
							// This is a very strange edge case.
							// The 4chan API can return a malformed thread object if the thread has been (incorrectly?) deleted
							
							// For example, this is the JSON returned by post /g/83700099 after it was removed for DMCA infringement:
							// {"posts":[{"resto":0,"replies":0,"images":0,"unique_ips":1,"semantic_url":null}]}
							
							// If it's returning this then the assumption is that the thread has been deleted

							Program.Log($"{workerId,-2}: Thread /{board}/{threadNumber} is malformed (DMCA?)", true);

							HandleThreadRemoval(threadPointer);
							await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

							return new ThreadUpdateTaskResult(true, emptyImageQueue, ThreadUpdateStatus.Deleted, 0);
						}

						// Process the thread data with its assigned TrackedThread instance, then pass the results to the consumer

						TrackedThread trackedThread;

						bool isNewThread = false;

						lock (TrackedThreads)
							if (!TrackedThreads.TryGetValue(threadPointer, out trackedThread))
							{
								// this is a brand new thread that hasn't been tracked yet
								trackedThread = TrackedThread.StartTrackingThread();
								TrackedThreads[threadPointer] = trackedThread;
								isNewThread = true;
							}

						var threadUpdateInfo = trackedThread.ProcessThreadUpdates(threadPointer, response.Data);
						threadUpdateInfo.IsNewThread = isNewThread;

						if (!threadUpdateInfo.HasChanges)
						{
							// This should be safe when a thread becomes archived, because that archive bit flip should be counted as a change as well

							return new ThreadUpdateTaskResult(true, emptyImageQueue, ThreadUpdateStatus.NotModified, 0);
						}
						
						// TODO: handle failures from this call
						// Right now if this call fails, Hayden's state will assume that it has succeeded because the
						//   TrackedThread instance's state hasn't rolled back

						var images = await ThreadConsumer.ConsumeThread(threadUpdateInfo);

						if (response.Data.OriginalPost.Archived == true)
						{
							Program.Log($"{workerId,-2}: Thread /{board}/{threadNumber} has been archived", true);

							HandleThreadRemoval(threadPointer);
							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						return new ThreadUpdateTaskResult(true,
							images,
							response.Data.OriginalPost.Archived == true ? ThreadUpdateStatus.Archived : ThreadUpdateStatus.Ok,
							threadUpdateInfo.NewPosts.Count - threadUpdateInfo.DeletedPosts.Count);

					case ResponseType.NotModified:
						// There are no updates for this thread
						return new ThreadUpdateTaskResult(true, emptyImageQueue, ThreadUpdateStatus.NotModified, 0);

					case ResponseType.NotFound:
						// This thread returned a 404, indicating a deletion

						Program.Log($"{workerId,-2}: Thread /{board}/{threadNumber} has been pruned or deleted", true);

						HandleThreadRemoval(new ThreadPointer(board, threadNumber));
						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

						return new ThreadUpdateTaskResult(true, emptyImageQueue, ThreadUpdateStatus.Deleted, 0);

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Program.Log($"ERROR: Could not poll or update thread /{board}/{threadNumber}. Will try again next board update\nClient name: {client.Name}\nException: {exception}");

				return new ThreadUpdateTaskResult(false, emptyImageQueue, ThreadUpdateStatus.Error, 0);
			}
		}

		/// <summary>
		/// Creates a task to download an image to a specified path, using a specific HttpClient. Skips if the file already exists.
		/// </summary>
		/// <param name="imageUrl">The <see cref="Uri"/> of the image.</param>
		/// <param name="downloadPath">The filepath to download the image to.</param>
		/// <param name="httpClient">The client to use for the request.</param>
		private async Task DownloadFileTask(Uri imageUrl, string downloadPath, HttpClient httpClient)
		{
			if (File.Exists(downloadPath))
				return;

			Program.Log($"Downloading image {Path.GetFileName(downloadPath)}", true);

			var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
			using var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(
				() => httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
				.ConfigureAwait(false);

			await using (var webStream = await response.Content.ReadAsStreamAsync())
			await using (var fileStream = new FileStream(downloadPath + ".part", FileMode.Create))
			{
				await webStream.CopyToAsync(fileStream);
			}

			File.Move(downloadPath + ".part", downloadPath);
		}

		#endregion
	}

	public class QueuedImageDownload
	{
		public Uri DownloadUri { get; set; }

		public string DownloadPath { get; set; }

		public QueuedImageDownload() { }

		public QueuedImageDownload(Uri downloadUri, string downloadPath)
		{
			DownloadUri = downloadUri;
			DownloadPath = downloadPath;
		}

		protected bool Equals(QueuedImageDownload other)
		{
			return DownloadUri.AbsolutePath == other.DownloadUri.AbsolutePath
				   && DownloadPath == other.DownloadPath;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj))
				return false;
			if (ReferenceEquals(this, obj))
				return true;
			if (obj.GetType() != GetType())
				return false;
			return Equals((QueuedImageDownload)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return ((DownloadUri != null ? DownloadUri.GetHashCode() : 0) * 397) ^ (DownloadPath != null ? DownloadPath.GetHashCode() : 0);
			}
		}
	}

	/// <summary>
	/// A struct containing information about a specific thread.
	/// </summary>
	public readonly struct ThreadPointer : IComparable<ThreadPointer>, IEquatable<ThreadPointer>
	{
		public string Board { get; }

		public ulong ThreadId { get; }

		public ThreadPointer(string board, ulong threadId)
		{
			Board = board;
			ThreadId = threadId;
		}

		#region Interface boilerplate

		public int CompareTo(ThreadPointer other)
		{
			int result = Board.CompareTo(other.Board);

			if (result != 0)
				return result;

			return ThreadId.CompareTo(other.ThreadId);
		}

		public bool Equals(ThreadPointer other)
		{
			return Board == other.Board && ThreadId == other.ThreadId;
		}

		public override bool Equals(object obj)
		{
			return obj is ThreadPointer other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Board, ThreadId);
		}

		public static bool operator ==(ThreadPointer left, ThreadPointer right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(ThreadPointer left, ThreadPointer right)
		{
			return !left.Equals(right);
		}
		
		#endregion
	}

	public enum ThreadUpdateStatus
	{
		Ok,
		Deleted,
		Archived,
		NotModified,
		DoNotArchive,
		Error
	}

	public struct ThreadUpdateTaskResult
	{
		public bool Success { get; set; }
		public IList<QueuedImageDownload> ImageDownloads { get; set; }
		public ThreadUpdateStatus Status { get; set; }
		public int PostCountChange { get; set; }

		public ThreadUpdateTaskResult(bool success, IList<QueuedImageDownload> imageDownloads, ThreadUpdateStatus status, int postCountChange)
		{
			Success = success;
			ImageDownloads = imageDownloads;
			Status = status;
			PostCountChange = postCountChange;
		}
	}

	public class BoardRules
	{
		public Regex ThreadTitleRegex { get; set; }
		public Regex OPContentRegex { get; set; }
		public Regex AnyFilter { get; set; }

		public BoardRules(BoardRulesConfig config)
		{
			if (!string.IsNullOrWhiteSpace(config.ThreadTitleRegexFilter))
			{
				ThreadTitleRegex = new Regex(config.ThreadTitleRegexFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}

			if (!string.IsNullOrWhiteSpace(config.OPContentRegexFilter))
			{
				OPContentRegex = new Regex(config.OPContentRegexFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}

			if (!string.IsNullOrWhiteSpace(config.AnyFilter))
			{
				AnyFilter = new Regex(config.AnyFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}
		}
	}
}