using Hayden.WebServer.DB;

namespace Hayden.WebServer.View
{
	public class PostPartialViewModel
	{
		public DBPost Post { get; set; }

		public PostPartialViewModel(DBPost post, Config config)
		{
			Post = post;

			if (HasFile)
			{
				string b36Name = Utility.ConvertToBase(Post.MediaHash);
				string extension = Post.MediaFilename.Remove(0, Post.MediaFilename.LastIndexOf('.'));

				if (config.ImagePrefix != null)
				{
					ImageUrl = $"{config.ImagePrefix}/{Post.Board}/image/{b36Name}{extension}";
					ThumbnailUrl = $"{config.ImagePrefix}/{Post.Board}/thumb/{b36Name}.jpg";
				}
				else
				{
					ImageUrl = $"/image/{Post.Board}/image/{b36Name}{extension}";
					ThumbnailUrl = $"/image/{Post.Board}/thumb/{b36Name}.jpg";
				}
			}
		}

		public bool HasFile => Post.MediaHash != null;
		public string ImageUrl { get; set; } = null;
		public string ThumbnailUrl { get; set; } = null;
	}
}