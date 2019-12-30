using System;
using System.Collections.Generic;
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
	public class BoardArchiver
	{
		public YotsubaConfig Config { get; }

		protected IThreadConsumer ThreadConsumer { get; }
		protected IStateStore StateStore { get; }
		protected ProxyProvider ProxyProvider { get; }

		public TimeSpan BoardUpdateTimespan { get; set; }

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

		public async Task Execute(CancellationToken token)
		{
			bool firstRun = true;

			List<(string board, ulong threadNumber)> threadQueue = new List<(string board, ulong threadNumber)>();
			Queue<QueuedImageDownload> enqueuedImages = new Queue<QueuedImageDownload>();
			List<QueuedImageDownload> requeuedImages = new List<QueuedImageDownload>();

			SortedList<string, DateTimeOffset> lastBoardCheckTimes = new SortedList<string, DateTimeOffset>(Config.Boards.Length);

			while (!token.IsCancellationRequested)
			{
				await Config.Boards.ForEachAsync(4, async board =>
				{
					DateTimeOffset lastDateTimeCheck;

					lock (lastBoardCheckTimes)
						if (!lastBoardCheckTimes.TryGetValue(board, out lastDateTimeCheck))
							lastDateTimeCheck = DateTimeOffset.MinValue;

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetGMTTimestamp(lastDateTimeCheck);

					DateTimeOffset beforeCheckTime = DateTimeOffset.Now;

					await Task.Delay(ApiCooldownTimespan);


					var pagesRequest = await NetworkPolicies.GenericRetryPolicy<(Page[] Pages, YotsubaResponseType ResponseType)>(12).ExecuteAsync(async () =>
					{
						Program.Log($"Requesting threads from board /{board}/...");
						await using var boardClient = await ProxyProvider.RentHttpClient();
						return await YotsubaApi.GetBoard(board, boardClient.Object, lastDateTimeCheck, token);
					});

					switch (pagesRequest.ResponseType)
					{
						case YotsubaResponseType.Ok:

							int newCount = 0;

							var threadList = pagesRequest.Pages.SelectMany(x => x.Threads).ToList();

							if (firstRun)
							{
								var existingThreads = await ThreadConsumer.CheckExistingThreads(threadList.Select(x => x.ThreadNumber), board, false, true);

								foreach (var existingThread in existingThreads)
								{
									var thread = threadList.First(x => x.ThreadNumber == existingThread.threadId);

									if (thread.LastModified <= Utility.GetGMTTimestamp(existingThread.lastPostTime))
									{
										threadList.Remove(thread);
									}
								}
							}

							foreach (var thread in threadList)
							{
								if (thread.LastModified > lastCheckTimestamp)
								{
									lock (threadQueue)
										threadQueue.Add((board, thread.ThreadNumber));
									
									newCount++;
								}
							}

							Program.Log($"Enqueued {newCount} threads from board /{board}/ past timestamp {lastCheckTimestamp}");

							break;

						case YotsubaResponseType.NotModified:
							break;

						case YotsubaResponseType.NotFound:
						default:
							Program.Log($"Unable to index board /{board}/, is there a connection error?");
							break;
					}


					if (firstRun)
					{
						await Task.Delay(ApiCooldownTimespan);

						var archiveRequest = await NetworkPolicies.GenericRetryPolicy<(ulong[] ThreadIds, YotsubaResponseType ResponseType)>(12).ExecuteAsync(async () =>
						{
							await using var boardClient = await ProxyProvider.RentHttpClient();
							return await YotsubaApi.GetArchive(board, boardClient.Object, lastDateTimeCheck, token);
						});

						switch (archiveRequest.ResponseType)
						{
							case YotsubaResponseType.Ok:

								var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.ThreadIds, board, true, false);

								Program.Log($"Found {existingArchivedThreads.Count} existing archived threads for board /{board}/");

								int count = 0;

								foreach (ulong nonExistingThreadId in archiveRequest.ThreadIds.Except(existingArchivedThreads.Select(x => x.threadId)))
								{
									lock (threadQueue)
										threadQueue.Add((board, nonExistingThreadId));

									count++;
								}

								Program.Log($"Enqueued {count} threads from board archive /{board}/");

								break;

							case YotsubaResponseType.NotModified:
								break;

							case YotsubaResponseType.NotFound:
							default:
								Program.Log($"Unable to index the archive of board /{board}/, is there a connection error?");
								break;
						}
					}

					lock (lastBoardCheckTimes)
						lastBoardCheckTimes[board] = beforeCheckTime;
				});

				threadQueue = threadQueue.Distinct().ToList();

				Program.Log($"{threadQueue.Count} threads have been queued total");
				threadQueue.TrimExcess();

				var waitTask = Task.Delay(BoardUpdateTimespan, token);


				var threadTasks = new Queue<WeakReference<Task>>();

				var requeuedThreads = new List<(string board, ulong threadNumber)>();

				void QueueProxyCall(Func<HttpClient, Task> action)
				{
					var task = Task.Run(async () =>
					{
						await using var client = await ProxyProvider.RentHttpClient();

						var threadWaitTask = Task.Delay(ApiCooldownTimespan);

						try
						{
							await action(client);
						}
						catch (Exception ex)
						{
							Program.Log($"ERROR: Network operation failed, and was unhandled. Inconsistencies may arise in continued use of program\r\n" + ex.ToString());
						}

						await threadWaitTask;
					});

					lock (threadTasks)
						threadTasks.Enqueue(new WeakReference<Task>(task));
				}


				int threadCompletedCount = 0;
				int imageCompletedCount = 0;

				async Task DownloadEnqueuedImage(HttpClient client)
				{
					QueuedImageDownload queuedDownload;

					lock (enqueuedImages)
						if (!enqueuedImages.TryDequeue(out queuedDownload))
							return;

					if (File.Exists(queuedDownload.DownloadPath))
					{
						Interlocked.Increment(ref imageCompletedCount);
						return;
					}

					await Task.Delay(100); // Wait 100ms because we're nice people

					try
					{
						await DownloadFileTask(queuedDownload.DownloadUri, queuedDownload.DownloadPath, client);
					}
					catch (Exception ex)
					{
						Program.Log($"ERROR: Could not download image . Will try again next board update\nException: {ex}");

						lock (requeuedImages)
							requeuedImages.Add(queuedDownload);
					}

					Interlocked.Increment(ref imageCompletedCount);
				}

				enqueuedImages.Clear();

				if (firstRun)
				{
					foreach (var queuedImage in await StateStore.GetDownloadQueue())
						enqueuedImages.Enqueue(queuedImage);

					Program.Log($"{enqueuedImages.Count} media items loaded from queue cache");
				}

				foreach (var queuedImage in requeuedImages)
					enqueuedImages.Enqueue(queuedImage);

				requeuedImages.Clear();

				var threadSemaphore = new SemaphoreSlim(20);

				foreach (var thread in threadQueue.RoundRobin(x => x.board))
				{
					await threadSemaphore.WaitAsync();

					QueueProxyCall(async client =>
					{
						(bool success, IList<QueuedImageDownload> imageDownloads)
							= await ThreadUpdateTask(CancellationToken.None, thread.board, thread.threadNumber, client);


						int newCompletedCount = Interlocked.Increment(ref threadCompletedCount);

						if (newCompletedCount % 50 == 0)
						{
							Program.Log($" --> Completed {threadCompletedCount} / {threadQueue.Count} : {threadQueue.Count - threadCompletedCount} to go");

							lock (enqueuedImages)
								Program.Log($" --> {enqueuedImages.Count} in image queue");
						}

						if (!success)
						{
							lock (requeuedThreads)
								requeuedThreads.Add(thread);
						}
						else
						{
							// Fallback to Monitor references instead of lock(){} because of some compiler rule that doesn't allow us to await inside of locks
							try
							{
								Monitor.Enter(enqueuedImages);

								foreach (var imageDownload in imageDownloads)
									enqueuedImages.Enqueue(imageDownload);

								await StateStore.WriteDownloadQueue(enqueuedImages.ToArray());
							}
							finally
							{
								Monitor.Exit(enqueuedImages);
							}


							// Perform 100 image downloads on a thread.
							for (int i = 0; i < 100; i++)
							{
								await DownloadEnqueuedImage(client);
							}
						}

						threadSemaphore.Release();
					});
				}

				// Queue a download task to download all remaining images.
				QueueProxyCall(async client =>
				{
					while (true)
					{
						QueuedImageDownload queuedDownload;

						lock (enqueuedImages)
							if (!enqueuedImages.TryDequeue(out queuedDownload))
								break;

						await Task.Delay(100); // Wait 100ms because we're nice people

						await DownloadEnqueuedImage(client);
					}
				});

				// Wait for all currently running/enqueued thread download tasks
				while (true)
				{
					WeakReference<Task> remainingTask;

					lock (threadTasks)
						if (!threadTasks.TryDequeue(out remainingTask))
						{
							break;
						}

					if (remainingTask.TryGetTarget(out var task))
						await task;
				}

				Program.Log($" --> Completed {threadCompletedCount} / {threadQueue.Count} : Waiting for next board update interval");

				firstRun = false;

				// A bit overkill but force a compacting GC collect here to make sure that the heap doesn't expand too much over time
				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect();


				threadQueue.Clear();

				threadQueue.AddRange(requeuedThreads);

				await waitTask;
			}
		}

		private async Task<(bool success, IList<QueuedImageDownload> imageDownloads)> ThreadUpdateTask(CancellationToken token, string board, ulong threadNumber, HttpClient client)
		{
			try
			{
				Program.Log($"Polling thread /{board}/{threadNumber}");

				var response = await YotsubaApi.GetThread(board, threadNumber, client, null, token);

				switch (response.ResponseType)
				{
					case YotsubaResponseType.Ok:
						Program.Log($"Downloading changes from thread /{board}/{threadNumber}");

						var images = await ThreadConsumer.ConsumeThread(response.Thread, board);

						if (response.Thread.OriginalPost.Archived == true)
						{
							Program.Log($"Thread /{board}/{threadNumber} has been archived");

							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						return (true, images);

					case YotsubaResponseType.NotModified:
						return (true, new QueuedImageDownload[0]);

					case YotsubaResponseType.NotFound:
						Program.Log($"Thread /{board}/{threadNumber} has been pruned or deleted");

						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);
						return (true, new QueuedImageDownload[0]);

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Program.Log($"ERROR: Could not poll or update thread /{board}/{threadNumber}. Will try again next board update\nException: {exception}");

				return (false, null);
			}
		}

		private async Task DownloadFileTask(Uri imageUrl, string downloadPath, HttpClient httpClient)
		{
			if (File.Exists(downloadPath))
				return;

			Program.Log($"Downloading image {Path.GetFileName(downloadPath)}");

			var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);

			using (var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(() => httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)))
			using (var webStream = await response.Content.ReadAsStreamAsync())
			using (var fileStream = new FileStream(downloadPath + ".part", FileMode.Create))
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
}