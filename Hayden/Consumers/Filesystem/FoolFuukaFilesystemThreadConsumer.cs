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

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<FoolFuukaThread, FoolFuukaPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var post in threadUpdateInfo.NewPosts.Where(x => x.Media != null))
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, post.Media.TimestampedFilename);

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(post.Media.FileUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled && post.Media.TimestampedThumbFilename != null)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, post.Media.TimestampedThumbFilename);;

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(post.Media.ThumbnailUrl), thumbFilename));
				}
			}

			return imageDownloads;
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