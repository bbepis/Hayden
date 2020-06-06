using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using MihaZupan;
using Newtonsoft.Json.Linq;

namespace Hayden.Proxy
{
	public class ConfigProxyProvider : ProxyProvider
	{
		public ConfigProxyProvider(JArray jsonArray, Action<HttpClientHandler> configureClientHandlerAction = null) : base(configureClientHandlerAction)
		{
			List<HttpClientProxy> proxies = new List<HttpClientProxy>();

			if (jsonArray != null)
				foreach (JObject obj in jsonArray)
				{
					string proxyType = obj["type"]?.Value<string>() ?? string.Empty;

					if (proxyType.Equals("http", StringComparison.OrdinalIgnoreCase))
					{
						string url = obj["url"]?.Value<string>();

						if (string.IsNullOrWhiteSpace(url))
							throw new Exception("Proxy URL must be specified and not empty.");

						string username = obj["username"]?.Value<string>();
						string password = obj["password"]?.Value<string>();

						IWebProxy proxy = username != null
										  ? new WebProxy(url, false, new string[0], new NetworkCredential(username, password))
										  : new WebProxy(url);

						proxies.Add(new HttpClientProxy(CreateNewClient(proxy), $"{username}@{url}"));
					}
					else if (proxyType.Equals("socks", StringComparison.OrdinalIgnoreCase))
					{
						string url = obj["url"]?.Value<string>();

						if (string.IsNullOrWhiteSpace(url))
							throw new Exception("Proxy URL must be specified and not empty.");

						Uri uri = new Uri(url);

						string username = obj["username"]?.Value<string>();
						string password = obj["password"]?.Value<string>();

						var handler = new Socks5ProxyHandler();
						handler.ProxyInfo = new ProxyInfo(uri.Host, uri.Port, username, password);

						proxies.Add(new HttpClientProxy(CreateNewClient(handler), $"{username}@{uri.Host}:{uri.Port}"));
					}
					else
					{
						if (proxyType == string.Empty)
							throw new Exception("Proxy type must be specified.");

						throw new Exception($"Unknown proxy type: {proxyType}");
					}
				}

			// add a direct connection client too
			proxies.Add(new HttpClientProxy(CreateNewClient((IWebProxy)null), "baseconnection/none"));

			foreach (var proxy in proxies)
			{
				bool success = true;

				try
				{
					var result = proxy.Client.GetAsync("https://a.4cdn.org/3/archive.json").ConfigureAwait(false).GetAwaiter().GetResult();

					if (!result.IsSuccessStatusCode)
						success = false;
				}
				catch
				{
					success = false;
				}

				if (success)
				{
					Program.Log($"Proxy '{proxy.Name}' tested successfully");
					ProxyClients.Add(proxy);
				}
				else
				{
					Program.Log($"Proxy '{proxy.Name}' failed test, will be ignored");
				}
			}
		}
	}
}