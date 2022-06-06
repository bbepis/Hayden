using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Timeout;

namespace Hayden.Api
{
	/// <summary>
	/// Static class containing Polly retry policies.
	/// </summary>
	public static class NetworkPolicies
	{
		private static readonly Random random = new Random();

		/// <summary>
		/// The HTTP request policy used for API calls to ensure that requests are reliably performed.
		/// </summary>
		public static AsyncPolicy<HttpResponseMessage> HttpApiPolicy { get; } =
			Policy<HttpResponseMessage>
				.Handle<HttpRequestException>()
				.OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests
									  || response.StatusCode == HttpStatusCode.RequestTimeout
									  || (int)response.StatusCode >= 500)
				
				.WaitAndRetryAsync(5,
					retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) // exponential back-off: 2, 4, 8 etc
									+ TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
				)
				.WrapAsync(
					Policy.TimeoutAsync(10, TimeoutStrategy.Optimistic, (context, span, failedTask) =>
					{
						Program.Log($"Timeout occurred: {context.OperationKey}", true);
						return Task.CompletedTask;
					})
				);

		/// <summary>
		/// Creates a generic retry policy, with exponential back-off and jitter.
		/// </summary>
		/// <typeparam name="T">The type of data to return.</typeparam>
		/// <param name="tries">The amount of (re)tries before the attempt is given up.</param>
		public static AsyncPolicy<T> GenericRetryPolicy<T>(int tries)
		{
			return Policy<T>
				   .Handle<Exception>()
				   .WaitAndRetryAsync(99999,
					   retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(retryAttempt, 5))) // exponential back-off: 2, 4, 8 etc
				                                      + TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
				   )
				   .WrapAsync(
					   Policy.TimeoutAsync(10, TimeoutStrategy.Optimistic, (context, span, failedTask) =>
					   {
						   Program.Log($"Timeout occurred: {context.OperationKey}", true);
						   return Task.CompletedTask;
					   })
				   );
		}
	}
}