using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using NodaTime;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the 4chan API.
	/// </summary>
	public class YotsubaApi : BaseApi<YotsubaThread>
	{
		/// <inheritdoc />
		protected override HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;

			// 4chan API requires this as part of CORS
			request.Headers.Add("Origin", "https://boards.4chan.org");

			return request;
		}

		/// <inheritdoc />
		public override bool SupportsArchive => true;

		/// <inheritdoc />
		public override Task<ApiResponse<YotsubaThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var timestampNow = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

			return MakeJsonApiCall<YotsubaThread>(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json?t={timestampNow}"), client, modifiedSince, cancellationToken);
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var timestampNow = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

			var result = await MakeJsonApiCall<Page[]>(new Uri($"https://a.4cdn.org/{board}/catalog.json?t={timestampNow}"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			return new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data.SelectMany(x => x.Threads).ToArray());
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var timestampNow = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

			return MakeJsonApiCall<ulong[]>(new Uri($"https://a.4cdn.org/{board}/archive.json?t={timestampNow}"), client, modifiedSince, cancellationToken);
		}
	}
}