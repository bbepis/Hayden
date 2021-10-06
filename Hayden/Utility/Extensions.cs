using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hayden
{
	public static class Extensions
	{
		/// <summary>
		/// Concurrently performs a task on each item in an enumerable collection.
		/// </summary>
		/// <typeparam name="T">The type of item.</typeparam>
		/// <param name="source">The <see cref="IEnumerable{T}"/> that contains the items to execute over.</param>
		/// <param name="dop">The maximum amount of tasks that can run in parallel.</param>
		/// <param name="body">The task to be performed for each individual item.</param>
		public static async Task ForEachAsync<T>(this IEnumerable<T> source, int dop, Func<T, Task> body)
		{
			var semaphore = new SemaphoreSlim(dop);

			await Task.WhenAll(source.Select(async x =>
			{
				await semaphore.WaitAsync();

				try
				{
					await body(x);
				}
				finally
				{
					semaphore.Release();
				}
			}));
		}
	}
}