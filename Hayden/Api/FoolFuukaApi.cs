using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null


namespace Hayden
{
	public class FoolFuukaApi : BaseApi<FoolFuukaThread>, ISearchableFrontendApi, IPaginatedFrontEndApi
	{
		public string ImageboardWebsite { get; }

		public FoolFuukaApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override async Task<ApiResponse<FoolFuukaThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
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

		protected override Thread ConvertThread(FoolFuukaThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.OriginalPost.Title,
				IsArchived = thread.Archived,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost()).ToArray(),
				AdditionalMetadata = new()
				{
					Sticky = thread.OriginalPost.Sticky.GetValueOrDefault(),
					Deleted = thread.OriginalPost.Deleted.GetValueOrDefault()
				}
			};
		}

		/// <inheritdoc />
		public override Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Not supported");
		}

		public async Task<ApiResponse<IAsyncEnumerable<PageThread>>> GetBoardPaginated(string board, HttpClient client, DateTimeOffset? modifiedSince = null,
			CancellationToken cancellationToken = default)
		{
			var collectedThreadIds = new HashSet<ulong>();

			int pageNumber = 1;

			var testResponse = await MakeJsonApiCall<JToken>(new Uri($"{ImageboardWebsite}_/api/chan/index/?board={board}&page={pageNumber}"), client, modifiedSince, cancellationToken);

			if (testResponse.ResponseType != ResponseType.Ok)
				return new ApiResponse<IAsyncEnumerable<PageThread>>(testResponse.ResponseType, null);

			async IAsyncEnumerable<PageThread> InternalGetEnumerable()
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					var response = await MakeJsonApiCall<JToken>(new Uri($"{ImageboardWebsite}_/api/chan/index/?board={board}&page={pageNumber}"), client, modifiedSince, cancellationToken);
					
					if (response.ResponseType != ResponseType.Ok)
						yield break;

					if (response.Data is JArray)
						break;

					var data = response.Data.ToObject<Dictionary<string, FoolFuukaIndexPageThread>>();

					if (data == null || data.Count == 0)
						break;

					foreach (var thread in data.Values)
					{
						if (!collectedThreadIds.Contains(thread.op.PostNumber))
						{
							yield return new PageThread(thread.op.PostNumber, 0, thread.op.Title, thread.op.SanitizedComment);
							collectedThreadIds.Add(thread.op.PostNumber);
						}
					}

					pageNumber++;
				}
			}

			return new ApiResponse<IAsyncEnumerable<PageThread>>(ResponseType.Ok, InternalGetEnumerable());
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Not supported");
		}

		public async Task<(ulong? total, IAsyncEnumerable<ThreadPointer> enumerable)> PerformSearch(SearchQuery query, HttpClient client, CancellationToken cancellationToken = default)
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
			
			async IAsyncEnumerable<ThreadPointer> InnerSearch(IEnumerable<FoolFuukaPostWithBoard> firstSearch)
			{
				// needs to be moved to config
				var blacklistedThreads = new ulong[] { };

				foreach (var post in firstSearch)
				{
					if (blacklistedThreads.Contains(post.PostNumber))
						continue;

					yield return new ThreadPointer(post.board.shortname, post.PostNumber);
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

						yield return new ThreadPointer(post.board.shortname, post.PostNumber);
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

		private class FoolFuukaIndexPageThread
		{
			public FoolFuukaPost op { get; set; }
			public FoolFuukaPost[] posts { get; set; }
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

	public class FoolFuukaThread
	{
		[JsonProperty("posts")]
		public List<FoolFuukaPost> Posts { get; set; }

		[JsonIgnore]
		public FoolFuukaPost OriginalPost => Posts[0];

		[JsonIgnore]
		public bool Archived => OriginalPost.Locked ?? false;
	}

	public class FoolFuukaPost
	{
		[JsonProperty("doc_id")]
		public ulong DocumentId { get; set; }

		[JsonProperty("num")]
		public ulong PostNumber { get; set; }

		[JsonProperty("subnum")]
		public ulong SubPostNumber { get; set; }

		[JsonProperty("thread_num")]
		public ulong ThreadPostNumber { get; set; }

		[JsonProperty("timestamp")]
		public uint UnixTimestamp { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("name")]
		public string Author { get; set; }

		[JsonProperty("trip")]
		public string Tripcode { get; set; }

		[JsonProperty("poster_hash")]
		public string PosterHash { get; set; }

		[JsonProperty("comment_sanitized")]
		public string SanitizedComment { get; set; }

		[JsonProperty("poster_country")]
		public string CountryCode { get; set; }

		[JsonProperty("poster_country_name")]
		public string CountryName { get; set; }

		[JsonProperty("troll_country_code")]
		public string TrollCountryCode { get; set; }

		[JsonProperty("troll_country_name")]
		public string TrollCountryName { get; set; }

		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("sticky")]
		public bool? Sticky { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("locked")]
		public bool? Locked { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("deleted")]
		public bool? Deleted { get; set; }

		[JsonProperty("media")]
		public FoolFuukaPostMedia Media { get; set; }

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion
		
		public Post ConvertToPost()
		{
			Media[] media = Array.Empty<Media>();

			if (Media != null)
				media = new[]
				{
					new Media
					{
						FileUrl = Media.FileUrl,
						ThumbnailUrl = Media.ThumbnailUrl,
						Filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(Media.OriginalFilename)),
						FileExtension = Path.GetExtension(Media.OriginalFilename),
						ThumbnailExtension = Path.GetExtension(Media.OriginalFilename),
						Index = 0,
						FileSize = Media.FileSize,
						IsDeleted = false, // asagi schema doesn't store this info
						IsSpoiler = Media.IsSpoiler,
						Md5Hash = Convert.FromBase64String(Media.Md5HashString),
						OriginalObject = Media,
						AdditionalMetadata = new()
						{
							YotsubaTimestamp = ulong.Parse(Path.GetFileNameWithoutExtension(Media.TimestampedFilename))
						}
					}
				};

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds(UnixTimestamp),
				Author = Author,
				Tripcode = Tripcode,
				Email = Email,
				IsDeleted = Deleted ?? false,
				ContentRendered = null,
				ContentRaw = SanitizedComment.TrimAndNullify(),
				ContentType = ContentType.Yotsuba,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = new()
				{
					Capcode = Capcode != null && Capcode != "N" ? Capcode : null,
					CountryCode = CountryCode.TrimAndNullify(),
					CountryName = CountryName?.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) != true ? CountryName.TrimAndNullify() : null,
					BoardFlagCode = TrollCountryCode.TrimAndNullify(),
					BoardFlagName = TrollCountryName.TrimAndNullify(),
					PosterID = PosterHash,
					AsagiExif = Media?.Exif
				}
			};
		}
	}

	public class FoolFuukaPostMedia
	{
		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("spoiler")]
		public bool IsSpoiler { get; set; }

		[JsonProperty("media")]
		public string TimestampedFilename { get; set; }

		[JsonProperty("preview_reply")]
		public string TimestampedThumbFilename { get; set; }

		[JsonProperty("media_filename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("media_hash")]
		public string Md5HashString { get; set; }

		[JsonProperty("media_link")]
		public string FileUrl { get; set; }

		[JsonProperty("thumb_link")]
		public string ThumbnailUrl { get; set; }

		[JsonProperty("media_size")]
		public uint FileSize { get; set; }

		[JsonProperty("exif")]
		public string Exif { get; set; }
	}
}