using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hayden
{
	public static class Extensions
	{
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
