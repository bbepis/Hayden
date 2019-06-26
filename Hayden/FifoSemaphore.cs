using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hayden
{
	public class FifoSemaphore
	{
		private readonly SemaphoreSlim semaphore;
		private readonly ConcurrentQueue<TaskCompletionSource<bool>> queue = new ConcurrentQueue<TaskCompletionSource<bool>>();

		public FifoSemaphore(int initialCount)
		{
			semaphore = new SemaphoreSlim(initialCount);
		}

		public FifoSemaphore(int initialCount, int maxCount)
		{
			semaphore = new SemaphoreSlim(initialCount, maxCount);
		}

		public Task WaitAsync(CancellationToken token = default)
		{
			token.ThrowIfCancellationRequested();

			var tcs = new TaskCompletionSource<bool>();
			queue.Enqueue(tcs);

			semaphore.WaitAsync(token).ContinueWith(t =>
			{
				if (queue.TryDequeue(out var popped))
				{
					if (token.IsCancellationRequested)
						popped.SetCanceled();
					else
						popped.SetResult(true);
				}
			});

			return tcs.Task;
		}

		public int CurrentCount => semaphore.CurrentCount;

		public void Release()
		{
			semaphore.Release();
		}
	}
}