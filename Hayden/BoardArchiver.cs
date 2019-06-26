using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Contract;

namespace Hayden
{
	public class BoardArchiver
	{
		public string Board { get; }

		protected IThreadConsumer ThreadConsumer { get; }

		public TimeSpan BoardUpdateTimespan { get; set; } = TimeSpan.FromSeconds(20);

		public TimeSpan ApiCooldownTimespan { get; set; } = TimeSpan.FromSeconds(1);

		private ConcurrentDictionary<ulong, ThreadTracker> TrackedThreads { get; } = new ConcurrentDictionary<ulong, ThreadTracker>();

		private DateTimeOffset? LastBoardUpdate { get; set; }

		private readonly FifoSemaphore APISemaphore = new FifoSemaphore(1);


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

		private async Task BoardUpdateTask(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				Program.Log($"Getting contents of board \"{Board}\"");

				var pagesRequest = await YotsubaApi.GetBoard(Board, LastBoardUpdate, cancellationToken);

				switch (pagesRequest.ResponseType)
				{
					case YotsubaResponseType.Ok:

						foreach (var thread in pagesRequest.Payload.SelectMany(x => x.Threads))
						{
							if (!TrackedThreads.ContainsKey(thread.ThreadNumber))
							{
								var cachedThread = new ThreadTracker
								{
									ThreadNumber = thread.ThreadNumber,
									LastTimestampUpdate = thread.LastModified
								};

								TrackedThreads[thread.ThreadNumber] = cachedThread;

								cachedThread.UpdateTask = ThreadUpdateTask(cancellationToken, cachedThread);
							}
						}
						
						LastBoardUpdate = DateTimeOffset.Now;
						break;

					case YotsubaResponseType.NotModified:
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}

				await Task.Delay(BoardUpdateTimespan, cancellationToken);
			}
		}

		private async Task ThreadUpdateTask(CancellationToken token, ThreadTracker tracker)
		{
			bool stopUpdating = false;

			while (!token.IsCancellationRequested)
			{
				await APISemaphore.WaitAsync(token);

				Program.Log($"Polling thread {Board}/{tracker.ThreadNumber}");

				var response = await YotsubaApi.GetThread(Board, tracker.ThreadNumber, tracker.LastUpdate, token);

				switch (response.ResponseType)
				{
					case YotsubaResponseType.Ok:
						tracker.LastUpdate = DateTimeOffset.Now;
						Program.Log($"Downloading changes from thread {Board}/{tracker.ThreadNumber}");

						await ThreadConsumer.ConsumeThread(response.Payload, Board);

						if (response.Payload.OriginalPost.Archived == true)
						{
							Program.Log($"Thread {Board}/{tracker.ThreadNumber} has been archived");
							stopUpdating = true;
						}

						break;

					case YotsubaResponseType.NotModified:
						break;

					case YotsubaResponseType.NotFound:
						Program.Log($"Thread {Board}/{tracker.ThreadNumber} has been pruned or deleted");
						stopUpdating = true;
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}

				if (stopUpdating)
					break;
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

			public ulong LastTimestampUpdate { get; set; }

			public Task UpdateTask { get; set; }
		}
	}
}