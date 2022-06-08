using System;
using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class FutabaThread : IThread<FutabaPost>
	{
		public List<FutabaPost> Posts { get; set; }

		[JsonIgnore]
		public FutabaPost OriginalPost => Posts[0];
		
		[JsonIgnore]
		public bool Archived => false;

		[JsonIgnore]
		string IThread<FutabaPost>.Title => OriginalPost.Subject;

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class FutabaPost : IPost
	{
		public ulong PostNumber { get; set; }

		public string TextHtml { get; set; }

		public string Subject { get; set; }

		public DateTimeOffset DateTime { get; set; }

		public string Author { get; set; }

		public string UserId { get; set; }

		public string EmailAddress { get; set; }

		public string ImageFilename { get; set; }

		public string ImageUrl { get; set; }

		public string ThumbnailUrl { get; set; }

		public int? VoteCount { get; set; }

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion

		[JsonIgnore]
		string IPost.Content => TextHtml;

		uint IPost.UnixTimestamp => Utility.GetGMTTimestamp(DateTime);
	}
}