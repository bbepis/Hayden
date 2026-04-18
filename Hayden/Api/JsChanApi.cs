using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Thread = Hayden.Models.Thread;

namespace Hayden;

/// <summary>
/// Class that handles requests to the 4chan API.
/// </summary>
public class JsChanApi : BaseApi<JsChanThread>
{
	public string ImageboardWebsite { get; }

	public JsChanApi(SourceConfig sourceConfig) : base(sourceConfig)
	{
		ImageboardWebsite = sourceConfig.ImageboardWebsite;

		if (!ImageboardWebsite.EndsWith("/"))
			ImageboardWebsite += "/";
	}

	public override async Task<ApiCapabilities> DetermineCapabilitiesAsync(HttpClient client)
	{
		var boardListApi = await MakeJsonApiCall<JsChanBoardList>(new Uri($"{ImageboardWebsite}boards.json"), client);

		string[] boardList = null;

		if (boardListApi.ResponseType == ResponseType.Ok)
		{
			boardList = boardListApi.Data.Boards
				.Where(x => !x.Webring)
				.Select(x => x.Id)
				.ToArray();
		}

		return new ApiCapabilities()
		{
			SupportsArchive = false,
			SupportsBoardLastModified = true,
			SupportsBoardListing = true,
			BoardList = boardList
		};
	}

	/// <inheritdoc />
	protected override async Task<ApiResponse<JsChanThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
	{
		var response = await MakeJsonApiCall<JsChanThread[]>(new Uri($"{ImageboardWebsite}api/threads/{board}/{threadNumber}/full.json?_t={872601010}"), client, modifiedSince, cancellationToken);

		if (response.ResponseType != ResponseType.Ok)
			return new ApiResponse<JsChanThread>(response.ResponseType, null);

		return new ApiResponse<JsChanThread>(ResponseType.Ok, response.Data[0]);
	}

	protected override Thread ConvertThread(JsChanThread thread, string board)
	{
		var op = thread.ConvertToPost(board, ImageboardWebsite);

		return new Thread
		{
			ThreadId = op.PostNumber,
			Title = thread.Subject,
			ArchivedTime = thread.Archived ? DateTimeOffset.MinValue : null,
			OriginalObject = thread,
			Posts = [op, ..(thread.Posts?.Select(x => x.ConvertToPost(board, ImageboardWebsite)) ?? [])],
			AdditionalMetadata = new()
			{
				Sticky = thread.Sticky.GetValueOrDefault()
			}
		};
	}

