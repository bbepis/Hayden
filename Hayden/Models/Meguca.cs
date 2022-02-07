using System;
using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class MegucaThread : IThread<MegucaPost>
	{
		[JsonProperty("posts")]
		public List<MegucaPost> Posts { get; set; }

		[JsonIgnore]
		public MegucaPost OriginalPost => Posts[0];


		[JsonProperty("abbrev")]
		public bool Abbreviated { get; set; }

		[JsonProperty("sticky")]
		public bool Sticky { get; set; }

		[JsonProperty("locked")]
		public bool Locked { get; set; }

		[JsonProperty("post_count")]
		public ulong PostCount { get; set; }

		[JsonProperty("image_count")]
		public ulong ImageCount { get; set; }

		[JsonProperty("update_time")]
		public ulong UpdateTime { get; set; }

		[JsonProperty("bump_time")]
		public ulong BumpTime { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }


		[JsonIgnore]
		public bool Archived => false;

		[JsonIgnore]
		string IThread<MegucaPost>.Title => Subject;

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class MegucaPost : IPost
	{
		[JsonProperty("editing")]
		public bool Editing { get; set; }

		[JsonProperty("sage")]
		public bool Sage { get; set; }

		[JsonProperty("auth")]
		public uint Auth { get; set; }

		[JsonProperty("id")]
		public ulong PostNumber { get; set; }

		[JsonProperty("time")]
		public ulong PostTime { get; set; }

		[JsonProperty("body")]
		public string ContentBody { get; set; }

		[JsonProperty("flag")]
		public string Flag { get; set; }

		[JsonProperty("name")]
		public string AuthorName { get; set; }

		[JsonProperty("trip")]
		public string Tripcode { get; set; }

		[JsonProperty("image")]
		public MegucaPostImage Image { get; set; }

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion

		[JsonIgnore]
		string IPost.Content => ContentBody;

		uint IPost.UnixTimestamp => (uint)PostTime;
	}

	public class MegucaPostImage
	{
		[JsonProperty("spoiler")]
		public bool IsSpoiler { get; set; }

		[JsonProperty("audio")]
		public bool Audio { get; set; }

		[JsonProperty("video")]
		public bool Video { get; set; }

		[JsonProperty("file_type")]
		public uint FileType { get; set; }

		[JsonProperty("thumb_type")]
		public uint ThumbType { get; set; }

		[JsonProperty("length")]
		public ulong Length { get; set; }

		[JsonProperty("dims")]
		public uint[] Dimensions { get; set; }

		[JsonProperty("size")]
		public ulong FileSize { get; set; }

		[JsonProperty("artist")]
		public string Artist { get; set; }

		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("md5")]
		public string Md5Hash { get; set; }

		[JsonProperty("sha1")]
		public string Sha1Hash { get; set; }

		[JsonProperty("name")]
		public string Filename { get; set; }
	}
}