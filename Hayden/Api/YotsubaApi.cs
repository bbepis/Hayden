using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hayden.Api;
using Hayden.Consumers;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;
using Thread = Hayden.Models.Thread;

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
		protected override Task<ApiResponse<YotsubaThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var timestampNow = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

			return MakeJsonApiCall<YotsubaThread>(new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json?t={timestampNow}"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(YotsubaThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = HttpUtility.HtmlDecode(thread.OriginalPost.Subject)?.TrimAndNullify(),
				IsArchived = thread.Archived,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(board)).ToArray(),
				AdditionalMetadata = new JObject
				{
					["sticky"] = thread.OriginalPost.Sticky
				}
			};
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

	public class YotsubaThread
	{
		[JsonProperty("posts")]
		public List<YotsubaPost> Posts { get; set; }

		[JsonIgnore]
		public YotsubaPost OriginalPost => Posts[0];

		[JsonIgnore]
		public bool Archived => OriginalPost.Archived ?? false;
	}

	public class YotsubaPost
	{
		// I comment out properties that are part of the API spec, but not used by Hayden.
		// I don't leave them in anyway, since we get a performance benefit by not having to deserialize them and keep them loaded in memory.

		[JsonProperty("no")]
		public ulong PostNumber { get; set; }

		[JsonProperty("resto")]
		public ulong ReplyPostNumber { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("sticky")]
		public bool? Sticky { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("closed")]
		public bool? Closed { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("archived")]
		public bool? Archived { get; set; }

		[JsonProperty("time")]
		public uint UnixTimestamp { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("trip")]
		public string Trip { get; set; }

		[JsonProperty("id")]
		public string PosterID { get; set; }

		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonProperty("country")]
		public string CountryCode { get; set; }

		[JsonProperty("sub")]
		public string Subject { get; set; }

		[JsonProperty("com")]
		public string Comment { get; set; }

		[JsonProperty("tim")]
		public ulong? TimestampedFilename { get; set; }

		[JsonProperty("filename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("ext")]
		public string FileExtension { get; set; }

		[JsonProperty("fsize")]
		public uint? FileSize { get; set; }

		[JsonProperty("md5")]
		public string FileMd5 { get; set; }

		[JsonProperty("w")]
		public ushort? ImageWidth { get; set; }

		[JsonProperty("h")]
		public ushort? ImageHeight { get; set; }

		[JsonProperty("tn_w")]
		public ushort? ThumbnailWidth { get; set; }

		[JsonProperty("tn_h")]
		public ushort? ThumbnailHeight { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("filedeleted")]
		public bool? FileDeleted { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("spoiler")]
		public bool? SpoilerImage { get; set; }

		[JsonProperty("since4pass")]
		public ushort? Since4Pass { get; set; }

		[JsonProperty("unique_ips")]
		public int? UniqueIps { get; set; }

		[JsonProperty("board_flag")]
		public string BoardFlagCode { get; set; }

		[JsonProperty("flag_name")]
		public string BoardFlagName { get; set; }

		#region Unused properties

		[JsonProperty("archived_on")]
		public uint? ArchivedOn { get; set; }

		[JsonConverter(typeof(YotsubaDateConverter))]
		[JsonProperty("now")]
		public DateTime? PostTime { get; set; }

		[JsonProperty("country_name")]
		public string CountryName { get; set; }

		[JsonProperty("custom_spoiler")]
		public byte? CustomSpoiler { get; set; }

		[JsonProperty("replies")]
		public uint? TotalReplies { get; set; }

		[JsonProperty("images")]
		public ushort? TotalImages { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("bumplimit")]
		public bool? BumpLimit { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("imagelimit")]
		public bool? ImageLimit { get; set; }

		[JsonProperty("tag")]
		public string Tag { get; set; }

		[JsonProperty("semantic_url")]
		public string SemanticUrl { get; set; }

		#endregion

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion

		[JsonIgnore]
		public string OriginalFilenameFull => FileMd5 != null ? OriginalFilename + FileExtension : null;

		[JsonIgnore]
		public string TimestampedFilenameFull => FileMd5 != null ? TimestampedFilename + FileExtension : null;
		
		public Post ConvertToPost(string board)
		{
			Media[] media = Array.Empty<Media>();

			if (FileMd5 != null)
				media = new[]
				{
					new Media
					{
						FileUrl = $"https://i.4cdn.org/{board}/{TimestampedFilenameFull}",
						ThumbnailUrl = $"https://i.4cdn.org/{board}/{TimestampedFilename}s.jpg",
						Filename = HttpUtility.HtmlDecode(OriginalFilename)?.Trim(),
						FileExtension = FileExtension,
						ThumbnailExtension = "jpg",
						Index = 0,
						FileSize = FileSize,
						IsDeleted = FileDeleted ?? false,
						IsSpoiler = SpoilerImage ?? false,
						Md5Hash = Convert.FromBase64String(FileMd5),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};
			
			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds(UnixTimestamp),
				Author = HttpUtility.HtmlDecode(Name)?.TrimAndNullify(),
				Tripcode = Trip,
				Email = null,
				//ContentRendered = Comment,
				ContentRaw = AsagiThreadConsumer.CleanComment(Comment).TrimAndNullify(),
				ContentType = ContentType.Yotsuba,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = Common.SerializeObject(new
				{
					capcode = Capcode,
					countryCode = CountryCode,
					countryName = CountryName,
					boardFlagCode = BoardFlagCode,
					boardFlagName = BoardFlagName,
					posterID = PosterID,
					customSpoiler = CustomSpoiler
				})
			};
		}
	}
}