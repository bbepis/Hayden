using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden
{
	public class BoardArchiver
	{
		public string Board { get; }

		protected IThreadConsumer ThreadConsumer { get; }

		public TimeSpan BoardUpdateTimespan { get; set; } = TimeSpan.FromSeconds(10);

		public TimeSpan ApiCooldownTimespan { get; set; } = TimeSpan.FromSeconds(1);

		private ConcurrentDictionary<ulong, ThreadTracker> TrackedThreads { get; } = new ConcurrentDictionary<ulong, ThreadTracker>();

		private DateTimeOffset? LastBoardUpdate { get; set; }

		private readonly FifoSemaphore APISemaphore = new FifoSemaphore(1);

		private PageThread[] CurrentActivePageThreads { get; set; } = new PageThread[0];
		private PageThread[] CurrentArchivedPageThreads { get; set; } = new PageThread[0];


		public BoardArchiver(string board, IThreadConsumer threadConsumer)
		{
			Board = board;
			ThreadConsumer = threadConsumer;
		}

		public async Task Execute(CancellationToken cancellationToken)
		{
			TrackedThreads.Clear();

			var semaphoreTask = SemaphoreUpdateTask(cancellationToken);

			await BoardUpdateTask(cancellationToken);
		}

		private async Task BoardUpdateTask(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				Program.Log($"Getting contents of board /{Board}/");

				await APISemaphore.WaitAsync(token);

				var archiveRequest = await YotsubaApi.GetArchive(Board, LastBoardUpdate, token);

				bool hasChanged = false;

				switch (archiveRequest.ResponseType)
				{
					case YotsubaResponseType.Ok:
						CurrentArchivedPageThreads = archiveRequest.ThreadIds
																   // Order by ascending thread number, to ensure we don't miss something from the very end of the archive
																   // that gets pruned by the time we get to it.
																   .OrderBy(x => x)
																   .Select(x => new PageThread { ThreadNumber = x }).ToArray();
						hasChanged = true;
						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
					default:
						Program.Log($"Unable to index the archive of board /{Board}/, is there a connection error?");
						break;
				}

				await APISemaphore.WaitAsync(token);

				var pagesRequest = await YotsubaApi.GetBoard(Board, LastBoardUpdate, token);

				switch (pagesRequest.ResponseType)
				{
					case YotsubaResponseType.Ok:
						CurrentActivePageThreads = pagesRequest.Pages.SelectMany(x => x.Threads).ToArray();
						hasChanged = true;
						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
					default:
						Program.Log($"Unable to index board /{Board}/, is there a connection error?");
						break;
				}

				//Program.Log($"DEBUG: Total threads: {CurrentActivePageThreads.Length + CurrentArchivedPageThreads.Length}");

				if (hasChanged)
				{
					UpdateThreadsToTrack(CurrentArchivedPageThreads.Concat(CurrentActivePageThreads), token);
				}

				await Task.Delay(BoardUpdateTimespan, token);
			}
		}

		private void UpdateThreadsToTrack(IEnumerable<PageThread> threads, CancellationToken token = default)
		{
			// Process threads that are currently not dead.

			foreach (var thread in threads)
			{
				if (TrackedThreads.TryGetValue(thread.ThreadNumber, out var threadTracker))
				{
					// We are already tracking the thread.

					if (threadTracker.LastModified < thread.LastModified)
					{
						// Thread has updated.
						threadTracker.LastModified = thread.LastModified;
						threadTracker.Updated = true;
					}
					else if (thread.IsArchived && !threadTracker.Archived)
					{
						// Thread has become archived.
						threadTracker.Archived = true;
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
						LastModified = thread.LastModified,
						Archived = thread.IsArchived,
						Updated = true
					};

					TrackedThreads[thread.ThreadNumber] = threadTracker;

					threadTracker.UpdateTask = ThreadUpdateTask(token, threadTracker);
				}
			}

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
		}

		private async Task ThreadUpdateTask(CancellationToken token, ThreadTracker tracker)
		{
			bool isArchived = false;

			while (!token.IsCancellationRequested)
			{
				if (!tracker.Deleted
					&& !tracker.Updated
					&& tracker.Archived == isArchived)
				{
					await Task.Delay(1000, token);
					continue;
				}

				await APISemaphore.WaitAsync(token);

				Program.Log($"Polling thread /{Board}/{tracker.ThreadNumber}");

				var response = await YotsubaApi.GetThread(Board, tracker.ThreadNumber, tracker.LastUpdate, token);

				switch (response.ResponseType)
				{
					case YotsubaResponseType.Ok:
						tracker.LastUpdate = DateTimeOffset.Now;
						Program.Log($"Downloading changes from thread /{Board}/{tracker.ThreadNumber}");

						await ThreadConsumer.ConsumeThread(response.Thread, Board);

						if (response.Thread.OriginalPost.Archived == true)
						{
							Program.Log($"Thread /{Board}/{tracker.ThreadNumber} has been archived");
							isArchived = true;
						}

						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
						Program.Log($"Thread /{Board}/{tracker.ThreadNumber} returned HTTP 404 Not Found");
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}

				tracker.Updated = false;

				if (tracker.Deleted)
				{
					Program.Log($"Thread /{Board}/{tracker.ThreadNumber} has been pruned or deleted");
					break;
				}
			}

			await ThreadConsumer.ThreadUntracked(tracker.ThreadNumber, Board);
			TrackedThreads.Remove(tracker.ThreadNumber, out _);
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

			public DateTimeOffset? LastUpdate { get; set; }

			public ulong LastModified { get; set; }

			public bool Archived { get; set; } = false;

			public bool Deleted { get; set; } = false;

			public bool Updated { get; set; } = false;

			public Task UpdateTask { get; set; }
		}
	}
}