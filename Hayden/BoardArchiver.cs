using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden
{
	public class BoardArchiver
	{
		public YotsubaConfig Config { get; }

		protected IThreadConsumer ThreadConsumer { get; }

		public TimeSpan BoardUpdateTimespan { get; set; }

		public TimeSpan ApiCooldownTimespan { get; set; }

		private ConcurrentDictionary<ulong, ThreadTracker> TrackedThreads { get; } = new ConcurrentDictionary<ulong, ThreadTracker>();

		private DateTimeOffset? LastBoardUpdate { get; set; }

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
			TrackedThreads.Clear();

			var semaphoreTask = SemaphoreUpdateTask(token);

			var boardTasks = new List<Task>();

			foreach (var board in Config.Boards)
			{
				boardTasks.Add(BoardUpdateTask(token, board));
			}

			await Task.WhenAll(boardTasks);
		}

		private async Task BoardUpdateTask(CancellationToken token, string board)
		{
			bool firstRun = true;
			var threadSemaphore = new SemaphoreSlim(10);
			
			PageThread[] currentActivePageThreads = new PageThread[0];
			PageThread[] currentArchivedPageThreads = new PageThread[0];

			while (!token.IsCancellationRequested)
			{
				Program.Log($"Getting contents of board /{board}/");

				bool hasChanged = false;

				await APISemaphore.WaitAsync(token);

				var pagesRequest = await YotsubaApi.GetBoard(board, LastBoardUpdate, token);

				switch (pagesRequest.ResponseType)
				{
					case YotsubaResponseType.Ok:
						currentActivePageThreads = pagesRequest.Pages.SelectMany(x => x.Threads).ToArray();
						hasChanged = true;
						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
					default:
						Program.Log($"Unable to index board /{board}/, is there a connection error?");
						break;
				}


				await APISemaphore.WaitAsync(token);

				var archiveRequest = await YotsubaApi.GetArchive(board, LastBoardUpdate, token);
				switch (archiveRequest.ResponseType)
				{
					case YotsubaResponseType.Ok:

						currentArchivedPageThreads = archiveRequest.ThreadIds
						   // Order by ascending thread number, to ensure we don't miss something from the very end of the archive
						   // that gets pruned by the time we get to it.
						   .OrderBy(x => x)
						   .Select(x => new PageThread { ThreadNumber = x, LastModified = PageThread.ArchivedLastModifiedTime }).ToArray();

						if (firstRun)
						{
							var existingArchivedThreads = await ThreadConsumer.CheckExistingThreads(archiveRequest.ThreadIds, board, true);

							Program.Log($"Found {existingArchivedThreads.Length} existing archived threads for board /{board}/");

							foreach (ulong existingThreadId in existingArchivedThreads)
							{
								var threadTracker = new ThreadTracker
								{
									Archived = true,
									Poll = false,
									ThreadNumber = existingThreadId,
									Board = board,
									LastModified = PageThread.ArchivedLastModifiedTime
								};

								TrackedThreads.TryAdd(existingThreadId, threadTracker);
							}
						}

						hasChanged = true;
						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
					default:
						Program.Log($"Unable to index the archive of board /{board}/, is there a connection error?");
						break;
				}

				var waitTask = Task.Delay(BoardUpdateTimespan, token);

				if (hasChanged)
				{
					var updateTrackers = UpdateThreadsToTrack(currentArchivedPageThreads.Concat(currentActivePageThreads), board);

					var weakReferences = new List<WeakReference<Task>>();
					
					foreach (var tracker in updateTrackers)
					{
						await threadSemaphore.WaitAsync();

						weakReferences.Add(new WeakReference<Task>(ThreadUpdateTask(CancellationToken.None, tracker)
							.ContinueWith(x => threadSemaphore.Release())));
					}

					foreach (var updateTask in weakReferences)
					{
						if (updateTask.TryGetTarget(out var task))
							await task;
					}

					firstRun = false;

					System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
					GC.Collect();
				}

				await waitTask;
			}
		}

		private List<ThreadTracker> UpdateThreadsToTrack(IEnumerable<PageThread> threads, string board)
		{
			List<ThreadTracker> updateTrackers = new List<ThreadTracker>();

			// Process threads that are currently not dead.

			foreach (var thread in threads)
			{
				if (TrackedThreads.TryGetValue(thread.ThreadNumber, out var threadTracker))
				{
					// We are already tracking the thread.

					if (!threadTracker.Archived && threadTracker.LastModified < thread.LastModified)
					{
						// Thread has updated.
						threadTracker.LastModified = thread.LastModified;

						threadTracker.Poll = true;
					}
					else if (thread.IsArchived && !threadTracker.Archived)
					{
						// Thread has become archived.
						threadTracker.Archived = true;

						threadTracker.Poll = true;
					}
					else
					{
						// Thread has not changed.
						// Threads cannot go from archived to active again, so we can skip checking for it.
					}
				}
				else
				{
					// We not yet tracking the thread.
					// Add the thread to the tracking list, and request it to be scraped.

					threadTracker = new ThreadTracker
					{
						ThreadNumber = thread.ThreadNumber,
						Board = board,
						LastModified = thread.LastModified,
						Archived = thread.IsArchived,
						Poll = true
					};

					TrackedThreads[thread.ThreadNumber] = threadTracker;
				}
			}

			foreach (var threadTracker in TrackedThreads.Values)
			{
				if (threadTracker.Board == board && threadTracker.Poll)
					updateTrackers.Add(threadTracker);
			}

			if (updateTrackers.Count > 0)
				Program.Log($"{updateTrackers.Count} threads from /{board}/ have been enqueued for polling");

			// Find threads that we are monitoring but are dead.

			var deadThreadIds = TrackedThreads.Keys.Except(threads.Select(x => x.ThreadNumber));

			foreach (var deadThreadId in deadThreadIds)
			{
				// Mark each thread as dead.
				// They will prune themselves from the tracking list.

				TrackedThreads[deadThreadId].Deleted = true;
			}

			// Update the last time we checked the board.

			LastBoardUpdate = DateTimeOffset.Now;

			return updateTrackers;
		}

		private async Task ThreadUpdateTask(CancellationToken token, ThreadTracker tracker)
		{
			try
			{
				await APISemaphore.WaitAsync(token);

				Program.Log($"Polling thread /{tracker.Board}/{tracker.ThreadNumber}");

				var response = await YotsubaApi.GetThread(tracker.Board, tracker.ThreadNumber, tracker.LastUpdate, token);

				switch (response.ResponseType)
				{
					case YotsubaResponseType.Ok:
						tracker.LastUpdate = DateTimeOffset.Now;
						Program.Log($"Downloading changes from thread /{tracker.Board}/{tracker.ThreadNumber}");

						await ThreadConsumer.ConsumeThread(response.Thread, tracker.Board);

						if (response.Thread.OriginalPost.Archived == true)
						{
							Program.Log($"Thread /{tracker.Board}/{tracker.ThreadNumber} has been archived");
						}

						tracker.Deleted = false;
						tracker.Poll = false;

						break;

					case YotsubaResponseType.NotModified:
						tracker.Deleted = false;
						tracker.Poll = false;

						break;

					case YotsubaResponseType.NotFound:
						Program.Log($"Thread /{tracker.Board}/{tracker.ThreadNumber} returned HTTP 404 Not Found");
						tracker.Poll = true;
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}

				if (tracker.Deleted)
				{
					Program.Log($"Thread /{tracker.Board}/{tracker.ThreadNumber} has been pruned or deleted; stopping tracking");

					await ThreadConsumer.ThreadUntracked(tracker.ThreadNumber, tracker.Board);
					TrackedThreads.Remove(tracker.ThreadNumber, out _);
				}
			}
			catch (Exception exception)
			{
				Program.Log($"ERROR: Could not poll or update thread /{tracker.Board}/{tracker.ThreadNumber}. Will try again next board update\nException: {exception}");
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

		private class ThreadTracker
		{
			public ulong ThreadNumber { get; set; }

			public string Board { get; set; }

			public DateTimeOffset? LastUpdate { get; set; }

			public ulong LastModified { get; set; }

			public bool Archived { get; set; } = false;

			public bool Deleted { get; set; } = false;

			public bool Poll { get; set; }
		}
	}
}