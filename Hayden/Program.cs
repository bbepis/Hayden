using System;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Consumers;

namespace Hayden
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.0.0");
			Console.WriteLine("By Bepis");

			if (args.Length < 2)
			{
				Console.WriteLine("Usage: hayden <board> <download directory location>");
				return;
			}

			Log("Initialized.");

			string board = args[0];
			string downloadDir = args[1];

			Log($"Downloading from board \"{board}\" to directory {downloadDir}");
			Log("Press Q to stop archival.");

			var boardArchiver = new BoardArchiver(board, new FilesystemThreadConsumer(downloadDir));

			var tokenSource = new CancellationTokenSource();

			var archivalTask = boardArchiver.Execute(tokenSource.Token);

			while (true)
			{
				try
				{
					var readKey = await Utility.ReadKeyAsync(CancellationToken.None, true);

					if (readKey.Key == ConsoleKey.Q)
						break;
				}
				catch (TaskCanceledException)
				{
					break;
				}
			}

			Log("Shutting down...");

			tokenSource.Cancel();

			await archivalTask;
		}

		public static void Log(string content)
		{
			Console.WriteLine($"[{DateTime.Now:G}] {content}");
		}
	}
}