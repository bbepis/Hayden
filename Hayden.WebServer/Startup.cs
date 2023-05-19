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
using Nest;
using Hayden.MediaInfo;
using Hayden.WebServer.Data;
using Hayden.WebServer.Services.Captcha;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;

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

		private ServerConfig ServerConfig { get; set; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddRazorPages();

			services.AddOptions();

			var section = Configuration.GetSection("config");

			ServerConfig = section.Get<ServerConfig>();
			services.Configure<ServerConfig>(section);

			switch (ServerConfig.Data.ProviderType)
			{
				case "Hayden": services.AddHaydenDataProvider(ServerConfig); break;
				case "Asagi": services.AddAsagiDataProvider(ServerConfig); break;
				case null: throw new Exception("Data provider type was null");
				default: throw new Exception($"Unknown data provider type: {ServerConfig.Data.ProviderType}");
			}
			
			if (ServerConfig.Elasticsearch?.Enabled == true)
			{
				services.AddSingleton<ElasticClient>(x =>
				{
					var settings = new ConnectionSettings(new Uri(ServerConfig.Elasticsearch.Endpoint));
						//.DefaultMappingFor<PostIndex>(map => map.IndexName(PostIndex.IndexName))

					if (ServerConfig.Elasticsearch.Username != null)
						settings.BasicAuthentication(ServerConfig.Elasticsearch.Username, ServerConfig.Elasticsearch.Password);

					if (ServerConfig.Elasticsearch.Debug)
						settings.EnableDebugMode();

					return new ElasticClient(settings);
				});
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

				if (ServerConfig.EnableHTTPS)
				{
					// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
					app.UseHsts();
				}
			}

			app.UseForwardedHeaders(new ForwardedHeadersOptions
			{
				ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
			});

			if (!env.IsDevelopment() && ServerConfig.EnableHTTPS)
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

			if (env.IsDevelopment())
			{
				app.Use(async (context, next) =>
				{
					context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

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
}