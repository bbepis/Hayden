using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Hayden.Proxy
{
	public abstract class ProxyProvider
	{
		protected Action<HttpClientHandler> ConfigureClientHandlerAction { get; }
		protected virtual AsyncCollection<HttpClient> ProxyClients { get; } = new AsyncCollection<HttpClient>();

		protected ProxyProvider(Action<HttpClientHandler> configureClientHandlerHandlerAction = null)
		{
			ConfigureClientHandlerAction = configureClientHandlerHandlerAction;
		}

		public virtual async Task<PoolObject<HttpClient>> RentHttpClient()
		{
			return new PoolObject<HttpClient>(await ProxyClients.TakeAsync(), proxy => ProxyClients.Add(proxy));
		}

		protected virtual HttpClient CreateNewClient(IWebProxy proxy)
		{
			var handler = new HttpClientHandler
			{
				MaxConnectionsPerServer = 24,
				Proxy = proxy,
				UseCookies = false,
				UseProxy = true,
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
			};

			ConfigureClientHandlerAction?.Invoke(handler);

			var httpClient = new HttpClient(handler, true);

			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Hayden/0.5.0");
			httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");

			return httpClient;
		}
	}
}