using System;
using System.Net;
using System.Net.Http;
using Polly;

namespace Hayden.Api
{
	public static class NetworkPolicies
	{
		public static AsyncPolicy<HttpResponseMessage> HttpApiPolicy;

		private static readonly Random random = new Random();

		static NetworkPolicies()
		{
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

		public static AsyncPolicy<T> GenericRetryPolicy<T>(int tries)
		{
			return Policy<T>
				   .Handle<Exception>()
				   .WaitAndRetryAsync(5,
					   retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) // exponential back-off: 2, 4, 8 etc
									   + TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
				   );
		}
	}
}