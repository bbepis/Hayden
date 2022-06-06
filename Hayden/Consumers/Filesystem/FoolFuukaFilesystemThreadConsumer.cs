using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class FoolFuukaFilesystemThreadConsumer : BaseFilesystemThreadConsumer<FoolFuukaThread, FoolFuukaPost>
	{
		public string ImageboardWebsite { get; }

		public FoolFuukaFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(FoolFuukaPost post,
			string threadImageDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			if (post.Media == null)
				yield break;

			string fullImageFilename = null, thumbFilename = null;
			Uri imageUrl = null, thumbUrl = null;

			if (Config.FullImagesEnabled)
			{
				fullImageFilename = Path.Combine(threadImageDirectory, post.Media.TimestampedFilename);
				imageUrl = new Uri(post.Media.FileUrl);
			}

			if (Config.ThumbnailsEnabled && post.Media.TimestampedThumbFilename != null)
			{
				thumbFilename = Path.Combine(threadThumbsDirectory, post.Media.TimestampedThumbFilename); ;
				thumbUrl = new Uri(post.Media.ThumbnailUrl);
			}

			yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<FoolFuukaThread, FoolFuukaPost> threadUpdateInfo, FoolFuukaThread writtenThread)
		{
			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)
				
				existingPost.SanitizedComment = modifiedPost.SanitizedComment;
			}
		}
		
		/// <inheritdoc />
		public override uint CalculateHash(FoolFuukaPost post)
		{
			return Utility.FNV1aHash32(post.SanitizedComment);
		}
	}
}