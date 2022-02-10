using System;
using System.Linq;
using Hayden.WebServer.DB;

namespace Hayden.WebServer.View
{
	public class PostPartialViewModel
	{
		public DBPost Post { get; set; }
		public (DBFileMapping mapping, DBFile file)[] FileMappings { get; set; }

		public PostPartialViewModel(DBPost post, string board, (DBFileMapping mapping, DBFile file)[] mappings, Config config)
		{
			Post = post;

			FileMappings = mappings.Where(x => x.mapping.PostId == post.PostId).ToArray();

			if (FileMappings.Length != 0)
			{
				ImageUrls = new string[FileMappings.Length];
				ThumbnailUrls = new string[FileMappings.Length];

				int i = 0;
				foreach (var mapping in FileMappings)
				{
					string b36Name = Utility.ConvertToBase(mapping.file.Md5Hash);

					var prefix = config.ImagePrefix ?? "image";

					ImageUrls[i] = $"{prefix}/{board}/image/{b36Name}.{mapping.file.Extension}";
					ThumbnailUrls[i] = $"{prefix}/{board}/thumb/{b36Name}.jpg";

					i++;
				}
			}
			else
			{
				ImageUrls = Array.Empty<string>();
				ThumbnailUrls = Array.Empty<string>();
			}
		}

		public static (string imageUrl, string thumbnailUrl) GenerateUrls(DBFile file, string board, Config config)
		{
			string b36Name = Utility.ConvertToBase(file.Md5Hash);

			var prefix = config.ImagePrefix ?? "image";

			var imageUrl = $"{prefix}/{board}/image/{b36Name}.{file.Extension}";
			var thumbUrl = $"{prefix}/{board}/thumb/{b36Name}.jpg";

			return (imageUrl, thumbUrl);
		}
		
		public string[] ImageUrls { get; set; }
		public string[] ThumbnailUrls { get; set; }
	}
}