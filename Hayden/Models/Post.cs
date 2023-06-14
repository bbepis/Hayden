using System;
using Hayden.Consumers.HaydenMysql.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

	public bool? IsDeleted { get; set; }

	public Media[] Media { get; set; }

	public object OriginalObject { get; set; }
	public PostAdditionalMetadata AdditionalMetadata { get; set; } = new();

	public class PostAdditionalMetadata
	{
		public string Capcode { get; set; }

		public string PosterID { get; set; }

		public string CountryCode { get; set; }
		public string CountryName { get; set; }

		public string BoardFlagCode { get; set; }
		public string BoardFlagName { get; set; }
		
		public string Exif { get; set; }
		public string AsagiExif { get; set; }

		public bool? PonychanMature { get; set; }
		public bool? PonychanAnonymous { get; set; }

		public ulong? InfinityNextGlobalId { get; set; }

		public string Serialize()
		{
			JObject jsonObject = new JObject();

			void addString(string key, string value)
			{
				if (!string.IsNullOrWhiteSpace(value))
					jsonObject[key] = value;
			}

			void addBool(string key, bool value)
			{
				if (value)
					jsonObject[key] = true;
			}

			addString("capcode", Capcode);
			addString("posterID", PosterID);
			addString("countryCode", CountryCode);
			addString("countryName", CountryName);
			addString("boardFlagCode", BoardFlagCode);
			addString("boardFlagName", BoardFlagName);
			addString("exif", Exif);
			addString("asagi_exif", AsagiExif);

			addBool("ponychan_mature", PonychanMature ?? false);
			addBool("ponychan_anonymous", PonychanAnonymous ?? false);

			if (InfinityNextGlobalId.HasValue)
				jsonObject["infinitynext_globalid"] = InfinityNextGlobalId.Value;

			return jsonObject.HasValues ? jsonObject.ToString(Formatting.None) : null;
		}
	}
}