﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NSocks;

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

		public override async Task InitializeAsync()
		{
			List<HttpClientProxy> proxies = new List<HttpClientProxy>();

			if (JsonArray != null)
				foreach (JObject obj in JsonArray)
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


						var handler = new Socks5Handler(uri, username, password);
						handler.ResolveDnsLocally = ResolveDnsLocally;
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
						Program.Log($"{proxy.Name} failed: {ex}", true);

						success = false;
					}

					if (success)
						break;
				}

				if (success)
				{
					Program.Log($"Proxy '{proxy.Name}' tested successfully");
					ProxyClients.Add(proxy);
					Interlocked.Increment(ref _proxyCount);
				}
				else
				{
					Program.Log($"Proxy '{proxy.Name}' failed test, will be ignored");
				}
			}));

			await Task.WhenAll(testTasks);
		}
	}
}