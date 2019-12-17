using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.Proxy;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx.Interop;

namespace Hayden
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.6.0");
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
				});

			var terminateTask = WaitForTerminateAsync();
			await Task.WhenAny(archivalTask, terminateTask);

			Log("Shutting down...");

			if (!tokenSource.IsCancellationRequested)
				tokenSource.Cancel();

			await archivalTask;
		}


		private static readonly object ConsoleLockObject = new object();
		public static void Log(string content)
		{
			lock (ConsoleLockObject)
				Console.WriteLine($"[{DateTime.Now:G}] {content}");
		}

		public static Task WaitForTerminateAsync()
		{
			Task unixKillSignalTask = null;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				var sigIntTask = WaitHandleAsyncFactory.FromWaitHandle(new UnixSignal(Signum.SIGINT))
													   .ContinueWith(async s => Log("Received kill signal (SIGINT)"));
				var sigTermTask = WaitHandleAsyncFactory.FromWaitHandle(new UnixSignal(Signum.SIGTERM))
														.ContinueWith(async s => Log("Received kill signal (SIGTERM)"));
				var sigHUpTask = WaitHandleAsyncFactory.FromWaitHandle(new UnixSignal(Signum.SIGHUP))
													   .ContinueWith(async s => Log("Received kill signal (SIGHUP)"));

				unixKillSignalTask = Task.WhenAny(sigIntTask, sigTermTask, sigHUpTask);
			}


			Task consoleWaitTask = null;

			if (!Console.IsInputRedirected)
			{
				consoleWaitTask = Task.Factory.StartNew(async () =>
				{
					while (true)
					{
						try
						{
							var readKey = await Utility.ReadKeyAsync(intercept: true);

							if (readKey.Key == ConsoleKey.Q)
								break;
						}
						catch (TaskCanceledException)
						{
							break;
						}
					}
				}, TaskCreationOptions.LongRunning).Unwrap();
			}

			var taskArray = new[] { unixKillSignalTask, consoleWaitTask ?? new TaskCompletionSource<object>().Task };
			return Task.WhenAny(taskArray.Where(x => x != null));
		}
	}
}