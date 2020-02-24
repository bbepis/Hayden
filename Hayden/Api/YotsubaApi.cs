﻿using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Hayden.Proxy;
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
		private static HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;
			request.Headers.Add("Origin", "https://boards.4chan.org");

			return request;
		}

		public static Task<(Thread Thread, YotsubaResponseType ResponseType)> GetThread(string board, ulong threadNumber, HttpClientProxy client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<Thread>(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json"), client, modifiedSince, cancellationToken);
		}

		public static Task<(Page[] Pages, YotsubaResponseType ResponseType)> GetBoard(string board, HttpClientProxy client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<Page[]>(new Uri($"https://a.4cdn.org/{board}/threads.json"), client, modifiedSince, cancellationToken);
		}

		public static Task<(ulong[] ThreadIds, YotsubaResponseType ResponseType)> GetArchive(string board, HttpClientProxy client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeYotsubaApiCall<ulong[]>(new Uri($"https://a.4cdn.org/{board}/archive.json"), client, modifiedSince, cancellationToken);
		}

		private static async Task<HttpResponseMessage> DoCall(Uri uri, HttpClientProxy client, DateTimeOffset? modifiedSince, CancellationToken cancellationToken)
		{
			using (var request = CreateRequest(uri, modifiedSince))
			{
				return await client.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
		}

		private static readonly JsonSerializer jsonSerializer = JsonSerializer.Create();
		private static async Task<(T, YotsubaResponseType)> MakeYotsubaApiCall<T>(Uri uri, HttpClientProxy client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			HttpResponseMessage response;

			try
			{
				response = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(() => DoCall(uri, client, modifiedSince, cancellationToken));
			}
			catch
			{
				Program.Log($"Exception: Network error with proxy {client.Name}");
				throw;
			}

			using (response)
			{
				if (response.StatusCode == HttpStatusCode.NotModified)
					return (default, YotsubaResponseType.NotModified);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return (default, YotsubaResponseType.NotFound);

				if (!response.IsSuccessStatusCode)
					throw new WebException($"Received an error code: {response.StatusCode}");
				
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
}