	/// <inheritdoc />
	public override async Task<ApiResponse<ThreadOverviewInfo[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
	{
		var result = await MakeJsonApiCall<JsChanCatalogListing[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

		if (result.ResponseType != ResponseType.Ok)
			return new ApiResponse<ThreadOverviewInfo[]>(result.ResponseType, null);

		var info = result.Data
			.Select((x, i) => new ThreadOverviewInfo
			{
				ThreadId = x.ThreadId,
				ContentHtml = x.ContentRaw,
				Subject = x.Subject,
				LastModified = x.BumpTime,
				Position = i,
				ReplyCount = x.ReplyCount
			})
			.ToArray();

		return new ApiResponse<ThreadOverviewInfo[]>(ResponseType.Ok, info);
	}

	/// <inheritdoc />
	public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
	{
		throw new InvalidOperationException("Does not support archives");
	}

	private class JsChanBoardList
	{
		[JsonProperty("boards")]
		public JsChanBoardListing[] Boards { get; set; }
	}

	private class JsChanBoardListing
	{
		[JsonProperty("_id")]
		public string Id { get; set; }

		[JsonProperty("webring")]
		public bool Webring { get; set; }
	}

	private class JsChanCatalogListing
	{
		[JsonProperty("postId")]
		public ulong ThreadId { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }

		[JsonProperty("nomarkup")]
		public string ContentRaw { get; set; }

		[JsonProperty("bumped")]
		public DateTimeOffset BumpTime { get; set; }

		[JsonProperty("replyposts")]
		public int ReplyCount { get; set; }
	}
}

public class JsChanThread : JsChanPost
{
	[JsonProperty("last_replies")]
	public List<KissuPost> Posts { get; set; }

	[JsonIgnore]
	public KissuPost OriginalPost => Posts[0];

	[JsonProperty]
	public bool Archived { get; set; }
}

public class JsChanEmbed
{
	[JsonProperty("site")]
	public string Site { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("inputURL")]
	public string InputURL { get; set; }
}

public class JsChanPost
{
	[JsonProperty("_id")]
	public ulong UniqueId { get; set; }

	[JsonProperty("date")]
	public DateTimeOffset TimePosted { get; set; }

	[JsonProperty("name")]
	public string Author { get; set; }

	[JsonProperty("country")]
	public PostCountry Country { get; set; }

	[JsonProperty("tripcode")]
	public string Tripcode { get; set; }

	[JsonProperty("capcode")]
	public string Capcode { get; set; }

	[JsonProperty("subject")]
	public string Subject { get; set; }

	[JsonProperty("message")]
	public string ContentHtml { get; set; }

	[JsonProperty("nomarkup")]
	public string ContentRaw { get; set; }

	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("banmessage")]
	public string BanMessage { get; set; }

	[JsonProperty("userId")]
	public string UserId { get; set; }

	// TODO: edited info

	[JsonProperty("files")]
	public PostFile[] Files { get; set; }

	public Post ConvertToPost(string board, string imageboardUrlRoot)
	{
		Media[] media = Array.Empty<Media>();

		if (Files != null && Files.Length > 0)
		{
			media = Files.Select((x, i) => new Media
			{
				FileUrl = $"{imageboardUrlRoot}{board}/file/{x.ServerFilename}",
				ThumbnailUrl = $"{imageboardUrlRoot}{board}/file/thumb/{x.Sha256HashHex}{x.ThumbnailExtension}",
				TimestampedFilename = x.Sha256HashHex,
				Filename = Path.GetFileNameWithoutExtension(x.OriginalFilename),
				FileExtension = x.Extension.TrimStart('.'),
				ThumbnailExtension = x.ThumbnailExtension.TrimStart('.'),
				Index = (byte)i,
				FileSize = (uint)x.Size,
				IsDeleted = false,
				IsSpoiler = x.Spoiler,
				ImageHeight = x.FileGeometry.Height,
				ImageWidth = x.FileGeometry.Width,
				OriginalObject = this,
				AdditionalMetadata = null
			})

			var mediaList = new List<Media>
			{
				
			};

			media = mediaList.ToArray();
		}
		else if (Embed != null)
		{
			media = new[]
			{
				new Media
				{
					Index = 0,
					AdditionalMetadata = new()
					{
						ExternalMediaUrl = Embed.InputURL
					}
				}
			};
		}

		return new Post
		{
			PostNumber = PostNumber,
			TimePosted = DateTimeOffset.FromUnixTimeSeconds(UnixTimestamp),
			Author = Name,
			Tripcode = Trip,
			Email = null,
			ContentRaw = Comment,
			ContentType = ContentType.Vichan,
			Media = media,
			OriginalObject = this,
			AdditionalMetadata = new()
			{
				Capcode = Capcode,
				CountryCode = CountryCode,
				CountryName = CountryName
			}
		};
	}

	public class PostCountry
	{
		[JsonProperty("code")]
		public string CountryCode { get; set; }

		[JsonProperty("name")]
		public string CountryName { get; set; }
	}

	public class PostFile
	{
		[JsonProperty("spoiler")]
		public bool Spoiler { get; set; }

		[JsonProperty("hash")]
		public string Sha256HashHex { get; set; }

		[JsonProperty("filename")]
		public string ServerFilename { get; set; }

		[JsonProperty("originalFilename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("extension")]
		public string Extension { get; set; }

		[JsonProperty("size")]
		public ulong Size { get; set; }

		[JsonProperty("thumbextension")]
		public string ThumbnailExtension { get; set; }

		[JsonProperty("hasThumb")]
		public bool HasThumbnail { get; set; }

		[JsonProperty("geometry")]
		public Geometry FileGeometry { get; set; }

		public class Geometry
		{
			[JsonProperty("width")]
			public uint Width { get; set; }

			[JsonProperty("height")]
			public uint Height { get; set; }
		}
	}
}