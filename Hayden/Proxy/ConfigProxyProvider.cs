using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Serilog;

namespace Hayden.Proxy
{
	public class ConfigProxyProvider : ProxyProvider
	{
		protected ProxyConfig Config { get; set; }
		protected bool ResolveDnsLocally { get; set; }

		public ConfigProxyProvider(ProxyConfig config, Action<HttpClientHandler> configureClientHandlerAction = null) : base(configureClientHandlerAction)
		{
			Config = config;
			ResolveDnsLocally = Config.ResolveDnsLocally;
		}

		private int _proxyCount = 0;
		public override int ProxyCount => _proxyCount;

		public override async Task InitializeAsync(bool needsToTest, string testUrl)
		{
			List<HttpClientProxy> proxies = new List<HttpClientProxy>();

			int localCount = 1;

			foreach (string url in Config.Proxies)
			{
				if (string.IsNullOrWhiteSpace(url) )
					throw new Exception("Proxy URL must be specified and not empty.");

				if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
					throw new Exception($"Proxy URL must be valid: {url}");

				if (url == "local")
				{
					proxies.Add(new HttpClientProxy(CreateNewClient((IWebProxy)null), $"baseconnection/p{localCount++}"));
				}
				else
				{
					string username = null, password = null;

					if (!string.IsNullOrWhiteSpace(uri.UserInfo))
					{
						if (uri.UserInfo.Contains(':'))
						{
							var split = uri.UserInfo.Split(':');
							username = split[0];
							password = split[1];
						}
						else
						{
							username = uri.UserInfo;
						}
					}

					var uriBuilder = new UriBuilder(uri);
					uriBuilder.UserName = null;
					uriBuilder.Password = null;
					var realUrl = uriBuilder.Uri.AbsoluteUri;

					IWebProxy proxy = username != null
						? new WebProxy(realUrl, false, Array.Empty<string>(),
							new NetworkCredential(username, password))
						: new WebProxy(realUrl);

					proxies.Add(new HttpClientProxy(CreateNewClient(proxy), $"{username}@{realUrl}"));
				}
			}

			if (Config.EnableLocalConnection)
				proxies.Add(new HttpClientProxy(CreateNewClient((IWebProxy)null), "baseconnection/none"));

			if (!needsToTest)
			{
				foreach (var proxy in proxies)
					ProxyClients.Add(proxy);

				_proxyCount = proxies.Count;
				return;
			}

			var testTasks = proxies.Select(proxy => Task.Run(async () =>
			{
				bool success = true;

				for (int i = 0; i < 4; i++)
				{
					success = true;

					try
					{
						// TODO: this should probably be checking the target website
						var result = await proxy.Client.GetAsync(testUrl ?? "https://icanhazip.com");

						if (!result.IsSuccessStatusCode)
							success = false;
					}
					catch (Exception ex)
					{
						NetworkPolicies.Logger.Debug(ex, "Proxy {proxyName} failed", proxy.Name);

						success = false;
					}

					if (success)
						break;
				}

				if (success)
				{
					NetworkPolicies.Logger.Information("Proxy '{proxyName}' tested successfully", proxy.Name);
					ProxyClients.Add(proxy);
					Interlocked.Increment(ref _proxyCount);
				}
				else
				{
					NetworkPolicies.Logger.Warning("Proxy '{proxyName}' failed test, will be ignored", proxy.Name);
				}
			}));

			await Task.WhenAll(testTasks);

			if (proxies.Count == 0)
			{
				Log.Fatal("No proxies or connections are available.");
				Environment.Exit(1);
			}
		}
	}
}