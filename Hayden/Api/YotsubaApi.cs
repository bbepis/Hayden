using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Models;
using Newtonsoft.Json;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public enum YotsubaResponseType
	{
		Ok,
		NotModified,
		NotFound
	}

	public static class YotsubaApi
	{
		public static HttpClient HttpClient { get; set; }

		static YotsubaApi()
		{
			HttpClient = new HttpClient(new HttpClientHandler
			{
				MaxConnectionsPerServer = 24,
				UseCookies = false,
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
			});
			
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Hayden/0.2.0");
			HttpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
		}

		private static HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;

			return request;
		}

		public static Task<(Thread Thread, YotsubaResponseType ResponseType)> GetThread(string board, ulong threadNumber, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			return MakeYotsubaApiCall<Thread>(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json"), modifiedSince, cancellationToken);
		}

		public static Task<(Page[] Pages, YotsubaResponseType ResponseType)> GetBoard(string board, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<Page[]>(new Uri($"https://a.4cdn.org/{board}/threads.json"), modifiedSince, cancellationToken);
		}

		public static Task<(ulong[] ThreadIds, YotsubaResponseType ResponseType)> GetArchive(string board, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<ulong[]>(new Uri($"https://a.4cdn.org/{board}/archive.json"), modifiedSince, cancellationToken);
		}

		private static readonly JsonSerializer jsonSerializer = JsonSerializer.Create();
		private static async Task<(T, YotsubaResponseType)> MakeYotsubaApiCall<T>(Uri uri, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var request = CreateRequest(uri, modifiedSince);

			var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if (response.StatusCode == HttpStatusCode.NotModified)
				return (default, YotsubaResponseType.NotModified);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return (default, YotsubaResponseType.NotFound);

			if (!response.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {response.StatusCode}");

			using (response)
			using (var responseStream = await response.Content.ReadAsStreamAsync())
			using (StreamReader streamReader = new StreamReader(responseStream))
			using (JsonReader reader = new JsonTextReader(streamReader))
			{
				var obj = jsonSerializer.Deserialize<T>(reader);

				return (obj, YotsubaResponseType.Ok);
			}
		}
	}
}