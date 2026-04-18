using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using Hayden.WebServer.Config;
using Microsoft.AspNetCore.Http.Extensions;

namespace Hayden.WebServer.Routing;

public class HstsHttpsMiddleware
{
	private ConfigOption<ServerSiteConfig> SiteConfig { get; }
	private RequestDelegate Next { get; }

	private long MaxAge = (long)TimeSpan.FromDays(30).TotalSeconds;
	private string[] ExcludedHosts =
	[
		"localhost",
		"127.0.0.1", // ipv4
		"[::1]" // ipv6
	];

	private string HeaderValue;

	public HstsHttpsMiddleware(RequestDelegate next, ConfigOption<ServerSiteConfig> siteConfig)
	{
		SiteConfig = siteConfig;
		Next = next;
		HeaderValue = $"max-age={MaxAge}";
	}

	public Task Invoke(HttpContext context)
	{
		if (!context.Request.IsHttps
		    || IsHostExcluded(context.Request.Host.Host)
		    || !SiteConfig.Value.EnableHttps)
		{
			return Next(context);
		}

		// uncomment to re-enable HSTS
		//context.Response.Headers.StrictTransportSecurity = HeaderValue;

		var request = context.Request;
		var redirectUrl = UriHelper.BuildAbsolute(
			"https",
			context.Request.Host,
			request.PathBase,
			request.Path,
			request.QueryString);

		context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
		context.Response.Headers.Location = redirectUrl;

		return Task.CompletedTask;
	}

	private bool IsHostExcluded(string host)
	{
		foreach (var excludedHost in ExcludedHosts)
		{
			if (string.Equals(host, excludedHost, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}