using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class FoolFuukaThread : IThread<FoolFuukaPost>
	{
		[JsonProperty("posts")]
		public List<FoolFuukaPost> Posts { get; set; }

		[JsonIgnore]
		public FoolFuukaPost OriginalPost => Posts[0];

		[JsonIgnore]
		public bool Archived => OriginalPost.Locked ?? false;

		[JsonIgnore]
		string IThread<FoolFuukaPost>.Title => OriginalPost.Title;

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class FoolFuukaPost : IPost
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

		[JsonProperty("comment_sanitized")]
		public string SanitizedComment { get; set; }

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
		
		[JsonIgnore]
		string IPost.Content => SanitizedComment;
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
	}
}