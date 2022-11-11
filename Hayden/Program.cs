using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Cache;
using Hayden.Config;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json.Linq;

namespace Hayden
{
	public class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hayden v0.8.0");
			Console.WriteLine("By Bepis");

			if (args.Length != 1)
			{
				Console.WriteLine("Usage: hayden <config file location>");
				return;
			}

			// restrict threadpool size to prevent excessive amounts of unmanaged memory usage
			ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
			ThreadPool.SetMaxThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 4);

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
			var serviceCollection = new ServiceCollection();

			var configFile = rawConfigFile.ToObject<ConfigFile>();

			if (configFile == null)
				throw new Exception("Invalid config file");

			configFile.Hayden ??= new HaydenConfigOptions();

			if (configFile.Source == null)
				throw new Exception("Source config section must be present.");

			if (configFile.Consumer == null)
				throw new Exception("Consumer config section must be present.");

			serviceCollection.AddSingleton(configFile);
			serviceCollection.AddSingleton(configFile.Source);
			serviceCollection.AddSingleton(configFile.Consumer);
			serviceCollection.AddSingleton(configFile.Hayden);

			DebugLogging = configFile.Hayden.DebugLogging;


			switch (configFile.Source.Type)
			{
				case "4chan":         serviceCollection.AddSingleton<IFrontendApi, YotsubaApi>(); break;
				case "Vichan":        serviceCollection.AddSingleton<IFrontendApi, VichanApi>(); break;
				case "LynxChan":      serviceCollection.AddSingleton<IFrontendApi, LynxChanApi>(); break;
				case "Meguca":        serviceCollection.AddSingleton<IFrontendApi, MegucaApi>(); break;
				case "InfinityNext":  serviceCollection.AddSingleton<IFrontendApi, InfinityNextApi>(); break;
				case "Ponychan":      serviceCollection.AddSingleton<IFrontendApi, PonychanApi>(); break;
				case "ASPNetChan":    serviceCollection.AddSingleton<IFrontendApi, ASPNetChanApi>(); break;
				case "FoolFuuka":     serviceCollection.AddSingleton<ISearchableFrontendApi, FoolFuukaApi>(); break;
				default:              throw new Exception($"Unknown source type: {configFile.Source.Type}");
			}
			
			switch (configFile.Consumer.Type)
			{
				case "Hayden":        serviceCollection.AddSingleton<IThreadConsumer, HaydenMysqlThreadConsumer>(); break;
				case "Filesystem":    serviceCollection.AddSingleton<IThreadConsumer, FilesystemThreadConsumer>(); break;
				case "Asagi":         serviceCollection.AddSingleton<IThreadConsumer, AsagiThreadConsumer>(); break;
				case "Null":          serviceCollection.AddSingleton<IThreadConsumer, NullThreadConsumer>(); break;
				default:              throw new Exception($"Unknown consumer type: {configFile.Consumer.Type}");
			}

			if (configFile.Consumer.Type == "Asagi" && configFile.Source.Type != "4chan")
				throw new Exception("The 'Asagi' backend only supports a source type of '4chan'.");


			var haydenDirectory = Path.Combine(configFile.Consumer.DownloadLocation, "hayden");
			Directory.CreateDirectory(haydenDirectory);

			// TODO: make this & proxy provider configurable
			var stateStore = new LiteDbStateStore(Path.Combine(haydenDirectory, "imagequeue.db"));
			serviceCollection.AddSingleton<IStateStore>(stateStore);

			ProxyProvider proxyProvider = null;

			if (rawConfigFile["proxies"] != null)
			{
				proxyProvider = new ConfigProxyProvider((JArray)rawConfigFile["proxies"], configFile.Hayden.ResolveDnsLocally);
				await proxyProvider.InitializeAsync();
				serviceCollection.AddSingleton<ProxyProvider>(proxyProvider);
			}

			serviceCollection.AddSingleton<IFileSystem, FileSystem>();

			var serviceProvider = serviceCollection.BuildServiceProvider();

			await serviceProvider.GetRequiredService<IThreadConsumer>().InitializeAsync();

			Log("Initialized.");
			Log("Press Q to stop archival.");

			if (configFile.Hayden.ScraperType == "Search")
			{
				var searchArchiver = ActivatorUtilities.CreateInstance<SearchArchiver>(serviceProvider);

				return () => searchArchiver.Execute(tokenSource.Token)
					.ContinueWith(task =>
					{
						if (task.IsFaulted)
						{
							Log("!! FATAL EXCEPTION !!");
							Log(task.Exception.ToString());
						}
					});
			}
			else
			{
				var boardArchiver = ActivatorUtilities.CreateInstance<BoardArchiver>(serviceProvider);

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
		}

		private static bool DebugLogging;
		private static readonly object ConsoleLockObject = new object();
		public static void Log(string content, bool debug = false)
		{
			if (debug && DebugLogging != true)
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
