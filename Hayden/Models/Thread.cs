using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hayden.Models
{
	public interface IThread<TPost> where TPost : IPost
	{
		List<TPost> Posts { get; set; }
	}

	public class Thread : IThread<Post>
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