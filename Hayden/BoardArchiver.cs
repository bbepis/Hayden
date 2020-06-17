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
			BoardUpdateTimespan = TimeSpan.FromSeconds(config.BoardDelay ?? 30);
		}

		/// <summary>
		/// Performs the main archival loop.
		/// </summary>
		/// <param name="token">Token to safely cancel the execution.</param>
		public async Task Execute(CancellationToken token)
		{
			bool firstRun = true;
			var imageDownloadClient = new HttpClientProxy(ProxyProvider.CreateNewClient(), "baseconnection/image");

			List<ThreadPointer> threadQueue = new List<ThreadPointer>();
			ConcurrentQueue<QueuedImageDownload> enqueuedImages = new ConcurrentQueue<QueuedImageDownload>();
			List<QueuedImageDownload> requeuedImages = new List<QueuedImageDownload>();

			SortedList<string, DateTimeOffset> lastBoardCheckTimes = new SortedList<string, DateTimeOffset>(Config.Boards.Length);

			while (!token.IsCancellationRequested)
			{
				int currentBoardCount = 0;

				await Config.Boards.ForEachAsync(8, async board =>
				{
					token.ThrowIfCancellationRequested();

					DateTimeOffset lastDateTimeCheck;

					lock (lastBoardCheckTimes)
						if (!lastBoardCheckTimes.TryGetValue(board, out lastDateTimeCheck))
							lastDateTimeCheck = DateTimeOffset.MinValue;

					DateTimeOffset beforeCheckTime = DateTimeOffset.Now;

					var threads = await GetBoardThreads(token, board, lastDateTimeCheck, firstRun);

					lock (threadQueue)
						threadQueue.AddRange(threads);

					if (firstRun && Config.ReadArchive)
					{
						var archivedThreads = await GetArchivedBoardThreads(token, board, lastDateTimeCheck);

						lock (threadQueue)
							threadQueue.AddRange(archivedThreads);
					}

					lock (lastBoardCheckTimes)
					{
						lastBoardCheckTimes[board] = beforeCheckTime;

						if (++currentBoardCount % 5 == 0 || currentBoardCount == Config.Boards.Length)
						{
							Program.Log($"{currentBoardCount} / {Config.Boards.Length} boards enqueued");
						}
					}
				});

				if (token.IsCancellationRequested)
					break;

				Program.Log($"{threadQueue.Count} threads have been queued total");
				threadQueue.TrimExcess();

				var waitTask = Task.Delay(BoardUpdateTimespan, token);


				var requeuedThreads = new List<ThreadPointer>();

				async Task AsyncProxyCall(Func<HttpClientProxy, Task> action)
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


				int threadCompletedCount = 0;
				int imageCompletedCount = 0;

				async Task<int> DownloadEnqueuedImage(HttpClientProxy client, QueuedImageDownload image)
				{
					QueuedImageDownload queuedDownload = image;

					if (image == null)
						if (!enqueuedImages.TryDequeue(out queuedDownload))
							return imageCompletedCount;

					if (File.Exists(queuedDownload.DownloadPath))
					{
						return Interlocked.Increment(ref imageCompletedCount);
					}

					var waitTask = Task.Delay(50, token); // Wait 100ms because we're nice people

					try
					{
						await DownloadFileTask(queuedDownload.DownloadUri, queuedDownload.DownloadPath, client.Client);
					}
					catch (Exception ex)
					{
						Program.Log($"ERROR: Could not download image. Will try again next board update\nClient name: {client.Name}\nException: {ex}");

						lock (requeuedImages)
							requeuedImages.Add(queuedDownload);
					}

					await waitTask;

					return Interlocked.Increment(ref imageCompletedCount);
				}

				if (firstRun)
				{
					foreach (var queuedImage in await StateStore.GetDownloadQueue())
						enqueuedImages.Enqueue(queuedImage);

					Program.Log($"{enqueuedImages.Count} media items loaded from queue cache");
				}

				foreach (var queuedImage in requeuedImages)
					enqueuedImages.Enqueue(queuedImage);

				requeuedImages.Clear();

				using var roundRobinQueue = threadQueue.RoundRobin(x => x.Board).GetEnumerator();

				IDictionary<int, string> WorkerStatuses = new ConcurrentDictionary<int, string>();

				async Task WorkerTask(int id, bool prioritizeImages)
				{
					var idString = id.ToString();

					async Task<bool> CheckImages()
					{
						bool success = enqueuedImages.TryDequeue(out var nextImage);

						if (success)
						{
							WorkerStatuses[id] = $"Downloading image {nextImage.DownloadUri}";

							int completedCount = await DownloadEnqueuedImage(imageDownloadClient, nextImage);

							if (completedCount % 10 == 0)
							{
								Program.Log($"{"[Image]",-9} [{completedCount}/{enqueuedImages.Count}]");
							}
						}

						return success;
					}

					async Task<bool> CheckThreads()
					{
						bool success = false;
						ThreadPointer nextThread;

						lock (roundRobinQueue)
						{
							success = roundRobinQueue.MoveNext();
							nextThread = roundRobinQueue.Current;
						}

						if (!success)
							return false;

						WorkerStatuses[id] = $"Scraping thread /{nextThread.Board}/{nextThread.ThreadId}";

						bool outerSuccess = true;

						await AsyncProxyCall(async client =>
						{
							var result = await ThreadUpdateTask(CancellationToken.None, idString, nextThread.Board, nextThread.ThreadId, client);

							int newCompletedCount = Interlocked.Increment(ref threadCompletedCount);

							string threadStatus = " ";

							switch (result.Status)
							{
								case ThreadUpdateStatus.Ok:          threadStatus = " "; break;
								case ThreadUpdateStatus.Archived:    threadStatus = "A"; break;
								case ThreadUpdateStatus.Deleted:     threadStatus = "D"; break;
								case ThreadUpdateStatus.NotModified: threadStatus = "N"; break;
								case ThreadUpdateStatus.Error:       threadStatus = "E"; break;
							}

							if (!success)
							{
								lock (requeuedThreads)
									requeuedThreads.Add(nextThread);

								outerSuccess = false;
								return;
							}

							Program.Log($"{"[Thread]",-9} {$"/{nextThread.Board}/{nextThread.ThreadId}",-17} {threadStatus} {$"+({result.ImageDownloads.Count}/{result.PostCount})",-13} [{enqueuedImages.Count}/{newCompletedCount}/{threadQueue.Count}]");

							foreach (var imageDownload in result.ImageDownloads)
								enqueuedImages.Enqueue(imageDownload);

							await StateStore.InsertToDownloadQueue(new ReadOnlyCollection<QueuedImageDownload>(result.ImageDownloads));
						});

						return outerSuccess;
					}

					while (true)
					{
						WorkerStatuses[id] = "Idle";

						if (token.IsCancellationRequested)
							break;

						if (prioritizeImages)
						{
							if (await CheckImages())
								continue;
						}

						if (await CheckThreads())
							continue;

						if (await CheckImages())
							continue;

						break;
					}

					Program.Log($"Worker ID {idString} finished", true);

					WorkerStatuses[id] = "Finished";

					if (Program.HaydenConfig.DebugLogging)
					{
						lock (WorkerStatuses)
							foreach (var kv in WorkerStatuses)
							{
								Program.Log($"ID {kv.Key,-2} => {kv.Value}", true);
							}
					}
				}

				List<Task> workerTasks = new List<Task>();

				int id = 1;

				for (int i = 0; i < ProxyProvider.ProxyCount; i++)
				{
					workerTasks.Add(WorkerTask(id++, i % 3 == 0));
				}

				await Task.WhenAll(workerTasks);
				

				Program.Log($" --> Completed {threadCompletedCount} / {threadQueue.Count} : Waiting for next board update interval");


				enqueuedImages.Clear();
				await StateStore.WriteDownloadQueue(enqueuedImages);

				Program.Log($" --> Cleared queued image cache");

				firstRun = false;

				// A bit overkill but force a compacting GC collect here to make sure that the heap doesn't expand too much over time
				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect();


				threadQueue.Clear();

				threadQueue.AddRange(requeuedThreads);

				await waitTask;
			}
		}

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

					foreach (ulong nonExistingThreadId in archiveRequest.Data.Except(existingArchivedThreads.Select(x => x.threadId)))
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
		public async Task<IList<ThreadPointer>> GetBoardThreads(CancellationToken token, string board, DateTimeOffset lastDateTimeCheck, bool firstRun)
		{
			var cooldownTask = Task.Delay(ApiCooldownTimespan, token);

			var threads = new List<ThreadPointer>();

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

					var threadList = pagesRequest.Data.SelectMany(x => x.Threads).ToList();

					if (firstRun)
					{
						var existingThreads = await ThreadConsumer.CheckExistingThreads(threadList.Select(x => x.ThreadNumber),
							board,
							false,
							true);

						foreach (var existingThread in existingThreads)
						{
							var thread = threadList.First(x => x.ThreadNumber == existingThread.threadId);

							if (thread.LastModified <= Utility.GetGMTTimestamp(existingThread.lastPostTime))
							{
								threadList.Remove(thread);
							}
						}
					}

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetGMTTimestamp(lastDateTimeCheck);

					foreach (var thread in threadList)
					{
						if (thread.LastModified > lastCheckTimestamp)
						{
							threads.Add(new ThreadPointer(board, thread.ThreadNumber));
						}
					}

					Program.Log($"Enqueued {threads.Count} threads from board /{board}/ past timestamp {lastCheckTimestamp}");

					break;

				case ResponseType.NotModified:
					break;

				case ResponseType.NotFound:
				default:
					Program.Log($"Unable to index board /{board}/, is there a connection error?");
					break;
			}

			await cooldownTask;

			return threads;
		}

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

				var response = await YotsubaApi.GetThread(board, threadNumber, client.Client, null, token);

				switch (response.ResponseType)
				{
					case ResponseType.Ok:
						Program.Log($"{workerId,-2}: Downloading changes from thread /{board}/{threadNumber}", true);

						var images = await ThreadConsumer.ConsumeThread(response.Data, board);

						if (response.Data.OriginalPost.Archived == true)
						{
							Program.Log($"{workerId,-2}: Thread /{board}/{threadNumber} has been archived", true);

							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						

						return new ThreadUpdateTaskResult(true,
							images,
							response.Data.OriginalPost.Archived == true ? ThreadUpdateStatus.Archived : ThreadUpdateStatus.Ok,
							response.Data.Posts.Length);

					case ResponseType.NotModified:
						return new ThreadUpdateTaskResult(true, new QueuedImageDownload[0], ThreadUpdateStatus.NotModified, response.Data.Posts.Length);

					case ResponseType.NotFound:
						Program.Log($"{workerId,-2}: Thread /{board}/{threadNumber} has been pruned or deleted", true);

						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);
						return new ThreadUpdateTaskResult(true, new QueuedImageDownload[0], ThreadUpdateStatus.Deleted, 0);

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Program.Log($"ERROR: Could not poll or update thread /{board}/{threadNumber}. Will try again next board update\nClient name: {client.Name}\nException: {exception}");

				return new ThreadUpdateTaskResult(false, new QueuedImageDownload[0], ThreadUpdateStatus.Error, 0);
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
			using var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(() => httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead));

			await using (var webStream = await response.Content.ReadAsStreamAsync())
			await using (var fileStream = new FileStream(downloadPath + ".part", FileMode.Create))
			{
				await webStream.CopyToAsync(fileStream);
			}

			File.Move(downloadPath + ".part", downloadPath);
		}
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
	public struct ThreadPointer
	{
		public string Board { get; }

		public ulong ThreadId { get; }

		public ThreadPointer(string board, ulong threadId)
		{
			Board = board;
			ThreadId = threadId;
		}
	}
	public enum ThreadUpdateStatus
	{
		Ok,
		Deleted,
		Archived,
		NotModified,
		Error
	}

	public struct ThreadUpdateTaskResult
	{
		public bool Success { get; set; }
		public IList<QueuedImageDownload> ImageDownloads { get; set; }
		public ThreadUpdateStatus Status { get; set; }
		public int PostCount { get; set; }

		public ThreadUpdateTaskResult(bool success, IList<QueuedImageDownload> imageDownloads, ThreadUpdateStatus status, int postCount)
		{
			Success = success;
			ImageDownloads = imageDownloads;
			Status = status;
			PostCount = postCount;
		}
	}
}