using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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

		/// <summary>
		/// Retrieves text embedded within the application as a resource stream.
		/// </summary>
		/// <param name="resourceName">The name of the resource.</param>
		/// <returns></returns>
		public static string GetEmbeddedText(string resourceName)
		{
			var assembly = Assembly.GetExecutingAssembly();

			using Stream stream = assembly.GetManifestResourceStream(resourceName);
			using StreamReader reader = new StreamReader(stream);

			return reader.ReadToEnd();
		}

		private static readonly DateTimeZone EasternTimeZone = DateTimeZoneProviders.Tzdb["America/New_York"];

		/// <summary>
		/// Converts a <see cref="DateTimeOffset"/> value into it's respective Unix timestamp, taking into account the <value>America/New_York</value> timezone.
		/// </summary>
		/// <param name="offset">The <see cref="DateTimeOffset"/> value to convert.</param>
		/// <returns>Offset unix timestamp.</returns>
		public static uint GetNewYorkTimestamp(DateTimeOffset offset)
		{
			return (uint)(ZonedDateTime.FromDateTimeOffset(offset)
									   .WithZone(EasternTimeZone)
									   .ToDateTimeUnspecified()
						  - DateTime.UnixEpoch).TotalSeconds;
		}

		/// <summary>
		/// Converts a <see cref="DateTimeOffset"/> value into it's respective Unix timestamp.
		/// </summary>
		/// <param name="offset">The <see cref="DateTimeOffset"/> value to convert.</param>
		/// <returns>Unix timestamp.</returns>
		public static uint GetGMTTimestamp(DateTimeOffset offset)
		{
			return (uint)(ZonedDateTime.FromDateTimeOffset(offset)
									   .WithZone(DateTimeZone.Utc)
									   .ToDateTimeUnspecified()
						  - DateTime.UnixEpoch).TotalSeconds;
		}

		/// <summary>
		/// Converts a Unix timestamp into it's respective <see cref="DateTimeOffset"/> value, taking into account the <value>America/New_York</value> timezone.
		/// </summary>
		/// <param name="timestamp">The timestamp to convert.</param>
		public static DateTimeOffset ConvertNewYorkTimestamp(uint timestamp)
		{
			return LocalDateTime.FromDateTime(DateTime.UnixEpoch + TimeSpan.FromSeconds(timestamp))
								.InZoneLeniently(EasternTimeZone)
								.ToDateTimeOffset();
		}

		/// <summary>
		/// Converts a Unix timestamp into it's respective <see cref="DateTimeOffset"/> value.
		/// </summary>
		/// <param name="timestamp">The timestamp to convert.</param>
		public static DateTimeOffset ConvertGMTTimestamp(uint timestamp)
		{
			return Instant.FromUnixTimeSeconds(timestamp)
						  .ToDateTimeOffset();
		}

		/// <summary>
		/// Transforms an enumerable list into a round-robin list, grouped by the predicate.
		/// </summary>
		/// <typeparam name="TItem">The type of the item.</typeparam>
		/// <typeparam name="TKey">The type of the key that items will be grouped against.</typeparam>
		/// <param name="source">The input source of the items to sort.</param>
		/// <param name="predicate">The selector of the key that items will be grouped against.</param>
		/// <returns></returns>
		public static IEnumerable<TItem> RoundRobin<TItem, TKey>(this IList<TItem> source, Func<TItem, TKey> predicate)
		{
			List<TKey> keys = source.Select(predicate)
				.Where(x => x != null) // safety check
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

		public static int FirstIndexOf<TItem>(this IEnumerable<TItem> items, Predicate<TItem> predicate)
		{
			int i = 0;

			foreach (var item in items)
			{
				if (predicate(item))
					return i;

				i++;
			}

			return -1;
		}
		
		public static string ConvertToBase(byte[] data, int @base = 36)
		{
			const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";

			var builder = new StringBuilder();

			var value = new BigInteger(data, true);

			while (value > 0)
			{
				value = BigInteger.DivRem(value, @base, out var remainder);

				builder.Append(chars[(int)remainder]);
			}

			return builder.ToString();
		}

		/// <summary>
		/// Generates an 32-bit FV1a hash from a string.
		/// </summary>
		/// <param name="input">The string to hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		public static uint FNV1aHash32(string input)
		{
			uint FNV32Offset = 0x811C9DC5U;

			return FNV1aHash32(input, FNV32Offset);
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a new 32-bit value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint FNV1aHash32(int input, uint state)
		{
			FNV1aHash32(input, ref state);
			return state;
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a new 32-bit value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void FNV1aHash32(int input, ref uint state)
		{
			const uint FNV32Prime = 0x1000193;

			state = (uint)(state ^ input) * FNV32Prime;
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a new 32-bit value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void FNV1aHash32(uint input, ref uint state)
		{
			const uint FNV32Prime = 0x1000193;

			state = (state ^ input) * FNV32Prime;
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a new 32-bit value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void FNV1aHash32(ulong input, ref uint state)
		{
			FNV1aHash32((uint)(input >> 32), ref state);
			FNV1aHash32((uint)(input & 0xFFFFFFFF), ref state);
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a string value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint FNV1aHash32(string input, uint state)
		{
			FNV1aHash32(input, ref state);
			return state;
		}

		/// <summary>
		/// Iterates an 32-bit FV1a hash with a string value.
		/// </summary>
		/// <param name="input">The new input to iterate the hash with.</param>
		/// <param name="state">The current state of the hash.</param>
		/// <returns>A 32-bit FV1a hash.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void FNV1aHash32(string input, ref uint state)
		{
			if (input != null)
			{
				foreach (char c in input)
				{
					ushort charValue = c;

					FNV1aHash32(charValue >> 8, ref state);
					FNV1aHash32(charValue & 0xFF, ref state);
				}
			}
			
			// Precaution against empty strings.
			// Say you have string A = "test" and string B = ""
			// Hash(A + B) and Hash(B + A) would produce the exact same result, if this additional character is not included

			FNV1aHash32(0x00, ref state);
		}
	}
}