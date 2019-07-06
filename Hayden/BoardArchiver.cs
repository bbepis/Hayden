using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;

namespace Hayden
{
	public class BoardArchiver
	{
		public YotsubaConfig Config { get; }

		protected IThreadConsumer ThreadConsumer { get; }

		public TimeSpan BoardUpdateTimespan { get; set; }

		public TimeSpan ApiCooldownTimespan { get; set; }

		private FifoSemaphore APISemaphore { get; }

		public BoardArchiver(YotsubaConfig config, IThreadConsumer threadConsumer, FifoSemaphore apiSemaphore = null)
		{
			Config = config;
			ThreadConsumer = threadConsumer;

			APISemaphore = apiSemaphore ?? new FifoSemaphore(1);

			ApiCooldownTimespan = TimeSpan.FromSeconds(config.ApiDelay ?? 1);
			BoardUpdateTimespan = TimeSpan.FromSeconds(config.BoardDelay ?? 30);
		}

		public async Task Execute(CancellationToken token)
		{
			var semaphoreTask = SemaphoreUpdateTask(token);

			var threadSemaphore = new SemaphoreSlim(20);
			bool firstRun = true;

			HashSet<(string board, ulong threadNumber)> threadQueue = new HashSet<(string board, ulong threadNumber)>();

			SortedList<string, DateTimeOffset> lastBoardCheckTimes = new SortedList<string, DateTimeOffset>(Config.Boards.Length);

			while (!token.IsCancellationRequested)
			{
				foreach (string board in Config.Boards)
				{
					if (!lastBoardCheckTimes.TryGetValue(board, out DateTimeOffset lastDateTimeCheck))
						lastDateTimeCheck = DateTimeOffset.MinValue;

					uint lastCheckTimestamp = firstRun
						? 0
						: Utility.GetNewYorkTimestamp(lastDateTimeCheck);

					DateTimeOffset beforeCheckTime = DateTimeOffset.Now;

					await APISemaphore.WaitAsync(token);

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
						await APISemaphore.WaitAsync(token);

						var archiveRequest = await YotsubaApi.GetArchive(board, lastDateTimeCheck, token);
						switch (archiveRequest.ResponseType)
						{
							case YotsubaResponseType.Ok:

								var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.ThreadIds, board, true);

								Program.Log($"Found {existingArchivedThreads.Length} existing archived threads for board /{board}/");

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

				Program.Log($"{threadQueue.Count} threads have been queued total");

				var waitTask = Task.Delay(BoardUpdateTimespan, token);

				var weakReferences = new List<WeakReference<Task>>();

				var requeuedThreads = new HashSet<(string board, ulong threadNumber)>();

				int completedCount = 0;

				var roundRobinQueue = threadQueue.GroupBy(s => s.board)
										 .SelectMany(grp => grp.Select((str, idx) => new { Index = idx, Value = str }))
										 .OrderBy(v => v.Index).ThenBy(v => v.Value.board)
										 .Select(v => v.Value);

				foreach (var thread in roundRobinQueue)
				{
					if (completedCount % 50 == 0)
					{
						Program.Log($" --> Completed {completedCount} / {threadQueue.Count}. {threadQueue.Count - completedCount} to go");
					}

					await threadSemaphore.WaitAsync();

					weakReferences.Add(new WeakReference<Task>(Task.Run(async () =>
						{
							bool success = await ThreadUpdateTask(CancellationToken.None, thread.board, thread.threadNumber);

							if (!success)
								requeuedThreads.Add(thread);

							threadSemaphore.Release();
						})));

					completedCount++;
				}

				foreach (var updateTask in weakReferences)
				{
					if (updateTask.TryGetTarget(out var task))
						await task;
				}

				firstRun = false;

				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect();


				threadQueue.Clear();

				foreach (var requeuedThread in requeuedThreads)
					threadQueue.Add(requeuedThread);

				await waitTask;
			}
		}

		private async Task<bool> ThreadUpdateTask(CancellationToken token, string board, ulong threadNumber)
		{
			try
			{
				await APISemaphore.WaitAsync(token);

				Program.Log($"Polling thread /{board}/{threadNumber}");

				var response = await YotsubaApi.GetThread(board, threadNumber, null, token);

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

		private async Task SemaphoreUpdateTask(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				if (APISemaphore.CurrentCount < 1)
					APISemaphore.Release();

				await Task.Delay(ApiCooldownTimespan, token);
			}
		}
	}
}