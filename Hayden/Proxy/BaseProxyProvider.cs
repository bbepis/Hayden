using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Hayden.Proxy
{
	public class HttpClientProxy
	{
		public HttpClient Client { get; }
		public string Name { get; }

		public HttpClientProxy(HttpClient client, string name)
		{
			Client = client;
			Name = name;
		}
	}

	public abstract class ProxyProvider
	{
		protected Action<HttpClientHandler> ConfigureClientHandlerAction { get; }
		protected virtual AsyncCollection<HttpClientProxy> ProxyClients { get; } = new AsyncCollection<HttpClientProxy>();

		protected ProxyProvider(Action<HttpClientHandler> configureClientHandlerHandlerAction = null)
		{
			ConfigureClientHandlerAction = configureClientHandlerHandlerAction;
		}

		public virtual async Task<PoolObject<HttpClientProxy>> RentHttpClient()
		{
			return new PoolObject<HttpClientProxy>(await ProxyClients.TakeAsync(), proxy => ProxyClients.Add(proxy));
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

			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Hayden/0.7.0");
			httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");

			return httpClient;
		}
	}
}