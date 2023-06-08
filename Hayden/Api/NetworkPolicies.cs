using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Timeout;
using Serilog;

namespace Hayden.Api
{
	/// <summary>
	/// Static class containing Polly retry policies.
	/// </summary>
	public static class NetworkPolicies
	{
		private static readonly Random random = new Random();

		public static ILogger Logger { get; } = Program.CreateLogger("Network");

		/// <summary>
		/// The HTTP request policy used for API calls to ensure that requests are reliably performed.
		/// </summary>
		public static AsyncPolicy<HttpResponseMessage> HttpApiPolicy { get; } =
			Policy<HttpResponseMessage>
				.Handle<HttpRequestException>()
				.Or<TimeoutRejectedException>()
				.OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests
									  || response.StatusCode == HttpStatusCode.RequestTimeout
									  || (int)response.StatusCode >= 500)
				
				.WaitAndRetryAsync(20,
					retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(retryAttempt, 4))) // exponential back-off: 2, 4, 8 etc
									+ TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
					, (result, span) =>
					{
						if (result.Exception != null)
							Logger.Debug(result.Exception, "Network response failed (exception)");
						else
							Logger.Debug("Network response failed (code): {statusCode}", result.Result.StatusCode);
					}
				)
				.WrapAsync(
					Policy.TimeoutAsync(10, TimeoutStrategy.Pessimistic, (context, span, failedTask) =>
					{
						Logger.Debug("Timeout occurred: {contextOperationKey}", context.OperationKey);
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
				   .Or<TimeoutRejectedException>()
				   .WaitAndRetryAsync(tries,
					   retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(retryAttempt, 5))) // exponential back-off: 2, 4, 8 etc
				                                      + TimeSpan.FromMilliseconds(random.Next(0, 5000)) // plus some jitter: up to 5 seconds
					   , (result, span) => Logger.Debug(result.Exception, "Network response failed (exception)")
				   )
				   .WrapAsync(
					   Policy.TimeoutAsync(10, TimeoutStrategy.Pessimistic, (context, span, failedTask) =>
					   {
						   Logger.Debug(failedTask.Exception, "Timeout occurred: {contextOperationKey}", context.OperationKey);
						   return Task.CompletedTask;
					   })
				   );
		}
	}
}