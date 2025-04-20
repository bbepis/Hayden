using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.WebServer.WebDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;

namespace Hayden.WebServer.Config;

public class ConfigService : BackgroundService
{
	private WebDbContext WebDbContext { get; }

	private static ILogger Logger { get; } = SerilogManager.CreateSubLogger("ConfigService");

	private List<IDatabaseOption> RegisteredOptions { get; } = new();

	public ConfigService(IServiceProvider services)
	{
		var scope = services.CreateScope();

		WebDbContext = scope.ServiceProvider.GetRequiredService<WebDbContext>();
	}

	public ConfigService(WebDbContext context)
	{
		WebDbContext = context;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (true)
		{
			Logger.Verbose("Reloading configs from database");

			try
			{
				foreach (var option in RegisteredOptions)
					await option.Load();

				Logger.Verbose("Completed");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to reload configs from auxiliary database");
			}

			await Task.Delay(TimeSpan.FromMinutes(30));
		}
	}

	public ConfigOption<T> RegisterConfig<T>() where T : class, new()
	{
		var configOption = new ConfigOption<T>(WebDbContext);
		RegisteredOptions.Add(configOption);
		configOption.Load().Wait();
		return configOption;
	}

	public ConfigOption<T> GetConfig<T>() where T : class, new()
	{
		return (ConfigOption<T>)RegisteredOptions.FirstOrDefault(x => x is ConfigOption<T>);
	}
}

public static class ConfigExtensions
{
	public static IServiceCollection AddWebDbConfiguration(this IServiceCollection services, DbContextOptions<WebDbContext> dbContextOptions, out ConfigService configService)
	{
		var cs = new ConfigService(new WebDbContext(dbContextOptions));

		services.AddHostedService(x => cs);

		services.AddSingleton(x => cs.RegisterConfig<ServerDataConfig>());
		services.AddSingleton(x => cs.RegisterConfig<ServerSearchConfig>());

		configService = cs;
		return services;
	}
}

#region Config types

public class ServerSearchConfig
{
	private const string prefix = "Search.";

	[ConfigKey(prefix + "Enabled")]
	public bool Enabled { get; set; }
	[ConfigKey(prefix + "Debug")]
	public bool Debug { get; set; }

	[ConfigKey(prefix + "ServerType")]
	public string ServerType { get; set; }

	[ConfigKey(prefix + "Endpoint")]
	public string Endpoint { get; set; }

	[ConfigKey(prefix + "Username")]
	public string Username { get; set; }
	[ConfigKey(prefix + "Password")]
	public string Password { get; set; }

	[ConfigKey(prefix + "Enabled")]
	public string IndexName { get; set; }
}

public class ServerDataConfig
{
	private const string prefix = "Data.";

	[ConfigKey(prefix + "ImagePrefix")]
	public string ImagePrefix { get; set; }
	[ConfigKey(prefix + "FileLocation")]
	public string FileLocation { get; set; }

	[ConfigKey(prefix + "DatabaseType")]
	public DatabaseType DatabaseType { get; set; }
	[ConfigKey(prefix + "ImagePrefix")]
	public string ProviderType { get; set; }
	[ConfigKey(prefix + "ConnectionString")]
	public string ConnectionString { get; set; }
}

#endregion

#region Config classes and helpers

public interface IDatabaseOption
{
	Task Load();
	Task Save();
}

public class ConfigOption<T> : IDatabaseOption, IOptions<T> where T : class, new()
{
	private WebDbContext WebDbContext { get; }

	public ConfigOption(WebDbContext context)
	{
		WebDbContext = context;
		Value = new T();
	}

	public T Value { get; private set; }

	private List<(PropertyInfo property, string key)> ReadReflectionInfo()
	{
		var list = new List<(PropertyInfo property, string key)>();
		var props = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);

		foreach (var property in props)
		{
			var attr = property.GetCustomAttribute<ConfigKeyAttribute>();

			if (attr == null)
				continue;

			list.Add((property, attr.Key));
		}

		return list;
	}

	public async Task Load()
	{
		foreach (var (prop, key) in ReadReflectionInfo())
		{
			var configValue = (await WebDbContext.ConfigEntries.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Key == key))?.Value;

			prop.SetValue(Value, Convert.ChangeType(configValue, prop.PropertyType));
		}
	}

	public async Task Save()
	{
		foreach (var (prop, key) in ReadReflectionInfo())
		{
			var modelValue = prop.GetValue(Value);

			var configValue = await WebDbContext.ConfigEntries
				.FirstOrDefaultAsync(x => x.Key == key);

			if (configValue == null)
				WebDbContext.Add(new DBConfigEntry(key, modelValue.ToString()));
			else
				configValue.Value = modelValue.ToString();
		}

		await WebDbContext.SaveChangesAsync();
		WebDbContext.ChangeTracker.Clear();
	}

	public T Snapshot()
	{
		return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(Value));
	}
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ConfigKeyAttribute(string key) : Attribute
{
	public string Key { get; } = key;
}

#endregion