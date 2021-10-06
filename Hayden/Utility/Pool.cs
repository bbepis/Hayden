using System;
using System.Threading.Tasks;

namespace Hayden
{
	public class PoolObject<T> : IDisposable, IAsyncDisposable
	{
		public T Object { get; }

		private Action<T> ReturnAction { get; }
		private Func<T, Task> ReturnTask { get; }

		public PoolObject(T o, Action<T> returnAction)
		{
			Object = o;
			ReturnAction = returnAction;
		}

		public PoolObject(T o, Func<T, Task> returnTask)
		{
			Object = o;
			ReturnTask = returnTask;
		}

		public void Dispose()
		{
			ReturnAction?.Invoke(Object);

			ReturnTask?.Invoke(Object).Wait();
		}

		public static implicit operator T(PoolObject<T> poolObject)
			=> poolObject.Object;

		public ValueTask DisposeAsync()
		{
			ReturnAction?.Invoke(Object);

			if (ReturnTask != null)
				return new ValueTask(ReturnTask(Object));

			return new ValueTask(Task.FromResult<object>(null));
		}
	}
}