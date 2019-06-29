using Newtonsoft.Json;

namespace Hayden.Models
{
	public struct PageThread
	{
		[JsonIgnore]
		public const ulong ArchivedLastModifiedTime = ulong.MaxValue;

		[JsonProperty("no")]
		public ulong ThreadNumber { get; set; }

		[JsonProperty("last_modified")]
		public ulong LastModified { get; set; }

		[JsonIgnore]
		public bool IsArchived => LastModified == ArchivedLastModifiedTime;
	}


	public struct Page
	{
		[JsonProperty("page")]
		public uint PageNumber { get; set; }

		[JsonProperty("threads")]
		public PageThread[] Threads { get; set; }
	}
}