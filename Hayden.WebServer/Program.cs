using System.CommandLine;
using System.Threading.Tasks;
using Hayden.WebServer.Config;
using Hayden.WebServer.WebDb;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

		var auxiliaryDbOption = new Argument<string>("database", () => "hayden-database.db", "The path to the Sqlite website database to use");
		var portOption = new Option<ushort>(new[] { "-p", "--port" }, () => 5000, "Port to listen to requests from");
		var sqlLoggingOption = new Option<bool>(new[] { "--sql-logging" }, "Enable logging for SQL commands");

		rootCommand.AddArgument(auxiliaryDbOption);
		rootCommand.AddOption(portOption);
		rootCommand.AddOption(sqlLoggingOption);
		rootCommand.SetHandler((auxiliaryDb, port, sqlLogging) => RunServer(args, auxiliaryDb, sqlLogging, port),
			auxiliaryDbOption, portOption, sqlLoggingOption);

		return rootCommand;
	}

	private static async Task<int> RunServer(string[] args, string auxiliaryDb, bool sqlLogging, ushort port)
	{
		if (!sqlLogging)
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

		var host = CreateHostBuilder(args, auxiliaryDb, port)
			.Build();

		if (!await Startup.PerformInitialization(host.Services))
			return 1;

		await host.RunAsync();
		return 0;
	}

	public static IHostBuilder CreateHostBuilder(string[] args, string auxiliaryDbPath, ushort port)
	{
		var initialWebDbOptions = new DbContextOptionsBuilder<WebDbContext>()
			.UseSqlite($"Data Source={auxiliaryDbPath}")
			.Options;

		return Host.CreateDefaultBuilder(args)
			.UseSerilog()
			.ConfigureWebHostDefaults(webBuilder =>
			{
				ConfigService configService = null;

				webBuilder
					.ConfigureServices(x => x.AddWebDbConfiguration(initialWebDbOptions, out configService))
					.UseStartup(e => new Startup(e.Configuration, e.HostingEnvironment, configService))
					.ConfigureKestrel(c => c.ListenAnyIP(port));
			});

	}
}