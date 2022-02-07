using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Newtonsoft.Json;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the InfinityNext API.
	/// </summary>
	public class InfinityNextApi : BaseApi<InfinityNextThread>
	{
		public string ImageboardWebsite { get; }

		public InfinityNextApi(string imageboardWebsite)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		public override async Task<ApiResponse<InfinityNextThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var rawThreadResponse = await MakeJsonApiCall<InfinityNextRawThread>(new Uri($"{ImageboardWebsite}{board}/thread/{threadNumber}.json"), client, modifiedSince, cancellationToken);

			if (rawThreadResponse.ResponseType != ResponseType.Ok)
				return new ApiResponse<InfinityNextThread>(rawThreadResponse.ResponseType, null);

			var rawThread = rawThreadResponse.Data;
			
			var opPost = rawThread.MapToPost();

			if (rawThread.Posts == null)
				rawThread.Posts = new List<InfinityNextPost>();

			rawThread.Posts.Insert(0, opPost);

			rawThread.Posts = rawThread.Posts.OrderBy(x => x.PostNumber).ToList();

			var thread = rawThread.MapToThread();

			return new ApiResponse<InfinityNextThread>(ResponseType.Ok, thread);
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<InfinityNextCatalogItem[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			var response = new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data.Select(x => 
					new PageThread(x.board_id, x.bumped_last, x.subject, x.content_raw))
				.ToArray());

			return response;
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}

		private class InfinityNextCatalogItem
		{
			public ulong bumped_last;
			public string content_raw;
			public ulong board_id;
			public string subject;
		}

		private class InfinityNextRawThread : InfinityNextThread
		{
			[JsonProperty("board_id")]
			public ulong PostNumber { get; set; }

			[JsonProperty("post_id")]
			public ulong GlobalPostNumber { get; set; }

			[JsonProperty("reply_to_board_id")]
			public ulong? ReplyPostNumber { get; set; }

			[JsonProperty("reply_to")]
			public ulong? ReplyGlobalPostNumber { get; set; }

			[JsonProperty("created_at")]
			public ulong CreatedAt { get; set; }

			[JsonProperty("updated_at")]
			public ulong? UpdatedAt { get; set; }

			[JsonProperty("updated_by")]
			public ulong? UpdatedBy { get; set; }

			[JsonProperty("deleted_at")]
			public ulong? DeletedAt { get; set; }

			[JsonProperty("author_id")]
			public string AuthorId { get; set; }

			[JsonProperty("author_country")]
			public string AuthorCountry { get; set; }

			[JsonProperty("author_ip_nulled_at")]
			public ulong? AuthorIpNulledAt { get; set; }

			[JsonProperty("author")]
			public string Author { get; set; }

			[JsonProperty("tripcode")]
			public string Tripcode { get; set; }

			[JsonProperty("capcode_id")]
			public string CapcodeId { get; set; }

			[JsonProperty("subject")]
			public string Subject { get; set; }

			[JsonProperty("email")]
			public string Email { get; set; }

			[JsonProperty("adventure_id")]
			public string AdventureId { get; set; }

			[JsonProperty("body_too_long")]
			public bool BodyTooLong { get; set; }

			[JsonProperty("flag_id")]
			public string FlagId { get; set; }

			[JsonProperty("body_has_content")]
			public bool BodyHasContent { get; set; }

			[JsonProperty("body_rtl")]
			public bool BodyRightToLeft { get; set; }

			[JsonProperty("body_signed")]
			public string BodySigned { get; set; }

			[JsonProperty("content_html")]
			public string ContentHtml { get; set; }

			[JsonProperty("content_raw")]
			public string ContentRaw { get; set; }

			[JsonProperty("global_bumped_last")]
			public DateTimeOffset GlobalBumpedLast { get; set; }

			[JsonProperty("attachments")]
			public List<InfinityNextAttachment> Attachments { get; set; }
			
			public InfinityNextPost MapToPost()
			{
				return new InfinityNextPost
				{
					PostNumber = PostNumber,
					GlobalPostNumber = GlobalPostNumber,
					ReplyPostNumber = ReplyPostNumber,
					ReplyGlobalPostNumber = ReplyGlobalPostNumber,
					CreatedAt = CreatedAt,
					UpdatedAt = UpdatedAt,
					UpdatedBy = UpdatedBy,
					DeletedAt = DeletedAt,
					AuthorId = AuthorId,
					AuthorCountry = AuthorCountry,
					AuthorIpNulledAt = AuthorIpNulledAt,
					Author = Author,
					Tripcode = Tripcode,
					CapcodeId = CapcodeId,
					Subject = Subject,
					Email = Email,
					AdventureId = AdventureId,
					BodyTooLong = BodyTooLong,
					FlagId = FlagId,
					BodyHasContent = BodyHasContent,
					BodyRightToLeft = BodyRightToLeft,
					BodySigned = BodySigned,
					ContentHtml = ContentHtml,
					ContentRaw = ContentRaw,
					GlobalBumpedLast = GlobalBumpedLast,
					Attachments = Attachments
				};
			}

			public InfinityNextThread MapToThread()
			{
				return new InfinityNextThread
				{
					Posts = Posts,
					ReplyCount = ReplyCount,
					ReplyFileCount = ReplyFileCount,
					ReplyLast = ReplyLast,
					BumpedLast = BumpedLast,
					Stickied = Stickied,
					StickiedAt = StickiedAt,
					BumpLockedAt = BumpLockedAt,
					LockedAt = LockedAt,
					CyclicalAt = CyclicalAt,
					Locked = Locked,
					GlobalBumpedLast = GlobalBumpedLast,
					FeaturedAt = FeaturedAt
				};
			}
		}
	}
}