using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hayden.Api;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null


namespace Hayden
{
	public class FoolFuukaApi : BaseApi<FoolFuukaThread>, ISearchableFrontendApi<FoolFuukaThread>
	{
		public string ImageboardWebsite { get; }

		public FoolFuukaApi(string imageboardWebsite)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		public override async Task<ApiResponse<FoolFuukaThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var rawThreadResponse = await MakeJsonApiCall<Dictionary<ulong, FoolFuukaRawThread>>(new Uri($"{ImageboardWebsite}_/api/chan/thread/?board={board}&num={threadNumber}"), client, modifiedSince, cancellationToken);
			
			if (rawThreadResponse.ResponseType != ResponseType.Ok)
				return new ApiResponse<FoolFuukaThread>(rawThreadResponse.ResponseType, null);

			var thread = new FoolFuukaThread();
			thread.Posts = new List<FoolFuukaPost>();

			var post = rawThreadResponse.Data.Values.First();
			
			thread.Posts.Add(post.op);

			if (post.posts != null)
				thread.Posts.AddRange(post.posts.Values.Where(x => x.SubPostNumber == 0).OrderBy(x => x.PostNumber));

			return new ApiResponse<FoolFuukaThread>(ResponseType.Ok, thread);
		}

		/// <inheritdoc />
		public override Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Not supported");
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Not supported");
		}

		public async Task<(ulong? total, IAsyncEnumerable<(ulong threadId, string board)> enumerable)> PerformSearch(SearchQuery query, HttpClient client, CancellationToken cancellationToken = default)
		{
			var uriBuilder = new UriBuilder($"{ImageboardWebsite}_/api/chan/search/");

			var stringParams = HttpUtility.ParseQueryString("");

			if (query.Board != null)
				stringParams.Add("boards", query.Board);

			if (query.TextQuery != null)
				stringParams.Add("text", query.TextQuery);

			stringParams.Add("type", "op");
			stringParams.Add("order", "asc");

			uriBuilder.Query = stringParams.ToString();

			var rawSearchResponse = await MakeJsonApiCall<FoolFuukaRawSearch>(uriBuilder.Uri, client, null, cancellationToken);

			if (rawSearchResponse.ResponseType != ResponseType.Ok || rawSearchResponse.Data.obj.posts.Length == 0)
				throw new Exception("Failed to perform search");
			
			async IAsyncEnumerable<(ulong threadId, string board)> InnerSearch(IEnumerable<FoolFuukaPostWithBoard> firstSearch)
			{
				// needs to be moved to config
				var blacklistedThreads = new ulong[] { };

				foreach (var post in firstSearch)
				{
					if (blacklistedThreads.Contains(post.PostNumber))
						continue;

					yield return (post.PostNumber, post.board.shortname);
				}

				int page = 2;

				while (true)
				{
					stringParams["page"] = (page++).ToString();
					uriBuilder.Query = stringParams.ToString();

					var rawSearchResponse = await MakeJsonApiCall<FoolFuukaRawSearch>(uriBuilder.Uri, client, null, cancellationToken);

					if (rawSearchResponse.ResponseType != ResponseType.Ok || (rawSearchResponse.Data.obj?.posts?.Length ?? 0) == 0)
						yield break;

					
					foreach (var post in rawSearchResponse.Data.obj.posts)
					{
						if (blacklistedThreads.Contains(post.PostNumber))
							continue;

						yield return (post.PostNumber, post.board.shortname);
					}
				}
			}

			return (rawSearchResponse.Data.meta?.total_found, InnerSearch(rawSearchResponse.Data.obj.posts));
		}

		private class FoolFuukaRawThread
		{
			public FoolFuukaPost op { get; set; }
			public Dictionary<string, FoolFuukaPost> posts { get; set; }
		}

		private class FoolFuukaRawSearch
		{
			[JsonProperty("0")]
			public FoolFuukaRawSearchSubObject obj { get; set; }

			public FoolFuukaRawSearchMetaObj meta { get; set; }

			public class FoolFuukaRawSearchSubObject
			{
				public FoolFuukaPostWithBoard[] posts { get; set; }
			}

			public class FoolFuukaRawSearchMetaObj
		{
				public ulong? total_found { get; set; }
			}
		}

		private class FoolFuukaPostWithBoard : FoolFuukaPost
		{
			public FoolFuukaBoardObj board;

			public class FoolFuukaBoardObj
			{
				public string shortname { get; set; }
			}
		}
	}
}