using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hayden.Proxy
{
	public class NullProxyProvider : ProxyProvider
	{
		public NullProxyProvider(Action<HttpClientHandler> configureClientHandlerAction = null) : base(configureClientHandlerAction)
		{
			// use only a direct connection
			ProxyClients.Add(new HttpClientProxy(CreateNewClient((IWebProxy)null), "baseconnection/none"));
		}

		public override int ProxyCount => 1;

		public override Task InitializeAsync(bool needsToTest)
		{
			return Task.CompletedTask;
		}
	}
}