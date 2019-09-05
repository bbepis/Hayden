using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Proxy;

namespace Hayden
{
	public class BoardArchiver
	{
		public YotsubaConfig Config { get; }

		protected IThreadConsumer ThreadConsumer { get; }
		protected ProxyProvider ProxyProvider { get; }

		public TimeSpan BoardUpdateTimespan { get; set; }

		public TimeSpan ApiCooldownTimespan { get; set; }

		private FifoSemaphore APISemaphore { get; }

		public BoardArchiver(YotsubaConfig config, IThreadConsumer threadConsumer, ProxyProvider proxyProvider = null)
		{
			Config = config;
			ThreadConsumer = threadConsumer;
			ProxyProvider = proxyProvider;

			APISemaphore = new FifoSemaphore(1);

			ApiCooldownTimespan = TimeSpan.FromSeconds(config.ApiDelay ?? 1);
			BoardUpdateTimespan = TimeSpan.FromSeconds(config.BoardDelay ?? 30);
		}

		public async Task Execute(CancellationToken token)
		{
			var concurrentSempahore = new SemaphoreSlim(20);
			bool firstRun = true;

			List<(string board, ulong threadNumber)> threadQueue = new List<(string board, ulong threadNumber)>();

			SortedList<string, DateTimeOffset> lastBoardCheckTimes = new SortedList<string, DateTimeOffset>(Config.Boards.Length);

			while (!token.IsCancellationRequested)
			{
				foreach (string board in Config.Boards)
				{
					if (!lastBoardCheckTimes.TryGetValue(board, out DateTimeOffset lastDateTimeCheck))
						lastDateTimeCheck = DateTimeOffset.MinValue;

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetGMTTimestamp(lastDateTimeCheck);

					DateTimeOffset beforeCheckTime = DateTimeOffset.Now;

					await Task.Delay(ApiCooldownTimespan);

					var pagesRequest = await YotsubaApi.GetBoard(board, lastDateTimeCheck, token);

					switch (pagesRequest.ResponseType)
					{
						case YotsubaResponseType.Ok:

							int newCount = 0;

							foreach (var thread in pagesRequest.Pages.SelectMany(x => x.Threads).ToArray())
							{
								if (thread.LastModified > lastCheckTimestamp)
								{
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

						var archiveRequest = await YotsubaApi.GetArchive(board, lastDateTimeCheck, token);
						switch (archiveRequest.ResponseType)
						{
							case YotsubaResponseType.Ok:

								var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.ThreadIds, board, true);

								Program.Log($"Found {existingArchivedThreads.Count} existing archived threads for board /{board}/");

								int count = 0;

								foreach (ulong nonExistingThreadId in archiveRequest.ThreadIds.Except(existingArchivedThreads))
								{
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

					lastBoardCheckTimes[board] = beforeCheckTime;
				}

				threadQueue = threadQueue.Distinct().ToList();

				Program.Log($"{threadQueue.Count} threads have been queued total");
				threadQueue.TrimExcess();

				var waitTask = Task.Delay(BoardUpdateTimespan, token);

				var weakReferences = new List<WeakReference<Task>>();

				var requeuedThreads = new List<(string board, ulong threadNumber)>();

				int completedCount = 0;

				var roundRobinQueue = threadQueue.RoundRobin(x => x.board);

				foreach (var thread in roundRobinQueue)
				{
					if (completedCount % 50 == 0)
					{
						Program.Log($" --> Completed {completedCount} / {threadQueue.Count} : {threadQueue.Count - completedCount} to go");
					}

					await concurrentSempahore.WaitAsync();

					weakReferences.Add(new WeakReference<Task>(Task.Run(async () =>
					{
						PoolObject<HttpClient> client = null;

						Task threadWaitTask = null;

						if (ProxyProvider != null)
						{
							client = await ProxyProvider.RentHttpClient();
							threadWaitTask = Task.Delay(ApiCooldownTimespan);
						}

						bool success = await ThreadUpdateTask(CancellationToken.None, thread.board, thread.threadNumber, client?.Object);

						if (!success)
							lock (requeuedThreads)
								requeuedThreads.Add(thread);

						concurrentSempahore.Release();

						if (threadWaitTask != null)
							await threadWaitTask;

						client?.Dispose();
					})));

					if (ProxyProvider == null)
						await Task.Delay(ApiCooldownTimespan);

					completedCount++;
				}

				foreach (var updateTask in weakReferences)
				{
					if (updateTask.TryGetTarget(out var task))
						await task;
				}

				Program.Log($" --> Completed {completedCount} / {threadQueue.Count} : Waiting for next board update interval");

				firstRun = false;

				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect();


				threadQueue.Clear();

				foreach (var requeuedThread in requeuedThreads)
					threadQueue.Add(requeuedThread);

				await waitTask;
			}
		}

		private async Task<bool> ThreadUpdateTask(CancellationToken token, string board, ulong threadNumber, HttpClient client)
		{
			try
			{
				Program.Log($"Polling thread /{board}/{threadNumber}");

				var response = await YotsubaApi.GetThread(board, threadNumber, null, token, client);

				switch (response.ResponseType)
				{
					case YotsubaResponseType.Ok:
						Program.Log($"Downloading changes from thread /{board}/{threadNumber}");

						await ThreadConsumer.ConsumeThread(response.Thread, board);

						if (response.Thread.OriginalPost.Archived == true)
						{
							Program.Log($"Thread /{board}/{threadNumber} has been archived");

							await ThreadConsumer.ThreadUntracked(threadNumber, board, false);
						}

						return true;

					case YotsubaResponseType.NotModified:
						return true;

					case YotsubaResponseType.NotFound:
						Program.Log($"Thread /{board}/{threadNumber} has been pruned or deleted");

						await ThreadConsumer.ThreadUntracked(threadNumber, board, true);
						return true;

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			catch (Exception exception)
			{
				Program.Log($"ERROR: Could not poll or update thread /{board}/{threadNumber}. Will try again next board update\nException: {exception}");

				return false;
			}
		}
	}
}