using System;
using System.Net;
using System.Net.Http;
using Polly;

namespace Hayden.Api
{
	public static class NetworkPolicies
	{
		public static AsyncPolicy<HttpResponseMessage> HttpApiPolicy;

		static NetworkPolicies()
		{
			var random = new Random();

			HttpApiPolicy = Policy<HttpResponseMessage>
							.Handle<HttpRequestException>()
							.OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests
												  || response.StatusCode == HttpStatusCode.RequestTimeout
												  || (int)response.StatusCode >= 500)
							.WaitAndRetryAsync(5,
								retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) // exponential back-off: 2, 4, 8 etc
												+ TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
							);
		}
	}
}