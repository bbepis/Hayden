using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Newtonsoft.Json;
using Polly;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the 4chan API.
	/// </summary>
	public static class YotsubaApi
	{
		/// <summary>
		/// Creates the base HTTP request used by 4chan API calls.
		/// </summary>
		/// <param name="uri">The uri of the request.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header.</param>
		/// <returns></returns>
		private static HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;

			// 4chan API requires this as part of CORS
			request.Headers.Add("Origin", "https://boards.4chan.org");

			return request;
		}

		/// <summary>
		/// Retrieves a thread and its posts from the 4chan API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		public static Task<ApiResponse<Thread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<Thread>(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json"), client, modifiedSince, cancellationToken);
		}

		/// <summary>
		/// Retrieves a list of a board's threads from the 4chan API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		public static Task<ApiResponse<Page[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<Page[]>(new Uri($"https://a.4cdn.org/{board}/threads.json"), client, modifiedSince, cancellationToken);
		}

		/// <summary>
		/// Retrieves a list of a board's archive's threads from the 4chan API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		public static Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<ulong[]>(new Uri($"https://a.4cdn.org/{board}/archive.json"), client, modifiedSince, cancellationToken);
		}

		private static async Task<HttpResponseMessage> DoCall(Uri uri, HttpClient client, DateTimeOffset? modifiedSince, CancellationToken cancellationToken)
		{
			using var request = CreateRequest(uri, modifiedSince);

			return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}

		// For performance, we keep a single static instance of a JsonSerializer object instead of recreating it multiple times.
		private static readonly JsonSerializer jsonSerializer = JsonSerializer.Create();

		private static async Task<ApiResponse<T>> MakeYotsubaApiCall<T>(Uri uri, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			int callCount = 0;
			using var response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync((context) => 
			{
				Program.Log($"HttpApiPolicy call ({callCount}): {uri.AbsoluteUri}", true);
				return DoCall(uri, client, modifiedSince, cancellationToken);
			}, new Context(uri.AbsoluteUri));

			if (response.StatusCode == HttpStatusCode.NotModified)
				return new ApiResponse<T>(ResponseType.NotModified, default);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return new ApiResponse<T>(ResponseType.NotFound, default);

			if (!response.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {response.StatusCode}");

			await using var responseStream = await response.Content.ReadAsStreamAsync();
			using StreamReader streamReader = new StreamReader(responseStream);
			using JsonReader reader = new JsonTextReader(streamReader);

			var obj = jsonSerializer.Deserialize<T>(reader);

			return new ApiResponse<T>(ResponseType.Ok, obj);
		}
	}
}