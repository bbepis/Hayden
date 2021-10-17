using Newtonsoft.Json;

namespace Hayden.Models
{
	public struct PageThread
	{
		[JsonProperty("no")]
		public ulong ThreadNumber { get; set; }

		[JsonProperty("last_modified")]
		public ulong LastModified { get; set; }

		[JsonProperty("sub")]
		public string Subject { get; set; }

		[JsonProperty("com")]
		public string Html { get; set; }
	}


	public struct Page
	{
		[JsonProperty("page")]
		public uint PageNumber { get; set; }

		[JsonProperty("threads")]
		public PageThread[] Threads { get; set; }
	}
}