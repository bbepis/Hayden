using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Cache;
using Hayden.Config;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.Proxy;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json.Linq;

namespace Hayden
{
	public class Program
	{
		public static HaydenConfigOptions HaydenConfig;

		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.7.0");
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
			string downloadLocation = null;

			switch (backendType)
			{
				case "Asagi":
					var asagiConfig = rawConfigFile["backend"].ToObject<AsagiConfig>();

					downloadLocation = asagiConfig.DownloadLocation;
					consumer = new AsagiThreadConsumer(asagiConfig, yotsubaConfig.Boards);
					break;

				case "Filesystem":
					var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

					downloadLocation = filesystemConfig.DownloadLocation;
					consumer = new FilesystemThreadConsumer(filesystemConfig);
					break;

				default:
					throw new ArgumentException($"Unknown backend type {backendType}");
			}

			HaydenConfig = rawConfigFile["hayden"]?.ToObject<HaydenConfigOptions>() ?? new HaydenConfigOptions();

			ProxyProvider proxyProvider = null;

			if (rawConfigFile["proxies"] != null)
			{
				proxyProvider = new ConfigProxyProvider((JArray)rawConfigFile["proxies"], HaydenConfig.ResolveDnsLocally);
				await proxyProvider.InitializeAsync();
			}

			Log("Initialized.");
			Log("Press Q to stop archival.");

			var haydenDirectory = Path.Combine(downloadLocation, "hayden");
			Directory.CreateDirectory(haydenDirectory);

			var stateStore = new LiteDbStateStore(Path.Combine(haydenDirectory, "imagequeue.db"));
			
			var boardArchiver = new BoardArchiver(yotsubaConfig, consumer, stateStore, proxyProvider);

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
			await Task.WhenAny(archivalTask, terminateTask).ConfigureAwait(false);

			Log("Shutting down...");

			if (!tokenSource.IsCancellationRequested)
				tokenSource.Cancel();

			await archivalTask.ConfigureAwait(false);
		}


		private static readonly object ConsoleLockObject = new object();
		public static void Log(string content, bool debug = false)
		{
			if (debug && !HaydenConfig.DebugLogging)
				return;

			lock (ConsoleLockObject)
				Console.WriteLine($"[{DateTime.Now:G}] {content}");
		}

		/// <summary>
		/// Creates a task that will complete when it receives a SIGINT, SIGTERM or SIGHUP signal.
		/// </summary>
		/// <returns></returns>
		private static Task WaitForUnixKillSignal()
		{
			var sigIntHandle = new UnixSignal(Signum.SIGINT);
			var sigTermHandle = new UnixSignal(Signum.SIGTERM);
			var sigHupHandle = new UnixSignal(Signum.SIGHUP);

			return Task.Factory.StartNew(() =>
			{
				int signal = UnixSignal.WaitAny(new[] { sigIntHandle, sigTermHandle, sigHupHandle });

				if (signal == 0)
					Log("Received kill signal (SIGINT)");
				else if (signal == 1)
					Log("Received kill signal (SIGTERM)");
				else if (signal == 2)
					Log("Received kill signal (SIGHUP)");
				else
					Log("Received kill signal (unknown)");

			}, TaskCreationOptions.LongRunning);
		}

		/// <summary>
		/// Creates a task that will complete when Hayden has been signaled to terminate.
		/// </summary>
		/// <returns></returns>
		private static Task WaitForTerminateAsync()
		{
			Task unixKillSignalTask = null;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				unixKillSignalTask = WaitForUnixKillSignal();
			}


			Task consoleWaitTask = null;

			if (!Console.IsInputRedirected)
			{
				// Create a task that will return if Q or Ctrl-C is pressed in the console.
				// This code breaks if the console is redirected

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

			// Return when either task completes

			var taskArray = new[] { unixKillSignalTask, consoleWaitTask ?? new TaskCompletionSource<object>().Task };
			return Task.WhenAny(taskArray.Where(x => x != null));
		}
	}
}