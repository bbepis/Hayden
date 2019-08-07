using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MihaZupan;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx;

namespace Hayden.Proxy
{
	public abstract class ProxyProvider
	{
		protected Action<HttpClientHandler> ConfigureClientHandlerAction { get; }

		protected ProxyProvider(Action<HttpClientHandler> configureClientHandlerHandlerAction = null)
		{
			ConfigureClientHandlerAction = configureClientHandlerHandlerAction;
		}

		public abstract Task<PoolObject<HttpClient>> RentHttpClient();
	}

	public class ConfigProxyProvider : ProxyProvider
	{
		private readonly AsyncCollection<HttpClient> proxyClients = new AsyncCollection<HttpClient>();

		private HttpClient CreateNewClient(IWebProxy proxy)
		{
			var handler = new HttpClientHandler
			{
				Proxy = proxy
			};

			ConfigureClientHandlerAction?.Invoke(handler);

			return new HttpClient(handler, true);
		}

		public ConfigProxyProvider(JArray jsonArray, Action<HttpClientHandler> configureClientHandlerAction = null) : base(configureClientHandlerAction)
		{
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
					
					proxyClients.Add(CreateNewClient(proxy));
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
					
					proxyClients.Add(CreateNewClient(proxy));
				}
				else
				{
					if (proxyType == string.Empty)
						throw new Exception("Proxy type must be specified.");

					throw new Exception($"Unknown proxy type: {proxyType}");
				}
			}

			// add a direct connection client too
			proxyClients.Add(CreateNewClient(null));
		}

		public override async Task<PoolObject<HttpClient>> RentHttpClient()
		{
			return new PoolObject<HttpClient>(await proxyClients.TakeAsync(), proxy => proxyClients.Add(proxy));
		}
	}
}