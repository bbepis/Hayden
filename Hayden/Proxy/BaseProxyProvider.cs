using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Hayden.Proxy
{
	/// <summary>
	/// A class that holds information about a specific <see cref="HttpClient"/> used as a proxy.
	/// </summary>
	public class HttpClientProxy
	{
		/// <summary>
		/// The client used for HTTP calls.
		/// </summary>
		public HttpClient Client { get; }

		/// <summary>
		/// The user-friendly name of the client.
		/// </summary>
		public string Name { get; }

		public HttpClientProxy(HttpClient client, string name)
		{
			Client = client;
			Name = name;
		}
	}

	/// <summary>
	/// Provides <see cref="HttpClientProxy"/> objects for use in the scraper.
	/// </summary>
	public abstract class ProxyProvider
	{
		protected Action<HttpClientHandler> ConfigureClientHandlerAction { get; }
		protected virtual AsyncCollection<HttpClientProxy> ProxyClients { get; } = new AsyncCollection<HttpClientProxy>();

		public abstract int ProxyCount { get; }

		protected ProxyProvider(Action<HttpClientHandler> configureClientHandlerHandlerAction = null)
		{
			ConfigureClientHandlerAction = configureClientHandlerHandlerAction;
		}

		public abstract Task InitializeAsync();

		/// <summary>
		/// Rents a <see cref="HttpClientProxy"/> object, encapsulated in a <see cref="PoolObject{HttpClientProxy}"/> object.
		/// </summary>
		/// <returns></returns>
		public virtual async Task<PoolObject<HttpClientProxy>> RentHttpClient()
		{
			return new PoolObject<HttpClientProxy>(await ProxyClients.TakeAsync(), proxy => ProxyClients.AddAsync(proxy));
		}

		/// <summary>
		/// Creates a new <see cref="HttpClient"/> object with some default values, and with the <see cref="IWebProxy"/> object attached.
		/// </summary>
		/// <param name="proxy">The proxy to use for the <see cref="HttpClient"/>.</param>
		/// <returns>A new and configured <see cref="HttpClient"/> instance.</returns>
		public virtual HttpClient CreateNewClient(IWebProxy proxy)
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

			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; rv:68.0) Gecko/20100101 Firefox/68.0");
			httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
			httpClient.Timeout = TimeSpan.FromSeconds(10);

			return httpClient;
		}

		public HttpClient CreateNewClient()
			=> CreateNewClient((IWebProxy)null);

		/// <summary>
		/// Creates a new <see cref="HttpClient"/> object with some default values, and with the <see cref="IWebProxy"/> object attached.
		/// </summary>
		/// <param name="proxy">The proxy to use for the <see cref="HttpClient"/>.</param>
		/// <returns>A new and configured <see cref="HttpClient"/> instance.</returns>
		public virtual HttpClient CreateNewClient(HttpMessageHandler handler)
		{
			var httpClient = new HttpClient(handler, true);

			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; rv:68.0) Gecko/20100101 Firefox/68.0"); //Hayden/0.7.0
			httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");

			return httpClient;
		}
	}
}