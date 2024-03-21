using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Newtonsoft.Json.Linq;

namespace Hayden.Proxy
{
	public class ConfigProxyProvider : ProxyProvider
	{
		protected JArray JsonArray { get; set; }
		protected bool ResolveDnsLocally { get; set; }

		public ConfigProxyProvider(JArray jsonArray, bool resolveDnsLocally, Action<HttpClientHandler> configureClientHandlerAction = null) : base(configureClientHandlerAction)
		{
			JsonArray = jsonArray;
			ResolveDnsLocally = resolveDnsLocally;
		}

		private int _proxyCount = 0;
		public override int ProxyCount => _proxyCount;

		public override async Task InitializeAsync(bool needsToTest)
		{
			List<HttpClientProxy> proxies = new List<HttpClientProxy>();

			int localCount = 1;

			if (JsonArray != null)
				foreach (JObject obj in JsonArray)
				{
					string url = obj["url"]?.Value<string>();

					if (string.IsNullOrWhiteSpace(url))
						throw new Exception("Proxy URL must be specified and not empty.");

					if (url == "local")
					{
						proxies.Add(new HttpClientProxy(CreateNewClient((IWebProxy)null), $"baseconnection/p{localCount++}"));
					}
					else
					{
						string username = obj["username"]?.Value<string>();
						string password = obj["password"]?.Value<string>();

						IWebProxy proxy = username != null
							? new WebProxy(url, false, Array.Empty<string>(),
								new NetworkCredential(username, password))
							: new WebProxy(url);

						proxies.Add(new HttpClientProxy(CreateNewClient(proxy), $"{username}@{url}"));
					}
				}

			// add a direct connection client too
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
						var result = await proxy.Client.GetAsync("https://a.4cdn.org/po/catalog.json");

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
		}
	}
}