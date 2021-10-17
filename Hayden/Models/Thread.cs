using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public class Thread
	{
		[JsonProperty("posts")]
		public List<Post> Posts { get; set; }

		[JsonIgnore]
		public Post OriginalPost => Posts[0];

		#region Hayden-specific and non-standard
		
		[JsonProperty("extension_isdeleted")]
		public bool? IsDeleted { get; set; }

		#endregion
	}
}