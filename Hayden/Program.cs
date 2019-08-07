using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.Proxy;
using Newtonsoft.Json.Linq;

namespace Hayden
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.4.0");
			Console.WriteLine("By Bepis");

			if (args.Length != 1)
			{
				Console.WriteLine("Usage: hayden <config file location>");
				return;
			}

			var rawConfigFile = JObject.Parse(File.ReadAllText(args[0]));

			IThreadConsumer consumer;

			string backendType = rawConfigFile["backend"]["type"].Value<string>();

			var yotsubaConfig = rawConfigFile["source"].ToObject<YotsubaConfig>();

			switch (backendType)
			{
				case "Asagi":
					var asagiConfig = rawConfigFile["backend"].ToObject<AsagiConfig>();

					consumer = new AsagiThreadConsumer(asagiConfig, yotsubaConfig.Boards);
					break;

				default:
					throw new ArgumentException($"Unknown backend type {backendType}");
			}

			ProxyProvider proxyProvider = null;

			if (rawConfigFile["proxies"] != null)
				proxyProvider = new ConfigProxyProvider((JArray)rawConfigFile["proxies"]);

			Log("Initialized.");

			//Log($"Downloading from board /{board}/ to directory {downloadDir}");
			Log("Press Q to stop archival.");
			
			var boardArchiver = new BoardArchiver(yotsubaConfig, consumer, proxyProvider);

			var tokenSource = new CancellationTokenSource();

			var archivalTask = boardArchiver.Execute(tokenSource.Token)
				.ContinueWith(task =>
				{
					if (task.IsFaulted)
					{
						Log("!! FATAL EXCEPTION !!");
						Log(task.Exception.ToString());
					}

					tokenSource.Cancel();
				});

			while (true)
			{
				try
				{
					var readKey = await Utility.ReadKeyAsync(tokenSource.Token, true);

					if (readKey.Key == ConsoleKey.Q)
						break;
				}
				catch (TaskCanceledException)
				{
					break;
				}
			}

			Log("Shutting down...");

			if (!tokenSource.IsCancellationRequested)
				tokenSource.Cancel();

			await archivalTask;
		}

		public static void Log(string content)
		{
			Console.WriteLine($"[{DateTime.Now:G}] {content}");
		}
	}
}