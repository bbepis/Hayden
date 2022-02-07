using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
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
		protected virtual HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;

			return request;
		}

		private async Task<(bool, HttpResponseMessage, ResponseType)> MakeApiCallInternal(Uri uri, HttpClient client, DateTimeOffset? modifiedSince, CancellationToken cancellationToken)
		{
			int callCount = 0;
			var httpResponse = await NetworkPolicies.HttpApiPolicy.ExecuteAsync(_ =>
			{
				Program.Log($"HttpApiPolicy call ({callCount}): {uri.AbsoluteUri}", true);

				using var request = CreateRequest(uri, modifiedSince);

				return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}, new Context(uri.AbsoluteUri)).ConfigureAwait(false);

			if (httpResponse.StatusCode == HttpStatusCode.NotModified)
			{
				return (false, httpResponse, ResponseType.NotModified);
			}

			if (httpResponse.StatusCode == HttpStatusCode.NotFound)
			{
				return (false, httpResponse, ResponseType.NotFound);
			}

			if (!httpResponse.IsSuccessStatusCode)
				throw new WebException($"Received an error code: {httpResponse.StatusCode}");

			return (true, httpResponse, ResponseType.Ok);
		}

		protected virtual async Task<ApiResponse<T>> MakeJsonApiCall<T>(Uri uri, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var (success, message, responseType) = await MakeApiCallInternal(uri, client, modifiedSince, cancellationToken);

			if (!success)
			{
				message.Dispose();
				return new ApiResponse<T>(responseType, default);
			}

			await using var responseStream = await message.Content.ReadAsStreamAsync();
			using StreamReader streamReader = new StreamReader(responseStream);
			using JsonReader reader = new JsonTextReader(streamReader);

			var obj = (await JToken.LoadAsync(reader, cancellationToken)).ToObject<T>();

			message.Dispose();

			return new ApiResponse<T>(ResponseType.Ok, obj);
		}

		protected virtual async Task<ApiResponse<IHtmlDocument>> MakeHtmlCall(Uri uri, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var (success, message, responseType) = await MakeApiCallInternal(uri, client, modifiedSince, cancellationToken);

			if (!success)
			{
				message.Dispose();
				return new ApiResponse<IHtmlDocument>(responseType, null);
			}

			// maybe this can be a static property?
			var parser = new HtmlParser();

			await using var responseStream = await message.Content.ReadAsStreamAsync();
			
			var document = await parser.ParseDocumentAsync(responseStream);

			message.Dispose();

			return new ApiResponse<IHtmlDocument>(ResponseType.Ok, document);
		}

		public abstract bool SupportsArchive { get; }
		public abstract Task<ApiResponse<TThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
		public abstract Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
		public abstract Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
	}
}
