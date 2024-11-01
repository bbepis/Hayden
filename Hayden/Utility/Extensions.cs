using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;

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

        public static IServiceCollection AddSingletonMulti<T1, T2, TImplementation>(this IServiceCollection services)
		    where TImplementation : class, T1, T2
			where T1 : class
            where T2 : class
        {
            services.AddSingleton<TImplementation>();
			services.AddSingleton<T1>(provider => provider.GetRequiredService<TImplementation>());
            services.AddSingleton<T2>(provider => provider.GetRequiredService<TImplementation>());

            return services;
        }

        public static async IAsyncEnumerable<TItem> GetAsyncEnumerable<TItem>(
	        this AsyncProducerConsumerQueue<TItem> queue)
        {
	        while (await queue.OutputAvailableAsync())
		        yield return queue.Dequeue();
        }

		public static IEnumerable<(TKey Key, TValue[] Values)> EfficientGroupBy<T, TKey, TValue>(
			this IEnumerable<T> items,
			Func<T, TKey> keySelector,
			Func<T, TValue> valueSelector)
		{
			bool isFirst = true;
			var lastKey = default(TKey);
			var list = new List<TValue>();
			var comparer = EqualityComparer<TKey>.Default;

			foreach (var item in items)
			{
				var key = keySelector(item);
				var value = valueSelector(item);

				if (isFirst)
				{
					lastKey = key;
					isFirst = false;
					list.Add(value);
					continue;
				}

				if (!comparer.Equals(lastKey, key))
				{
					yield return (lastKey, list.ToArray());
					list.Clear();
					lastKey = key;
					list.Add(value);
					continue;
				}

				list.Add(value);
			}

			if (!isFirst)
				yield return (lastKey, list.ToArray());
		}
	}
}