using Newtonsoft.Json;

namespace Hayden.Models
{
	public class Thread
	{
		[JsonProperty("posts")]
		public Post[] Posts { get; set; }

		[JsonIgnore]
		public Post OriginalPost => Posts[0];
	}
}