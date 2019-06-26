using Newtonsoft.Json;

namespace Hayden.Models
{
	public class PageThread
	{
		[JsonProperty("no")]
		public ulong ThreadNumber { get; set; }

		[JsonProperty("last_modified")]
		public ulong LastModified { get; set; }
	}


	public class Page
	{
		[JsonProperty("page")]
		public uint PageNumber { get; set; }

		[JsonProperty("threads")]
		public PageThread[] Threads { get; set; }
	}
}