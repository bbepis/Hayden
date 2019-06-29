using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Consumers;
using Hayden.Contract;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.1.0");
			Console.WriteLine("By Bepis");

			if (args.Length != 1)
			{
				Console.WriteLine("Usage: hayden <config file location>");
				return;
			}

			var configFile = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(args[0]));

			IThreadConsumer consumer;

			switch ((string)configFile.Backend.type)
			{
				case "Asagi":
					var connection = new MySqlConnection((string)configFile.Backend.connectionString);
					await connection.OpenAsync();

					consumer = new AsagiThreadConsumer(connection, (string)configFile.Backend.downloadLocation);
					break;

				default:
					throw new ArgumentException($"Unknown backend type {(string)configFile.Backend.type}");
			}

			Log("Initialized.");

			string board = ((JArray)configFile.Source.boards)[0].Value<string>();

			//Log($"Downloading from board /{board}/ to directory {downloadDir}");
			Log("Press Q to stop archival.");
			
			var boardArchiver = new BoardArchiver(board, consumer);

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