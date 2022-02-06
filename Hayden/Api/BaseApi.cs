using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;

namespace Hayden.Api
{
	public abstract class BaseApi<TThread> : IFrontendApi<TThread>
	{
		/// <summary>
		/// Creates the base HTTP request used by the frontend's API calls.
		/// </summary>
		/// <param name="uri">The uri of the request.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header.</param>
		/// <returns></returns>
		protected abstract HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince);

		protected virtual async Task<ApiResponse<T>> MakeJsonApiCall<T>(Uri uri, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			int callCount = 0;
			using var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(_ =>
			{
				Program.Log($"HttpApiPolicy call ({callCount}): {uri.AbsoluteUri}", true);

				using var request = CreateRequest(uri, modifiedSince);

				return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}, new Context(uri.AbsoluteUri)).ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.NotModified)
				return new ApiResponse<T>(ResponseType.NotModified, default);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return new ApiResponse<T>(ResponseType.NotFound, default);

			if (!response.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {response.StatusCode}");

			await using var responseStream = await response.Content.ReadAsStreamAsync();
			using StreamReader streamReader = new StreamReader(responseStream);
			using JsonReader reader = new JsonTextReader(streamReader);

			var obj = (await JToken.LoadAsync(reader, cancellationToken)).ToObject<T>();

			return new ApiResponse<T>(ResponseType.Ok, obj);
		}

		public abstract bool SupportsArchive { get; }
		public abstract Task<ApiResponse<TThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
		public abstract Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
		public abstract Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
	}
}
