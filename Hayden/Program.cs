using System;
using System.CommandLine;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Cache;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.MediaInfo;
using Hayden.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using Hayden.ImportExport;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Hayden;

public class Program
{
	static async Task<int> Main(string[] args)
	{
		SerilogManager.SetLogger();

		Log.Information("Hayden v0.9.0");
		Log.Information("By Bepis");

		// restrict threadpool size to prevent excessive amounts of unmanaged memory usage
		ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
		ThreadPool.SetMaxThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 4);

		var rootCommand = CreateCommandParser();

		return await rootCommand.InvokeAsync(args);
	}

	private static RootCommand CreateCommandParser()
	{
		var rootCommand = new RootCommand("Hayden all-chan archival software");

		var scrapeCommand = new Command("scrape", "Scrape using a config");
		var configArg = new Argument<string>("config path", () => "config.json", "The path to the .json config file") { Arity = ArgumentArity.ExactlyOne };
		scrapeCommand.Add(configArg);
		scrapeCommand.SetHandler(RunScrape, configArg);

		rootCommand.Add(scrapeCommand);
		
		var dumpCommand = new Command("dump", "Dump a database to a standard format");
		var connectionStringArg = new Argument<string>("connection string", "The path to the .json config file") { Arity = ArgumentArity.ExactlyOne };
		dumpCommand.Add(connectionStringArg);

		rootCommand.Add(dumpCommand);
		
		var generateConfigCommand = new Command("genconfig", "Dump a database to a standard format");
		var generatedConfigArgument = new Argument<string>("config file", "The path to the .json config file to generate") { Arity = ArgumentArity.ExactlyOne };
		generateConfigCommand.Add(generatedConfigArgument);

		generateConfigCommand.SetHandler(GenerateConfig, generatedConfigArgument);

		rootCommand.Add(generateConfigCommand);

		return rootCommand;
	}

	private static void GenerateConfig(string configFile)
	{
		var configObject = new ConfigFile
		{
			Hayden = new HaydenConfigOptions
			{
				DebugLogging = false,
				ScraperType = "Archive",
				ResolveDnsLocally = true
			},
			Source = new Config.SourceConfig()
			{
				Type = "4chan",
				ImageboardWebsite = "",
				DbConnectionString = "",
				Boards = new Dictionary<string, Config.BoardRulesConfig>
				{
					["a"] = new Config.BoardRulesConfig(),
					["b"] = new Config.BoardRulesConfig(),
				},
				ApiDelay = 1,
				BoardScrapeDelay = 30,
				SingleScan = false,
				ReadArchive = true
			},
			Consumer = new Config.ConsumerConfig
			{
				Type = "Foobar",
				DatabaseType = Config.DatabaseType.None,
				ConnectionString = "",
				DownloadLocation = "",
				IgnoreSha1Hash = false,
				IgnoreMd5Hash = false,
				ThumbnailsEnabled = false,
				FullImagesEnabled = false,
				ConsolidationMode = Config.ConsolidationMode.Authoritative
			}
		};

		File.WriteAllText(configFile, JsonConvert.SerializeObject(configObject, new JsonSerializerSettings()
		{
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Include,
			Converters = new List<JsonConverter>
			{
				new StringEnumConverter(new DefaultNamingStrategy(), false)
			}
		}));
	}

	private static async Task<int> RunScrape(string configPath)
	{
		if (!File.Exists(configPath))
		{
			Log.Error("Could not find config: {configPath}", configPath);
			return 2;
		}

		var rawConfigFile = JObject.Parse(File.ReadAllText(configPath));

		var tokenSource = new CancellationTokenSource();

		var archivalTask = Task.Run(() => CreateBoardArchiverExecutor(rawConfigFile, tokenSource));

		var terminateTask = WaitForTerminateAsync();
		await Task.WhenAny(archivalTask, terminateTask).ConfigureAwait(false);

		Log.Warning("Shutting down...");

		if (!tokenSource.IsCancellationRequested)
			tokenSource.Cancel();

		return await archivalTask.ConfigureAwait(false);
	}

	private static async Task<int> CreateBoardArchiverExecutor(JObject rawConfigFile, CancellationTokenSource tokenSource)
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

		SerilogManager.LevelSwitch.MinimumLevel = configFile.Hayden.DebugLogging ? LogEventLevel.Verbose : LogEventLevel.Information;

		switch (configFile.Source.Type)
		{
			case "4chan":         serviceCollection.AddSingleton<IFrontendApi, YotsubaApi>(); break;
			case "Vichan":        serviceCollection.AddSingleton<IFrontendApi, VichanApi>(); break;
			case "LynxChan":      serviceCollection.AddSingleton<IFrontendApi, LynxChanApi>(); break;
			case "Meguca":        serviceCollection.AddSingleton<IFrontendApi, MegucaApi>(); break;
			case "InfinityNext":  serviceCollection.AddSingleton<IFrontendApi, InfinityNextApi>(); break;
			case "Ponychan":      serviceCollection.AddSingleton<IFrontendApi, PonychanApi>(); break;
			case "ASPNetChan":    serviceCollection.AddSingleton<IFrontendApi, ASPNetChanApi>(); break;
			case "Tinyboard":     serviceCollection.AddSingleton<IFrontendApi, TinyboardApi>(); break;
			case "FoolFuuka":     serviceCollection.AddSingletonMulti<IFrontendApi, ISearchableFrontendApi, FoolFuukaApi>(); break;
			case "Fuuka":         serviceCollection.AddSingleton<IImporter, FuukaImporter>(); break;
			case "Asagi":         serviceCollection.AddSingleton<IImporter, AsagiImporter>(); break;
			case "JSArchive":     serviceCollection.AddSingleton<IImporter, JSArchiveImporter>(); break;
			case "Tar":           serviceCollection.AddSingleton<IForwardOnlyImporter, TarJsonImporter>(); break;
			case "Json":          serviceCollection.AddSingleton<IForwardOnlyImporter, JsonImporter>(); break;
			default:              throw new Exception($"Unknown source type: {configFile.Source.Type}");
		}
			
		switch (configFile.Consumer.Type)
		{
			case "Hayden":        serviceCollection.AddSingleton<IThreadConsumer, HaydenThreadConsumer>(); break;
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
		var stateStore = new SqliteStateStore($"Data Source={Path.Combine(haydenDirectory, "imagequeue.db")}");
		serviceCollection.AddSingleton<IStateStore>(stateStore);

		ProxyProvider proxyProvider = null;

		if (rawConfigFile["proxies"] != null)
		{
			proxyProvider = new ConfigProxyProvider((JArray)rawConfigFile["proxies"], configFile.Hayden.ResolveDnsLocally);
			await proxyProvider.InitializeAsync();
			serviceCollection.AddSingleton<ProxyProvider>(proxyProvider);
		}

		serviceCollection.AddSingleton<IFileSystem, FileSystem>();
		serviceCollection.AddSingleton<IMediaInspector, FfprobeMediaInspector>();

		var serviceProvider = serviceCollection.BuildServiceProvider();

		await serviceProvider.GetRequiredService<IThreadConsumer>().InitializeAsync();

		Log.Information("Initialized.");
		Log.Information("Press Q to stop archival.");

		Task archiveTask;

		if (configFile.Hayden.ScraperType == "Search")
		{
			var searchArchiver = ActivatorUtilities.CreateInstance<SearchArchiver>(serviceProvider);
			archiveTask = searchArchiver.Execute(tokenSource.Token);
		}
		else
		{
			BoardArchiver boardArchiver;

			if (configFile.Hayden.ScraperType == "Import")
				boardArchiver = ActivatorUtilities.CreateInstance<ImportArchiver>(serviceProvider);
			else
				boardArchiver = ActivatorUtilities.CreateInstance<BoardArchiver>(serviceProvider);

			archiveTask = boardArchiver.Execute(tokenSource.Token);
		}

		try
		{
			await archiveTask;

			return 0;
		}
		catch (TaskCanceledException)
		{
			return 1;
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "!! FATAL EXCEPTION !!");

			return 1;
		}
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
				Log.Warning("Received kill signal (SIGINT)");
			else if (signal == 1)
				Log.Warning("Received kill signal (SIGTERM)");
			else if (signal == 2)
				Log.Warning("Received kill signal (SIGHUP)");
			else
				Log.Warning("Received kill signal (unknown)");

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

internal static class LoggingFunctions
{
	public static LogEventPropertyValue FilterSourceContext(
		LogEventPropertyValue context)
	{
		if (context is ScalarValue sv && sv.Value != null && sv.Value is string s)
		{
			if (s == "Microsoft.Hosting.Lifetime")
				return new ScalarValue(string.Empty);

			return new ScalarValue($" [{s}]");
		}

		// Undefined - argument was not a string.
		return null;
	}

	public static LogEventPropertyValue AddRequestInfo(
		LogEvent @event)
	{
		if (@event.Level >= LogEventLevel.Error)
		{
			return new ScalarValue(@event.Properties.TryGetValue("requestInfo", out var requestInfo) ? "\n" + requestInfo : null);
		}

		return null;
	}

	public static LogEventPropertyValue IsError(
		LogEvent @event)
	{
		return new ScalarValue(@event.Level >= LogEventLevel.Error);
	}
}