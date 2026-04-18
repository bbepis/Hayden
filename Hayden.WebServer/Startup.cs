using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Hayden.MediaInfo;
using Hayden.WebServer.Data;
using Hayden.WebServer.Search;
using Hayden.WebServer.Services;
using Hayden.WebServer.Services.Captcha;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;
using System.Net;
using Hayden.Config;
using Microsoft.EntityFrameworkCore;
using Hayden.Consumers.Asagi;
using Nest;
using Hayden.WebServer.WebDb;
using Hayden.WebServer.Config;
using Hayden.WebServer.Routing;

namespace Hayden.WebServer;

public class Startup : StartupBase
{
	public Startup(IConfiguration configuration, IWebHostEnvironment env, ConfigService configService)
	{
		Configuration = configuration;
		Environment = env;
		ConfigService = configService;
	}

	public IConfiguration Configuration { get; }
	public IWebHostEnvironment Environment { get; }
	public ConfigService ConfigService { get; }

	// This method gets called by the runtime. Use this method to add services to the container.
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddRazorPages();
		services.AddOptions();

		var dataConfig = ConfigService.GetConfig<ServerDataConfig>().Snapshot();
		var searchConfig = ConfigService.GetConfig<ServerSearchConfig>().Snapshot();

		switch (dataConfig.ProviderType?.ToLower())
		{
			case "hayden": services.AddHaydenDataProvider(dataConfig); break;
			case "asagi": services.AddAsagiDataProvider(dataConfig); break;
			case null: throw new Exception("Data provider type was not provided");
			default: throw new Exception($"Unknown data provider type: {dataConfig.ProviderType}");
		}
			
		if (searchConfig.Enabled == true)
		{
			switch (searchConfig.ServerType?.ToLower())
			{
				case "elasticsearch": services.AddElasticSearch(searchConfig); break;
				case "lnx": services.AddLnxSearch(); break;
				case null: throw new Exception("Search server type was not provided");
				default: throw new Exception($"Unknown search server type: {searchConfig.ServerType}");
			}

			services.AddHostedService<SearchSyncService>();
		}

		services.AddAuthentication()
			.AddCookie(options =>
			{
				options.Cookie.Name = "identity";
				options.Cookie.IsEssential = true;
				options.Cookie.HttpOnly = true;
				options.Cookie.SameSite = Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Strict;
				options.Events.OnRedirectToAccessDenied = context => {
					context.Response.StatusCode = 403;
					return Task.CompletedTask;
				};

				if (Environment.IsDevelopment())
					options.Cookie.SecurePolicy = CookieSecurePolicy.None;
			});

		services.AddSingleton<IMediaInspector, FfprobeMediaInspector>();

		var captchaConfig = ConfigService.GetConfig<ServerCaptchaConfig>().Value;
		services.AddSingleton<ICaptchaProvider, HCaptchaProvider>(_ => new HCaptchaProvider(
			captchaConfig.CaptchaTesting ? HCaptchaProvider.DummySiteKey : captchaConfig.CaptchaSiteKey,
			captchaConfig.CaptchaTesting ? HCaptchaProvider.DummySecret : captchaConfig.CaptchaSecret));

		services.AddHostedService<StatsService>();

