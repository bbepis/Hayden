﻿using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class VichanThread : IThread<VichanPost>
	{
		[JsonProperty("posts")]
		public List<VichanPost> Posts { get; set; }

		[JsonIgnore]
		public VichanPost OriginalPost => Posts[0];

		[JsonProperty]
		public bool Archived { get; set; }

		[JsonIgnore]
		string IThread<VichanPost>.Title => OriginalPost.Subject;

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}

	public class VichanPost : IPost
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
		
		[JsonProperty("cyclical")]
		public string Cyclical { get; set; }

		[JsonProperty("time")]
		public uint UnixTimestamp { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("trip")]
		public string Trip { get; set; }

		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonProperty("country")]
		public string CountryCode { get; set; }

		[JsonProperty("sub")]
		public string Subject { get; set; }

		[JsonProperty("com")]
		public string Comment { get; set; }

		[JsonProperty("tim")]
		public string TimestampedFilename { get; set; }

		[JsonProperty("filename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("ext")]
		public string FileExtension { get; set; }

		[JsonProperty("fsize")]
		public uint? FileSize { get; set; }

		//[JsonProperty("md5")]
		//public string FileMd5 { get; set; }

		[JsonProperty("w")]
		public ushort? ImageWidth { get; set; }

		[JsonProperty("h")]
		public ushort? ImageHeight { get; set; }

		[JsonProperty("tn_w")]
		public byte? ThumbnailWidth { get; set; }

		[JsonProperty("tn_h")]
		public byte? ThumbnailHeight { get; set; }
		
		[JsonProperty("country_name")]
		public string CountryName { get; set; }

		[JsonProperty("custom_spoiler")]
		public byte? CustomSpoiler { get; set; }

		[JsonProperty("replies")]
		public uint? TotalReplies { get; set; }

		[JsonProperty("images")]
		public ushort? TotalImages { get; set; }

		[JsonProperty("extra_files")]
		public List<VichanExtraFile> ExtraFiles { get; set; }

		#region Hayden-specific and non-standard

		[JsonProperty("extension_isdeleted")]
		public bool? ExtensionIsDeleted { get; set; }

		#endregion

		[JsonIgnore]
		public string OriginalFilenameFull => OriginalFilename != null ? OriginalFilename + FileExtension : null;

		[JsonIgnore]
		public string TimestampedFilenameFull => OriginalFilename != null ? TimestampedFilename + FileExtension : null;

		[JsonIgnore]
		string IPost.Content => Comment;
	}

	public class VichanExtraFile
	{
		[JsonProperty("tim")]
		public string TimestampedFilename { get; set; }

		[JsonProperty("filename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("ext")]
		public string FileExtension { get; set; }

		[JsonProperty("fsize")]
		public uint? FileSize { get; set; }

		//[JsonProperty("md5")]
		//public string FileMd5 { get; set; }

		[JsonProperty("w")]
		public ushort? ImageWidth { get; set; }

		[JsonProperty("h")]
		public ushort? ImageHeight { get; set; }

		[JsonProperty("tn_w")]
		public byte? ThumbnailWidth { get; set; }

		[JsonProperty("tn_h")]
		public byte? ThumbnailHeight { get; set; }
	}
}