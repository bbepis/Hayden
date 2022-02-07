using System;
using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class InfinityNextThread : IThread<InfinityNextPost>
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

		[JsonIgnore]
		public bool Archived => false;

		[JsonIgnore]
		string IThread<InfinityNextPost>.Title => OriginalPost.Subject;

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class InfinityNextPost : IPost
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

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion

		[JsonIgnore]
		string IPost.Content => ContentRaw;

		[JsonIgnore]
		uint IPost.UnixTimestamp => (uint)CreatedAt;
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