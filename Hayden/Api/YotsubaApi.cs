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

	public class YotsubaResponse<T>
	{
		public YotsubaResponseType ResponseType { get; }

		public T Payload { get; }

		public YotsubaResponse(YotsubaResponseType responseType, T payload)
		{
			ResponseType = responseType;
			Payload = payload;
		}
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
			
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Hayden/0.0.0");
			HttpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
		}

		private static HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;

			return request;
		}

		public static async Task<YotsubaResponse<Thread>> GetThread(string board, ulong threadNumber, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var request = CreateRequest(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json"), modifiedSince);

			var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if (response.StatusCode == HttpStatusCode.NotModified)
				return new YotsubaResponse<Thread>(YotsubaResponseType.NotModified, null);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return new YotsubaResponse<Thread>(YotsubaResponseType.NotFound, null);

			if (!response.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {response.StatusCode}");

			using (response)
			using (var responseStream = await response.Content.ReadAsStreamAsync())
			using (StreamReader streamReader = new StreamReader(responseStream))
			using (JsonReader reader = new JsonTextReader(streamReader))
			{
				var serializer = JsonSerializer.Create();

				var posts = serializer.Deserialize<Thread>(reader);

				return new YotsubaResponse<Thread>(YotsubaResponseType.Ok, posts);
			}
		}

		public static async Task<YotsubaResponse<Page[]>> GetBoard(string board, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var request = CreateRequest(new Uri($"https://a.4cdn.org/{board}/threads.json"), modifiedSince);

			var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if (response.StatusCode == HttpStatusCode.NotModified)
				return new YotsubaResponse<Page[]>(YotsubaResponseType.NotModified, null);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return new YotsubaResponse<Page[]>(YotsubaResponseType.NotFound, null);

			if (!response.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {response.StatusCode}");

			using (response)
			using (var responseStream = await response.Content.ReadAsStreamAsync())
			using (StreamReader streamReader = new StreamReader(responseStream))
			using (JsonReader reader = new JsonTextReader(streamReader))
			{
				var serializer = JsonSerializer.Create();

				var pages = serializer.Deserialize<Page[]>(reader);

				return new YotsubaResponse<Page[]>(YotsubaResponseType.Ok, pages);
			}
		}
	}
}