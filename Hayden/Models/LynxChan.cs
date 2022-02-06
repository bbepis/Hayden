using System;
using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class LynxChanThread : IThread<LynxChanPost>
	{
		[JsonProperty("posts")]
		public List<LynxChanPost> Posts { get; set; }

		[JsonIgnore]
		public LynxChanPost OriginalPost => Posts[0];

		[JsonProperty("threadId")]
		public ulong ThreadId { get; set; }

		[JsonProperty("subject")]
		public string Title { get; set; }

		[JsonProperty("archived")]
		public bool Archived { get; set; }

		[JsonProperty("locked")]
		public bool Locked { get; set; }

		[JsonProperty("pinned")]
		public bool Pinned { get; set; }

		[JsonProperty("cyclic")]
		public bool Cyclic { get; set; }

		[JsonProperty("autoSage")]
		public bool AutoSage { get; set; }

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class LynxChanPost : IPost
	{
		[JsonProperty("postId")]
		public ulong PostNumber { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }
		
		[JsonProperty("creation")]
		public string CreationDateTime { get; set; }
		
		[JsonProperty("markdown")]
		public string Markdown { get; set; }
		
		[JsonProperty("message")]
		public string Message { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("signedRole")]
		public string SignedRole { get; set; }

		[JsonProperty("files")]
		public LynxChanPostFile[] Files { get; set; }
		
		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }
		
		[JsonIgnore]
		string IPost.Content => Message;

		[JsonIgnore]
		uint IPost.UnixTimestamp => (uint)DateTimeOffset.Parse(CreationDateTime).ToUnixTimeSeconds();

		#endregion
	}

	public class LynxChanPostFile
	{
		[JsonProperty("originalName")]
		public string OriginalName { get; set; }

		[JsonProperty("path")]
		public string Path { get; set; }

		[JsonProperty("thumb")]
		public string ThumbnailUrl { get; set; }

		[JsonProperty("mime")]
		public string MimeType { get; set; }

		[JsonProperty("size")]
		public ulong FileSize { get; set; }

		[JsonProperty("width")]
		public ulong? Width { get; set; }

		[JsonProperty("height")]
		public ulong? Height { get; set; }

		[JsonIgnore]
		public string DirectPath => Path.Substring(Path.LastIndexOf('/') + 1);

		[JsonIgnore]
		public string DirectThumbPath => ThumbnailUrl?.Substring(Path.LastIndexOf('/') + 1);

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}
}