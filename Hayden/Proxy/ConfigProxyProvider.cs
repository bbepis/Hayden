using System;
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
						
						ProxyClients.Add(CreateNewClient(proxy));
					}
					else if (proxyType.Equals("socks", StringComparison.OrdinalIgnoreCase))
					{
						string url = obj["url"]?.Value<string>();

						if (string.IsNullOrWhiteSpace(url))
							throw new Exception("Proxy URL must be specified and not empty.");

						Uri uri = new Uri(url);

						string username = obj["username"]?.Value<string>();
						string password = obj["password"]?.Value<string>();

						IWebProxy proxy = username != null
							? new HttpToSocks5Proxy(uri.Host, uri.Port, username, password)
							: new HttpToSocks5Proxy(uri.Host, uri.Port);
						
						ProxyClients.Add(CreateNewClient(proxy));
					}
					else
					{
						if (proxyType == string.Empty)
							throw new Exception("Proxy type must be specified.");

						throw new Exception($"Unknown proxy type: {proxyType}");
					}
				}

			// add a direct connection client too
			ProxyClients.Add(CreateNewClient(null));
		}
	}
}