		services.AddMvc(x => { x.EnableEndpointRouting = false; });
	}

	public static async Task<bool> PerformInitialization(IServiceProvider services)
	{
		using var scope = services.CreateScope();

		var dataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
		return await dataProvider.PerformInitialization(scope.ServiceProvider);
	}

	// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
	public override void Configure(IApplicationBuilder app)
	{
		app.UseForwardedHeaders(new ForwardedHeadersOptions
		{
			ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
		});

		if (Environment.IsDevelopment())
		{
			ApiController.RegisterCodes.Add("development", ModeratorRole.Developer);
			app.UseDeveloperExceptionPage();
		}
		else
		{
			app.UseExceptionHandler("/Error");
			app.UseMiddleware<HstsHttpsMiddleware>();
		}

		string overridePath = Path.GetFullPath("wwwroot-override");

		if (Directory.Exists(overridePath))
			app.UseStaticFiles(new StaticFileOptions
			{
				FileProvider = new PhysicalFileProvider(overridePath)
			});

		app.UseStaticFiles();

		app.Use(async (context, next) =>
		{
			if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIps))
			{
				context.Connection.RemoteIpAddress = IPAddress.Parse(cfIps[0]);
			}

			await next();
		});

		if (Environment.IsDevelopment())
		{
			app.Use(async (context, next) =>
			{
				context.Response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173");
				context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
				context.Response.Headers.Add("access-control-expose-headers", "Set-Cookie");

				await next();
			});
		}

		app.UseRouting();

		app.Use(async (context, next) =>
		{
			var authenticateResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

			if (authenticateResult.Succeeded)
				context.User = authenticateResult.Principal;

			await next();
		});

		app.UseAuthentication();
		app.UseAuthorization();

		app.UseMvc();
		//app.UseMvc(routes =>
		//{
		//	if (Config.ApiMode)
		//	{
		//		routes.MapRoute("api", "/api/{action=Index}", new { controller = "ArchiveApi" });
		//		routes.MapRoute("frontend-index", "/", new { controller = "ArchiveApi", action = "SvelteFrontend" });
		//		routes.MapRoute("frontend-thread", "{board}/thread/{threadId}", new { controller = "ArchiveApi", action = "SvelteFrontend" });
		//		routes.MapRoute("frontend-privacy", "privacy", new { controller = "ArchiveApi", action = "SvelteFrontend" });
		//	}
		//	else
		//	{
		//		routes.MapRoute("default", "{controller=Archive}/{action=Index}");
		//	}
		//});
	}
}

public static class ServiceExtensions
{
	public static IServiceCollection AddWebDb(this IServiceCollection services, string filename)
	{
		services.AddDbContext<WebDbContext>(x =>
			x.UseSqlite($"Data Source={filename}"));

		return services;
	}

	public static IServiceCollection AddHaydenDataProvider(this IServiceCollection services, ServerDataConfig dataConfig)
	{
		services.AddScoped<IDataProvider, HaydenDataProvider>();

		if (dataConfig.DatabaseType == DatabaseType.MySql)
		{
			services.AddDbContext<HaydenDbContext>(x =>
				x.UseMySql(dataConfig.ConnectionString, ServerVersion.AutoDetect(dataConfig.ConnectionString),
						y =>
						{
							y.CommandTimeout(86400);
							y.EnableIndexOptimizedBooleanColumns();
						})
					.AddQueryHints());
		}
		else if (dataConfig.DatabaseType == DatabaseType.Sqlite)
		{
			services.AddDbContext<HaydenDbContext>(x =>
				x.UseSqlite(dataConfig.ConnectionString));
		}
		else
		{
			throw new Exception("Unknown database type");
		}

		return services;
	}

	public static IServiceCollection AddAsagiDataProvider(this IServiceCollection services, ServerDataConfig dataConfig)
	{
		services.AddScoped<IDataProvider, AsagiDataProvider>();

		if (dataConfig.DatabaseType == DatabaseType.MySql)
		{
			services.AddDbContext<AsagiDbContext>(builder =>
				((DbContextOptionsBuilder<AsagiDbContext>)builder)
				.ConfigureAsagiMysql(dataConfig.ConnectionString));
		}
		else
		{
			throw new Exception("Unsupported database type");
		}

		return services;
	}

	public static IServiceCollection AddElasticSearch(this IServiceCollection services, ServerSearchConfig serverConfig)
	{
		services.AddSingleton<ElasticClient>(x =>
		{
			var settings = new ConnectionSettings(new Uri(serverConfig.Endpoint));
			//.DefaultMappingFor<PostIndex>(map => map.IndexName(PostIndex.IndexName))

			if (serverConfig.Username != null)
				settings.BasicAuthentication(serverConfig.Username, serverConfig.Password);

			if (serverConfig.Debug)
				settings.EnableDebugMode();

			return new ElasticClient(settings);
		});

		services.AddSingleton<ISearchService, ElasticSearch>();

		return services;
	}

	public static IServiceCollection AddLnxSearch(this IServiceCollection services)
	{
		services.AddSingleton<ISearchService, LnxSearch>();

		return services;
	}
}