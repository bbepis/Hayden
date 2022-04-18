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
using Hayden.Models;
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

			var tokenSource = new CancellationTokenSource();

			var archivalTask = (await CreateBoardArchiverExecutor(rawConfigFile, tokenSource))();

			var terminateTask = WaitForTerminateAsync();
			await Task.WhenAny(archivalTask, terminateTask).ConfigureAwait(false);

			Log("Shutting down...");

			if (!tokenSource.IsCancellationRequested)
				tokenSource.Cancel();

			await archivalTask.ConfigureAwait(false);
		}

		private static async Task<Func<Task>> CreateBoardArchiverExecutor(JObject rawConfigFile, CancellationTokenSource tokenSource)
		{
			YotsubaConfig config;
			string downloadLocation = null;

			async Task<Func<Task>> GenericInitialize<TThread, TPost>(IFrontendApi<TThread> frontend, IThreadConsumer<TThread, TPost> consumer)
				where TPost : IPost where TThread : IThread<TPost>
			{
				await consumer.InitializeAsync();

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

				var boardArchiver = new BoardArchiver<TThread, TPost>(config, frontend, consumer, stateStore, proxyProvider);

				return () => boardArchiver.Execute(tokenSource.Token)
					.ContinueWith(task =>
					{
						if (task.IsFaulted)
						{
							Log("!! FATAL EXCEPTION !!");
							Log(task.Exception.ToString());
						}
					});
			}

			// Figure out what backend the user wants to use, and load it
			// This entire section is more of a stop-gap than actual clean code

			string sourceType = rawConfigFile["source"]["type"].Value<string>();
			string backendType = rawConfigFile["backend"]["type"].Value<string>();
			HaydenConfig = rawConfigFile["hayden"]?.ToObject<HaydenConfigOptions>() ?? new HaydenConfigOptions();

			if (HaydenConfig.ScraperType == "Search")
			{
				var altchanConfig = rawConfigFile["source"].ToObject<AltchanConfig>();
				config = altchanConfig;

				var frontend = new FoolFuukaApi(altchanConfig.ImageboardWebsite);

				var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

				downloadLocation = filesystemConfig.DownloadLocation;
				var consumer = new FoolFuukaFilesystemThreadConsumer(altchanConfig.ImageboardWebsite, filesystemConfig);

				await consumer.InitializeAsync();


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

				var boardArchiver = new SearchArchiver<FoolFuukaThread, FoolFuukaPost>(config, new SearchQuery(), frontend, consumer, stateStore, proxyProvider);

				return () => boardArchiver.Execute(tokenSource.Token)
					.ContinueWith(task =>
					{
						if (task.IsFaulted)
						{
							Log("!! FATAL EXCEPTION !!");
							Log(task.Exception.ToString());
						}
					});
			}

			if (sourceType == "4chan")
			{
				config = rawConfigFile["source"].ToObject<YotsubaConfig>();
				var frontend = new YotsubaApi();
				IThreadConsumer<YotsubaThread, YotsubaPost> consumer;

				switch (backendType)
				{
					case "Asagi":
						var asagiConfig = rawConfigFile["backend"].ToObject<AsagiConfig>();

						downloadLocation = asagiConfig.DownloadLocation;
						consumer = new AsagiThreadConsumer(asagiConfig, config.Boards.Keys);
						break;

					case "Hayden":
						var haydenConfig = rawConfigFile["backend"].ToObject<HaydenMysqlConfig>();

						downloadLocation = haydenConfig.DownloadLocation;
						consumer = new HaydenMysqlThreadConsumer(haydenConfig);
						break;

					case "Filesystem":
						var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

						downloadLocation = filesystemConfig.DownloadLocation;
						consumer = new YotsubaFilesystemThreadConsumer(filesystemConfig);
						break;

					default:
						throw new ArgumentException($"Unknown backend type {backendType}");
				}

				return await GenericInitialize(frontend, consumer);
			}

			if (sourceType == "LynxChan")
			{
				var altchanConfig = rawConfigFile["source"].ToObject<AltchanConfig>();
				config = altchanConfig;

				var frontend = new LynxChanApi(altchanConfig.ImageboardWebsite);
				IThreadConsumer<LynxChanThread, LynxChanPost> consumer;

				switch (backendType)
				{
					case "Filesystem":
						var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

						downloadLocation = filesystemConfig.DownloadLocation;
						consumer = new LynxChanFilesystemThreadConsumer(altchanConfig.ImageboardWebsite, filesystemConfig);
						break;

					default:
						throw new ArgumentException($"Unknown backend type {backendType}");
				}

				return await GenericInitialize(frontend, consumer);
			}

			if (sourceType == "Vichan")
			{
				var altchanConfig = rawConfigFile["source"].ToObject<AltchanConfig>();
				config = altchanConfig;

				var frontend = new VichanApi(altchanConfig.ImageboardWebsite);
				IThreadConsumer<VichanThread, VichanPost> consumer;

				switch (backendType)
				{
					case "Filesystem":
						var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

						downloadLocation = filesystemConfig.DownloadLocation;
						consumer = new VichanFilesystemThreadConsumer(altchanConfig.ImageboardWebsite, filesystemConfig);
						break;

					default:
						throw new ArgumentException($"Unknown backend type {backendType}");
				}

				return await GenericInitialize(frontend, consumer);
			}

			if (sourceType == "InfinityNext")
			{
				var altchanConfig = rawConfigFile["source"].ToObject<AltchanConfig>();
				config = altchanConfig;

				var frontend = new InfinityNextApi(altchanConfig.ImageboardWebsite);
				IThreadConsumer<InfinityNextThread, InfinityNextPost> consumer;

				switch (backendType)
				{
					case "Filesystem":
						var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

						downloadLocation = filesystemConfig.DownloadLocation;
						consumer = new InfinityNextFilesystemThreadConsumer(altchanConfig.ImageboardWebsite, filesystemConfig);
						break;

					default:
						throw new ArgumentException($"Unknown backend type {backendType}");
				}

				return await GenericInitialize(frontend, consumer);
			}

			if (sourceType == "Meguca")
			{
				var altchanConfig = rawConfigFile["source"].ToObject<AltchanConfig>();
				config = altchanConfig;

				var frontend = new MegucaApi(altchanConfig.ImageboardWebsite);
				IThreadConsumer<MegucaThread, MegucaPost> consumer;

				switch (backendType)
				{
					case "Filesystem":
						var filesystemConfig = rawConfigFile["backend"].ToObject<FilesystemConfig>();

						downloadLocation = filesystemConfig.DownloadLocation;
						consumer = new MegucaFilesystemThreadConsumer(altchanConfig.ImageboardWebsite, filesystemConfig);
						break;

					default:
						throw new ArgumentException($"Unknown backend type {backendType}");
				}

				return await GenericInitialize(frontend, consumer);
			}

			throw new ArgumentException($"Unknown source type {sourceType}");
		}


		private static readonly object ConsoleLockObject = new object();
		public static void Log(string content, bool debug = false)
		{
			if (debug && HaydenConfig?.DebugLogging != true)
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