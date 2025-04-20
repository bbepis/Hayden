using System;
using Hayden.Consumers.HaydenMysql.DB;
using Newtonsoft.Json;

namespace Hayden.Models;

public class Post
{
	public string Author { get; set; }
	public string Tripcode { get; set; }
	// TODO: add this to the schema
	public string Subject { get; set; }
	public string Email { get; set; }

	public DateTimeOffset TimePosted { get; set; }

	public ulong PostNumber { get; set; }

	public string ContentRendered { get; set; }
	public string ContentRaw { get; set; }
	public ContentType ContentType { get; set; }

	public DateTimeOffset? TimeDeleted { get; set; }

	public Media[] Media { get; set; }

	public object OriginalObject { get; set; }
	public PostAdditionalMetadata AdditionalMetadata { get; set; } = new();

	public class PostAdditionalMetadata
	{
		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonProperty("posterID")]
		public string PosterID { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }

		[JsonProperty("modifications")]
		public PostModification Modifications { get; set; }

		[JsonProperty("countryCode")]
		public string CountryCode { get; set; }
		[JsonProperty("countryName")]
		public string CountryName { get; set; }

		[JsonProperty("boardFlagCode")]
		public string BoardFlagCode { get; set; }
		[JsonProperty("boardFlagName")]
		public string BoardFlagName { get; set; }

		[JsonProperty("exif")]
		public string Exif { get; set; }
		[JsonProperty("asagi_exif")]
		public string AsagiExif { get; set; }

		[JsonProperty("ponychan_mature")]
		public bool? PonychanMature { get; set; }
		[JsonProperty("ponychan_anonymous")]
		public bool? PonychanAnonymous { get; set; }

		[JsonProperty("infinitynext_globalid")]
		public ulong? InfinityNextGlobalId { get; set; }

		[JsonProperty("source")]
		public string Source { get; set; }
	}
}