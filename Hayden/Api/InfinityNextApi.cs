using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the InfinityNext API.
	/// </summary>
	public class InfinityNextApi : BaseApi<InfinityNextThread>
	{
		public string ImageboardWebsite { get; }

		public InfinityNextApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override async Task<ApiResponse<InfinityNextThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
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

		protected override Thread ConvertThread(InfinityNextThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.OriginalPost.Subject,
				IsArchived = thread.Locked,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost()).ToArray(),
				AdditionalMetadata = new JObject
				{
					["sticky"] = thread.Stickied
				}
			};
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

	public class InfinityNextThread
	{
		[JsonProperty("posts")]
		public List<InfinityNextPost> Posts { get; set; }

		[JsonIgnore]
		public InfinityNextPost OriginalPost => Posts[0];

		[JsonProperty("reply_count")]
		public uint? ReplyCount { get; set; }

		[JsonProperty("reply_file_count")]
		public uint? ReplyFileCount { get; set; }

		[JsonProperty("reply_last")]
		public ulong? ReplyLast { get; set; }

		[JsonProperty("bumped_last")]
		public ulong? BumpedLast { get; set; }

		[JsonProperty("stickied")]
		public bool Stickied { get; set; }

		[JsonProperty("stickied_at")]
		public ulong? StickiedAt { get; set; }

		[JsonProperty("bumplocked_at")]
		public ulong? BumpLockedAt { get; set; }

		[JsonProperty("locked_at")]
		public ulong? LockedAt { get; set; }

		[JsonProperty("cyclical_at")]
		public ulong? CyclicalAt { get; set; }

		[JsonProperty("locked")]
		public bool Locked { get; set; }

		[JsonProperty("global_bumped_last")]
		public DateTimeOffset GlobalBumpedLast { get; set; }

		[JsonProperty("featured_at")]
		public ulong? FeaturedAt { get; set; }
	}

	public class InfinityNextPost
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
		
		public Post ConvertToPost()
		{
			Media[] media = Array.Empty<Media>();

			if (Attachments != null)
			{
				media = Attachments.Select(attachment => new Media
				{
					FileUrl = attachment.FileUrl,
					ThumbnailUrl = attachment.ThumbnailUrl,
					Filename = Path.GetFileNameWithoutExtension(attachment.Filename),
					FileExtension = Path.GetExtension(attachment.Filename),
					ThumbnailExtension = Path.GetExtension(attachment.ThumbnailUrl),
					Index = (byte)attachment.Position, // is this zero based? 9chan is down right now so can't check
					FileSize = (uint)attachment.FileInfo.FileSize,
					IsDeleted = attachment.IsDeleted,
					IsSpoiler = attachment.IsSpoiler,
					Sha256Hash = Convert.FromBase64String(attachment.FileInfo.FileSha256Hash),
					OriginalObject = this,
					AdditionalMetadata = new JObject
					{
						["meta"] = attachment.FileInfo.Meta,
						["globalAttachmentId"] = attachment.GlobalAttachmentId,
						["globalFileId"] = attachment.GlobalFileId
					}
				}).ToArray();
			}

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt),
				Author = Author,
				Tripcode = Tripcode,
				Email = Email,
				ContentRendered = ContentHtml,
				ContentRaw = ContentRaw,
				ContentType = ContentType.InfinityNext,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = Common.SerializeObject(new
				{
					globalPostId = GlobalPostNumber,
					posterID = AuthorId,
					capcode = CapcodeId,
					flagId = FlagId
				})
			};
		}
	}

	public class InfinityNextAttachment
	{
		[JsonProperty("attachment_id")]
		public ulong GlobalAttachmentId { get; set; }

		[JsonProperty("file_id")]
		public ulong GlobalFileId { get; set; }

		[JsonProperty("filename")]
		public string Filename { get; set; }

		[JsonProperty("position")]
		public uint Position { get; set; }

		[JsonProperty("is_spoiler")]
		public bool IsSpoiler { get; set; }

		[JsonProperty("is_deleted")]
		public bool IsDeleted { get; set; }

		[JsonProperty("thumbnail_id")]
		public ulong ThumbnailFileId { get; set; }

		[JsonProperty("thumbnail_url")]
		public string ThumbnailUrl { get; set; }

		[JsonProperty("file_url")]
		public string FileUrl { get; set; }

		[JsonProperty("file")]
		public InfinityNextAttachmentExtendedInfo FileInfo { get; set; }

		[JsonProperty("thumbnail")]
		public InfinityNextAttachmentExtendedInfo ThumbnailInfo { get; set; }
	}

	public class InfinityNextAttachmentExtendedInfo
	{
		[JsonProperty("file_id")]
		public ulong GlobalFileId { get; set; }

		[JsonProperty("filesize")]
		public ulong FileSize { get; set; }

		[JsonProperty("mime")]
		public string MimeType { get; set; }

		[JsonProperty("meta")]
		public string Meta { get; set; }

		[JsonProperty("file_width")]
		public ushort? FileWidth { get; set; }

		[JsonProperty("file_height")]
		public ushort? FileHeight { get; set; }

		[JsonProperty("hash")]
		public string FileSha256Hash { get; set; }
	}
}