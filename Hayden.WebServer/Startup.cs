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

namespace Hayden.WebServer
{
	public class Startup
	{
		public Startup(IConfiguration configuration, IWebHostEnvironment env)
		{
			Configuration = configuration;
			Environment = env;
		}

		public IConfiguration Configuration { get; }
		public IWebHostEnvironment Environment { get; }

		internal static ServerConfig ServerConfig { get; set; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddRazorPages();
			services.AddOptions();			

			switch (ServerConfig.Data.ProviderType?.ToLower())
			{
				case "hayden": services.AddHaydenDataProvider(ServerConfig); break;
				case "asagi": services.AddAsagiDataProvider(ServerConfig); break;
				case null: throw new Exception("Data provider type was null");
				default: throw new Exception($"Unknown data provider type: {ServerConfig.Data.ProviderType}");
			}
			
			if (ServerConfig.Search?.Enabled == true)
			{
				switch (ServerConfig.Search.ServerType?.ToLower())
				{
					case "elasticsearch": services.AddElasticSearch(ServerConfig.Search); break;
					case "lnx": services.AddLnxSearch(); break;
					case null: throw new Exception("Search server type was null");
					default: throw new Exception($"Unknown search server type: {ServerConfig.Data.ProviderType}");
				}

				services.AddHostedService<SearchSyncService>();
			}

			services.AddAuthentication()
				.AddCookie(options =>
				{
					options.Cookie.Name = "identity";
					options.Cookie.IsEssential = true;
					options.Cookie.HttpOnly = false;
					options.Cookie.SameSite = Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Strict;
					options.Events.OnRedirectToAccessDenied = context => {
						context.Response.StatusCode = 403;
						return Task.CompletedTask;
					};
				});

			services.AddSingleton<IMediaInspector, FfprobeMediaInspector>();
			services.AddSingleton<ICaptchaProvider, HCaptchaProvider>(_ => new HCaptchaProvider(
				ServerConfig.Captcha.HCaptchaTesting ? HCaptchaProvider.DummySiteKey : ServerConfig.Captcha.HCaptchaSiteKey,
				ServerConfig.Captcha.HCaptchaTesting ? HCaptchaProvider.DummySecret : ServerConfig.Captcha.HCaptchaSecret));

			services.AddMvc(x => { x.EnableEndpointRouting = false; });
		}

		public static async Task<bool> PerformInitialization(IServiceProvider services)
		{
			using var scope = services.CreateScope();

			var dataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
			return await dataProvider.PerformInitialization(scope.ServiceProvider);
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				ApiController.RegisterCodes.Add("development", ModeratorRole.Developer);
				app.UseDeveloperExceptionPage();
			}
			else
			{
				app.UseExceptionHandler("/Error");

				if (ServerConfig.RedirectToHTTPS)
				{
					// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
					app.UseHsts();
				}
			}

			app.UseForwardedHeaders(new ForwardedHeadersOptions
			{
				ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
			});

			if (!env.IsDevelopment() && ServerConfig.RedirectToHTTPS)
			{
				app.UseHttpsRedirection();
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

			if (env.IsDevelopment())
			{
				app.Use(async (context, next) =>
				{
					context.Response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5523");
					context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");

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
		public static IServiceCollection AddHaydenDataProvider(this IServiceCollection services, ServerConfig serverConfig)
		{
			services.AddScoped<IDataProvider, HaydenDataProvider>();

			if (serverConfig.Data.DBType == DatabaseType.MySql)
			{
				services.AddDbContext<HaydenDbContext>(x =>
					x.UseMySql(serverConfig.Data.DBConnectionString, ServerVersion.AutoDetect(serverConfig.Data.DBConnectionString),
						y =>
						{
							y.CommandTimeout(86400);
							y.EnableIndexOptimizedBooleanColumns();
						}));
			}
			else if (serverConfig.Data.DBType == DatabaseType.Sqlite)
			{
				services.AddDbContext<HaydenDbContext>(x =>
					x.UseSqlite(serverConfig.Data.DBConnectionString));
			}
			else
			{
				throw new Exception("Unknown database type");
			}

			return services;
		}

		public static IServiceCollection AddAsagiDataProvider(this IServiceCollection services, ServerConfig serverConfig)
		{
			services.AddScoped<IDataProvider, AsagiDataProvider>();
			services.AddSingleton(new AsagiDbContext.AsagiDbContextOptions { ConnectionString = serverConfig.Data.DBConnectionString });

			if (serverConfig.Data.DBType == DatabaseType.MySql)
			{
				services.AddDbContext<AsagiDbContext>(x =>
					x.UseMySql(serverConfig.Data.DBConnectionString, ServerVersion.AutoDetect(serverConfig.Data.DBConnectionString),
						y =>
						{
							y.CommandTimeout(86400);
							y.EnableIndexOptimizedBooleanColumns();
						}));
			}
			else
			{
				throw new Exception("Unsupported database type");
			}

			if (!string.IsNullOrWhiteSpace(serverConfig.Data.AuxiliaryDbLocation))
			{
				services.AddDbContext<AuxiliaryDbContext>(x => x
					.UseSqlite("Data Source=" + serverConfig.Data.AuxiliaryDbLocation));
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
}