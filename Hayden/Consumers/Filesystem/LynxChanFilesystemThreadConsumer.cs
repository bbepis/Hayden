using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class LynxChanFilesystemThreadConsumer : BaseFilesystemThreadConsumer<LynxChanThread, LynxChanPost>
	{
		public string ImageboardWebsite { get; }

		public LynxChanFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(LynxChanPost post, string threadImageDirectory, ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			if (post.Files == null)
				yield break;

			foreach (var file in post.Files)
			{
				string fullImageFilename = null, thumbFilename = null;
				Uri imageUrl = null, thumbUrl = null;

				if (Config.FullImagesEnabled)
				{
					fullImageFilename = Path.Combine(threadImageDirectory, file.DirectPath);
					imageUrl = new Uri($"{ImageboardWebsite}{file.Path.Substring(1)}");
				}

				if (Config.ThumbnailsEnabled)
				{
					thumbFilename = Path.Combine(threadThumbsDirectory, file.DirectThumbPath);
					thumbUrl = new Uri($"{ImageboardWebsite}{file.ThumbnailUrl.Substring(1)}");
				}

				yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
			}
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<LynxChanThread, LynxChanPost> threadUpdateInfo, LynxChanThread writtenThread)
		{
			writtenThread.Archived = threadUpdateInfo.Thread.Archived;
			writtenThread.AutoSage = threadUpdateInfo.Thread.AutoSage;
			writtenThread.Cyclic = threadUpdateInfo.Thread.Cyclic;
			writtenThread.Pinned = threadUpdateInfo.Thread.Pinned;
			writtenThread.Locked = threadUpdateInfo.Thread.Locked;

			// overwrite any modified posts

			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties

				existingPost.Markdown = modifiedPost.Markdown;
				existingPost.Message = modifiedPost.Message;

				//if (modifiedPost.SpoilerImage.HasValue) // This might be null if the image is deleted
				//	existingPost.SpoilerImage = modifiedPost.SpoilerImage;

				//// To mark deleted images, we still set this property but leave all other image-related properties intact
				//existingPost.FileDeleted = modifiedPost.FileDeleted;
			}
		}

		public static uint CalculatePostHash(string postMessage, string postMarkdown) // I have no idea how to check for deleted images
		{
			// The HTML content of a post can change due to public warnings and bans.
			uint hashCode = Utility.FNV1aHash32(postMessage);

			Utility.FNV1aHash32(postMarkdown, ref hashCode);

			return hashCode;
		}
		
		/// <inheritdoc />
		public override uint CalculateHash(LynxChanPost post)
			=> CalculatePostHash(post.Message, post.Markdown);
	}
}