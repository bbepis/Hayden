using System;

namespace Hayden
{
	public class PoolObject<T> : IDisposable
	{
		public T Object { get; }

		private Action<T> ReturnAction { get; }

		public PoolObject(T o, Action<T> returnAction)
		{
			Object = o;
			ReturnAction = returnAction;
		}

		public void Dispose()
		{
			ReturnAction(Object);
		}

		public static implicit operator T(PoolObject<T> poolObject)
			=> poolObject.Object;
	}
}
