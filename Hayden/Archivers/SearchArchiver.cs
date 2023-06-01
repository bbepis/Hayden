using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Cache;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Proxy;
using Serilog;

namespace Hayden
{
	/// <summary>
	/// Handles the core archival logic, independent of any API or consumer implementations.
	/// </summary>
	public class SearchArchiver
	{
		public SourceConfig SourceConfig { get; }
		public ConsumerConfig ConsumerConfig { get; }

		protected IThreadConsumer ThreadConsumer { get; }
		protected ISearchableFrontendApi FrontendApi { get; }
		protected IStateStore StateStore { get; }
		protected ProxyProvider ProxyProvider { get; }
		
		/// <summary>
		/// The minimum amount of time the archiver should wait before checking the boards for any thread updates.
		/// </summary>
		public TimeSpan BoardUpdateTimespan { get; set; }

		/// <summary>
		/// The minimum amount of time that should be waited in-between API calls.
		/// </summary>
		public TimeSpan ApiCooldownTimespan { get; set; }

		public SearchArchiver(SourceConfig sourceConfig, ConsumerConfig consumerConfig, 
			ISearchableFrontendApi frontendApi, IThreadConsumer threadConsumer,
			IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		{
			SourceConfig = sourceConfig;
			ConsumerConfig = consumerConfig;

			FrontendApi = frontendApi;
			ThreadConsumer = threadConsumer;
			ProxyProvider = proxyProvider ?? new NullProxyProvider();
			StateStore = stateStore ?? new NullStateStore();

			ApiCooldownTimespan = TimeSpan.FromSeconds(sourceConfig.ApiDelay ?? 1);
			BoardUpdateTimespan = TimeSpan.FromSeconds(sourceConfig.BoardScrapeDelay ?? 30);
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

			ConcurrentQueue<QueuedImageDownload> enqueuedImages = new ConcurrentQueue<QueuedImageDownload>();
			List<QueuedImageDownload> requeuedImages = new List<QueuedImageDownload>();

			var (totalSearchCount, searchEnumerable) = await FrontendApi.PerformSearch(new SearchQuery { Board = "g", TextQuery = "home server" }, imageDownloadClient.Client, token);
			var searchEnumerator = searchEnumerable.GetAsyncEnumerator(token);

			var totalSearchCountString = totalSearchCount != null ? $"~{totalSearchCount}" : "?";

			var semaphore = new SemaphoreSlim(1);

			async ValueTask<ThreadPointer?> EnumerateNextPost()
			{
				try
				{
					await semaphore.WaitAsync(token);

					if (await searchEnumerator.MoveNextAsync())
						return searchEnumerator.Current;

					return null;
				}
				finally
				{
					semaphore.Release();
				}
			}

			// We only loop if cancellation has not been requested (i.e. "Q" has not been pressed)
			// Every time you see "token" mentioned, its performing a check



			int threadCompletedCount = 0;
			int imageCompletedCount = 0;
			
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
					if (tempFilePath != null && File.Exists(tempFilePath))
						File.Delete(tempFilePath);

					if (tempThumbPath != null && File.Exists(tempThumbPath))
						File.Delete(tempThumbPath);
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

					workerStatuses[id] = $"Downloading image {nextImage.FullImageUri}";

					int completedCount = await DownloadEnqueuedImage(imageDownloadClient, nextImage);

					if (completedCount % 10 == 0 || enqueuedImages.Count == 0)
					{
						Log.Information($"{"[Image]",-9} [{{completedCount}}/{{enqueuedImagesCount}}]",
							completedCount, enqueuedImages.Count);
					}

					return true;
				}

				// The unit of work involving checking if any threads are available, and downloading a single one + enqueuing those images.
				async Task<bool> CheckThreads()
				{
					// Grab the next thread from the round robin queue. Complex because we want to do this in a thread-safe way
					var enumerationResult = await EnumerateNextPost();

					if (!enumerationResult.HasValue)
						// Exit if there are no threads available
						return false;

					var nextThread = enumerationResult.Value;

					workerStatuses[id] = $"Scraping thread /{nextThread.Board}/{nextThread.ThreadId}";

					bool outerSuccess = true;

					// Add a timeout for the scrape to 2 minutes, so it doesn't hang forever
					using var timeoutToken = new CancellationTokenSource(TimeSpan.FromMinutes(2));

					await AsyncProxyCall(async client =>
					{
						var result = await ThreadDownloadTask(timeoutToken.Token, idString, nextThread.Board, nextThread.ThreadId, client);

						int newCompletedCount = Interlocked.Increment(ref threadCompletedCount);

						string threadStatus;

						switch (result.Status)
						{
							case ThreadUpdateStatus.Ok:
								threadStatus = " ";
								break;
							case ThreadUpdateStatus.Archived:
								threadStatus = "A";
								break;
							case ThreadUpdateStatus.Deleted:
								threadStatus = "D";
								break;
							case ThreadUpdateStatus.NotModified:
								threadStatus = "N";
								break;
							case ThreadUpdateStatus.DoNotArchive:
								threadStatus = "S";
								break;
							case ThreadUpdateStatus.Error:
								threadStatus = "E";
								break;
							default:
								threadStatus = "?";
								break;
						}

						if (!result.Success)
						{
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
						Log.Information($"{"[Thread]",-9} {$"/{nextThread.Board}/{nextThread.ThreadId}",-17} {threadStatus} {$"+({result.ImageDownloads.Count}/{result.PostCountChange})",-13} [{enqueuedImages.Count}/{newCompletedCount}/{totalSearchCountString}]");
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
				Log.Debug("Worker ID {idString} finished", idString);

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

			Log.Information("");
			Log.Information("Completed {threadCompletedCount} threads", threadCompletedCount);
			Log.Information("");

			// Add any images that were previously requeued from failure
			foreach (var queuedImage in requeuedImages)
				enqueuedImages.Enqueue(queuedImage);

			requeuedImages.Clear();

			await StateStore.WriteDownloadQueue(enqueuedImages);

			Log.Debug($" --> Cleared queued image cache");
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
				Log.Error(ex, "Network operation failed, and was unhandled. Inconsistencies may arise in continued use of program");
			}

			await threadWaitTask;
		}

		#endregion

		#region Worker

		/// <summary>
		/// Polls a thread, and passes it to the consumer if the thread has been detected as updated.
		/// </summary>
		/// <param name="token">The cancellation token associated with this request.</param>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread to poll.</param>
		/// <param name="client">The <see cref="HttpClientProxy"/> to use for the poll request.</param>
		/// <returns></returns>
		private async Task<ThreadUpdateTaskResult> ThreadDownloadTask(CancellationToken token, string workerId, string board, ulong threadNumber, HttpClientProxy client)
		{
			try
			{
				var existing = await ThreadConsumer.CheckExistingThreads(new[] { threadNumber }, board, false);

				if (existing.Count > 0)
					return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.NotModified, 0);

				Log.Debug($"{workerId,-2}: Polling thread /{board}/{threadNumber}", true);
				
				var response = await FrontendApi.GetThread(board, threadNumber, client.Client, null, token);

				token.ThrowIfCancellationRequested();

				switch (response.ResponseType)
				{
					case ResponseType.Ok:

						var threadPointer = new ThreadPointer(board, threadNumber);

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

							Log.Warning($"{workerId,-2}: Thread /{board}/{threadNumber} is malformed (DMCA?)");
							
							await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

							return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Deleted, 0);
						}

						// Process the thread data with its assigned TrackedThread instance, then pass the results to the consumer

						TrackedThread trackedThread;

						bool isNewThread;

						if (existing.Count == 1)
						{
							trackedThread = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existing.First());
							isNewThread = false;
						}
						else
						{
							trackedThread = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash);
							isNewThread = true;
						}

						var threadUpdateInfo = trackedThread.ProcessThreadUpdates(threadPointer, response.Data);
						threadUpdateInfo.IsNewThread = isNewThread;

						if (!threadUpdateInfo.HasChanges)
						{
							// This should be safe when a thread becomes archived, because that archive bit flip should be counted as a change as well

							return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.NotModified, 0);
						}
						
