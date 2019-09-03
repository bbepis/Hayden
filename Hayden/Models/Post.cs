using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class Post
	{
		// I comment out properties that are part of the API spec, but not used by Hayden.
		// I don't leave them in anyway, since we get a performance benefit by not having to deserialize them and keep them loaded in memory.

		[JsonProperty("no")]
		public ulong PostNumber { get; set; }

		[JsonProperty("resto")]
		public ulong ReplyPostNumber { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("sticky")]
		public bool? Sticky { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("closed")]
		public bool? Closed { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("archived")]
		public bool? Archived { get; set; }
		
		//[JsonProperty("archived_on")]
		//public uint? ArchivedOn { get; set; }
		
		//[JsonConverter(typeof(YotsubaDateConverter))]
		//[JsonProperty("now")]
		//public DateTime? PostTime { get; set; }
		
		[JsonProperty("time")]
		public uint UnixTimestamp { get; set; }
		
		[JsonProperty("name")]
		public string Name { get; set; }
		
		[JsonProperty("trip")]
		public string Trip { get; set; }
		
		[JsonProperty("id")]
		public string PosterID { get; set; }
		
		[JsonProperty("capcode")]
		public string Capcode { get; set; }
		
		[JsonProperty("country")]
		public string CountryCode { get; set; }
		
		//[JsonProperty("country_name")]
		//public string CountryName { get; set; }
		
		[JsonProperty("sub")]
		public string Subject { get; set; }
		
		[JsonProperty("com")]
		public string Comment { get; set; }
		
		[JsonProperty("tim")]
		public ulong? TimestampedFilename { get; set; }
		
		[JsonProperty("filename")]
		public string OriginalFilename { get; set; }
		
		[JsonProperty("ext")]
		public string FileExtension { get; set; }
		
		[JsonProperty("fsize")]
		public uint? FileSize { get; set; }
		
		[JsonProperty("md5")]
		public string FileMd5 { get; set; }

		[JsonProperty("w")]
		public ushort? ImageWidth { get; set; }

		[JsonProperty("h")]
		public ushort? ImageHeight { get; set; }

		[JsonProperty("tn_w")]
		public byte? ThumbnailWidth { get; set; }

		[JsonProperty("tn_h")]
		public byte? ThumbnailHeight { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("filedeleted")]
		public bool? FileDeleted { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("spoiler")]
		public bool? SpoilerImage { get; set; }
		
		//[JsonProperty("custom_spoiler")]
		//public byte? CustomSpoiler { get; set; }
		
		//[JsonProperty("omitted_posts")]
		//public ushort? OmittedPosts { get; set; }
		
		//[JsonProperty("omitted_images")]
		//public ushort? OmittedImages { get; set; }
		
		//[JsonProperty("replies")]
		//public uint? TotalReplies { get; set; }
		
		//[JsonProperty("images")]
		//public ushort? TotalImages { get; set; }

		//[JsonConverter(typeof(BoolIntConverter))]
		//[JsonProperty("bumplimit")]
		//public bool? BumpLimit { get; set; }

		//[JsonConverter(typeof(BoolIntConverter))]
		//[JsonProperty("imagelimit")]
		//public bool? ImageLimit { get; set; }

		//[JsonProperty("capcode_replies")]
		//public Dictionary<string, int[]> CapcodeReplies { get; set; }

		//[JsonProperty("last_modified")]
		//public uint? LastModified { get; set; }

		//[JsonProperty("tag")]
		//public string Tag { get; set; }

		//[JsonProperty("semantic_url")]
		//public string SemanticUrl { get; set; }

		[JsonProperty("since4pass")]
		public ushort? Since4Pass { get; set; }

		[JsonProperty("unique_ips")]
		public int? UniqueIps { get; set; }

		[JsonProperty("troll_country")]
		public string TrollCountry { get; set; }



		[JsonIgnore]
		public string OriginalFilenameFull => FileMd5 != null ? OriginalFilename + FileExtension : null;

		[JsonIgnore]
		public string TimestampedFilenameFull => FileMd5 != null ? TimestampedFilename + FileExtension : null;
	}
}