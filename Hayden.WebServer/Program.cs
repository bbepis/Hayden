using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using Serilog.Events;

namespace Hayden.WebServer;

public static class Program
{
	public static async Task<int> Main(string[] args)
	{
		return await CreateRootCommand(args).InvokeAsync(args);
	}

	private static RootCommand CreateRootCommand(string[] args)
	{
		var rootCommand = new RootCommand();

		var configFileOption = new Option<string>(new[] { "-c", "--config" }, () => "config.json", "Configuration file to use when launching");
		var portOption = new Option<ushort>(new[] { "-p", "--port" }, () => 5000, "Port to listen to requests from");

		rootCommand.AddOption(configFileOption);
		rootCommand.AddOption(portOption);
		rootCommand.SetHandler((configFile, port) => RunServer(args, configFile, port),
			configFileOption, portOption);

		var createConfigCommand = new Command("genconfig", "Generate config");
		var configFileArgument = new Argument<string>("config file", () => "config.json", "Write the config file to this location");

		createConfigCommand.Add(configFileArgument);
		createConfigCommand.SetHandler(GenerateConfig, configFileArgument);

		rootCommand.Add(createConfigCommand);

		return rootCommand;
	}

	private static async Task<int> RunServer(string[] args, string configFile, ushort port)
	{
		if (!File.Exists(configFile))
			throw new Exception("Could not find configuration file. Make sure it exists (defaults to config.json)");

		var	serverConfig = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(configFile));

		if (!serverConfig.SqlLogging)
		{
			SerilogManager.Config
				.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
				.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.AspNetCore.StaticFiles", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.Extensions.Hosting.Internal", LogEventLevel.Error)
				.MinimumLevel.Override("Microsoft.AspNetCore.Server.Kestrel", LogEventLevel.Error);
		}

		SerilogManager.SetLogger();

		var host = CreateHostBuilder(args, serverConfig, port)
			.Build();

		if (!await Startup.PerformInitialization(host.Services))
			return 1;

		await host.RunAsync();
		return 0;
	}

	public static IHostBuilder CreateHostBuilder(string[] args, ServerConfig config, ushort port) =>
		Host.CreateDefaultBuilder(args)
			.UseSerilog()
			.ConfigureServices(services =>
			{
				Startup.ServerConfig = config;
				services.AddSingleton(Options.Create(config));
			})
			.ConfigureWebHostDefaults(webBuilder =>
			{
				webBuilder.UseStartup<Startup>()
					.ConfigureKestrel(c => c.ListenAnyIP(port));
			});

	private static void GenerateConfig(string outputFile)
	{
		var sampleServerConfig = new ServerConfig()
		{
			Data = new ServerDataConfig()
			{
				ProviderType = "Foobar",
				DBType = Config.DatabaseType.None,
				DBConnectionString = "Foobar",
				AuxiliaryDbLocation = null,
				FileLocation = null,
				ImagePrefix = null
			},

			Search = new ServerSearchConfig()
			{
				Enabled = false,
				ServerType = "Foobar",
				Endpoint = "localhost:1234",
				Debug = false,
				IndexName = "hayden_index",
				Username = "username",
				Password = "password"
			},

			Captcha = new ServerCaptchaConfig()
			{
				HCaptchaTesting = true,
				HCaptchaSiteKey = "Foobar",
				HCaptchaSecret = "Foobar"
			},

			Extensions = new ServerExtensionsConfig(),

			Settings = new ServerSettingsConfig
			{
				CompactBoardsUi = false,
				MaxFileUploadSizeMB = 4,
				SiteName = "Hayden Archive",
				ShiftJisArt = null
			},

			RedirectToHTTPS = false,
			SqlLogging = false
		};

		File.WriteAllText(outputFile, JsonConvert.SerializeObject(sampleServerConfig, new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Include,
			Converters = new List<JsonConverter>
			{
				new StringEnumConverter(new DefaultNamingStrategy(), false)
			}
		}));
	}
}