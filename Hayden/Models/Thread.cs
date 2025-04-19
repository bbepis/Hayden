using System;
using Newtonsoft.Json;

namespace Hayden.Models;

public class Thread
{
	public ulong ThreadId { get; set; }

	public string Title { get; set; }

	public Post[] Posts { get; set; }

	public DateTimeOffset? DeletedTime { get; set; }
	public DateTimeOffset? ArchivedTime { get; set; }
	
	public object OriginalObject { get; set; }
	public ThreadAdditionalMetadata AdditionalMetadata { get; set; }

	public class ThreadAdditionalMetadata
	{
		[JsonProperty("sticky")]
		public bool Sticky { get; set; }
	}
}