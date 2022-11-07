using Newtonsoft.Json.Linq;

namespace Hayden.Models;

public class Thread
{
	public ulong ThreadId { get; set; }

	public string Title { get; set; }
	public bool IsArchived { get; set; }

	public Post[] Posts { get; set; }
	
	public object OriginalObject { get; set; }
	public JObject AdditionalMetadata { get; set; }
}