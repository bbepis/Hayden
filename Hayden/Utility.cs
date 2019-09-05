using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
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
		public static async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken, bool intercept = false, int responsiveness = 100)
		{
			var cancelPressed = false;
			var cancelWatcher = new ConsoleCancelEventHandler((sender, args) => { cancelPressed = true; });

			Console.CancelKeyPress += cancelWatcher;

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
	}
}