						// TODO: handle failures from this call
						// Right now if this call fails, Hayden's state will assume that it has succeeded because the
						//   TrackedThread instance's state hasn't rolled back

						var images = await ThreadConsumer.ConsumeThread(threadUpdateInfo);

						if (response.Data.IsArchived == true)
						{
							Log.Debug($"{workerId,-2}: Thread /{board}/{threadNumber} has been archived");
							
							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						return new ThreadUpdateTaskResult(true,
							images,
							response.Data.IsArchived == true ? ThreadUpdateStatus.Archived : ThreadUpdateStatus.Ok,
							threadUpdateInfo.NewPosts.Count - threadUpdateInfo.DeletedPosts.Count);

					case ResponseType.NotModified:
						// There are no updates for this thread
						return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.NotModified, 0);

					case ResponseType.NotFound:
						// This thread returned a 404, indicating a deletion

						Log.Debug($"{workerId,-2}: Thread /{board}/{threadNumber} has been pruned or deleted");
						
						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);

						return new ThreadUpdateTaskResult(true, Array.Empty<QueuedImageDownload>(), ThreadUpdateStatus.Deleted, 0);

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Log.Error(exception, $"Could not poll or update thread /{board}/{threadNumber}. Will try again next board update\nClient name: {client.Name}");

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
					return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
				})
				.ConfigureAwait(false);

			response.EnsureSuccessStatusCode();

			var tempFilePath = Path.Combine(ConsumerConfig.DownloadLocation, "hayden", Guid.NewGuid().ToString("N") + ".temp");

			await using (var webStream = await response.Content.ReadAsStreamAsync())
			await using (var tempFileStream = new FileStream(tempFilePath, FileMode.CreateNew))
			{
				await webStream.CopyToAsync(tempFileStream);
			}

			return tempFilePath;
		}

		#endregion
	}
}