using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NodaTime;

namespace Hayden
{
	public static class Utility
	{
		/// <summary>
		/// Obtains the next character or function key pressed by the user
		/// asynchronously. The pressed key is displayed in the console window.
		/// </summary>
		/// <param name="cancellationToken">
		/// The cancellation token that can be used to cancel the read.
		/// </param>
		/// <param name="responsiveness">
		/// The number of milliseconds to wait between polling the
		/// <see cref="Console.KeyAvailable"/> property.
		/// </param>
		/// <returns>Information describing what key was pressed.</returns>
		/// <exception cref="TaskCanceledException">
		/// Thrown when the read is cancelled by the user input (Ctrl+C etc.)
		/// or when cancellation is signalled via
		/// the passed <paramred name="cancellationToken"/>.
		/// </exception>
		public static async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default, bool intercept = false, bool handleConsoleCancel = true, int responsiveness = 100)
		{
			var cancelPressed = false;
			ConsoleCancelEventHandler cancelWatcher = null;

			if (handleConsoleCancel)
			{
				cancelWatcher = (sender, args) =>
				{
					args.Cancel = false;
					cancelPressed = true;
				};

				Console.CancelKeyPress += cancelWatcher;
			}
			
			try
			{
				while (!cancelPressed && !cancellationToken.IsCancellationRequested)
				{
					if (Console.KeyAvailable)
					{
						return Console.ReadKey(intercept);
					}

					await Task.Delay(
						responsiveness,
						cancellationToken);
				}

				if (cancelPressed)
				{
					throw new TaskCanceledException("Readkey canceled by user input.");
				}

				throw new TaskCanceledException();
			}
			finally
			{
				if (handleConsoleCancel)
					Console.CancelKeyPress -= cancelWatcher;
			}
		}

		public static string GetEmbeddedText(string resourceName)
		{
			var assembly = Assembly.GetExecutingAssembly();

			using (Stream stream = assembly.GetManifestResourceStream(resourceName))
			using (StreamReader reader = new StreamReader(stream))
			{
				return reader.ReadToEnd();
			}
		}

		private static readonly DateTimeZone EasternTimeZone = DateTimeZoneProviders.Tzdb["America/New_York"];

		public static uint GetNewYorkTimestamp(DateTimeOffset offset)
		{
			return (uint)(ZonedDateTime.FromDateTimeOffset(offset)
									   .WithZone(EasternTimeZone)
									   .ToDateTimeUnspecified()
						  - DateTime.UnixEpoch).TotalSeconds;
		}

		public static uint GetGMTTimestamp(DateTimeOffset offset)
		{
			return (uint)(ZonedDateTime.FromDateTimeOffset(offset)
									   .WithZone(DateTimeZone.Utc)
									   .ToDateTimeUnspecified()
						  - DateTime.UnixEpoch).TotalSeconds;
		}

		public static IEnumerable<TItem> RoundRobin<TItem, TKey>(this IList<TItem> source, Func<TItem, TKey> predicate)
		{
			List<TKey> keys = source.Select(predicate)
			                        .Distinct()
									.ToList();

			SortedList<TKey, int> queueIndices = new SortedList<TKey, int>();

			foreach (var key in keys)
				queueIndices.Add(key, 0);

			while (queueIndices.Count > 0)
			{
				foreach (var key in keys)
				{
					if (!queueIndices.ContainsKey(key))
						continue;

					int index = queueIndices[key];

					while (index < source.Count)
					{
						var item = source[index];

						index++;

						if (Equals(predicate(item), key))
						{
							yield return item;
							break;
						}
					}

					if (index == source.Count)
						queueIndices.Remove(key);
					else
						queueIndices[key] = index;
				}
			}
		}
	}
}