using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class YotsubaFilesystemThreadConsumer : BaseFilesystemThreadConsumer<YotsubaThread, YotsubaPost>
	{
		public YotsubaFilesystemThreadConsumer(FilesystemConfig config) : base(config) { }
		
		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(YotsubaPost post, string threadImageDirectory, ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			if (!post.TimestampedFilename.HasValue)
				yield break;

			string fullImageFilename = null, thumbFilename = null;
			Uri imageUrl = null, thumbUrl = null;

			if (Config.FullImagesEnabled)
			{
				fullImageFilename = Path.Combine(threadImageDirectory, post.TimestampedFilenameFull);
				imageUrl = new Uri($"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilenameFull}");
			}

			if (Config.ThumbnailsEnabled)
			{
				thumbFilename = Path.Combine(threadThumbsDirectory, $"{post.TimestampedFilename}s.jpg");
				thumbUrl = new Uri($"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilename}s.jpg");
			}

			yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
		}

		public new static void PerformJsonThreadUpdate(ThreadUpdateInfo<YotsubaThread, YotsubaPost> threadUpdateInfo, string threadFileName)
		{
			var config = new FilesystemConfig();
			((BaseFilesystemThreadConsumer<YotsubaThread, YotsubaPost>)(new YotsubaFilesystemThreadConsumer(config))).PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<YotsubaThread, YotsubaPost> threadUpdateInfo, YotsubaThread writtenThread)
		{
			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties

				existingPost.Comment = modifiedPost.Comment;

				if (modifiedPost.SpoilerImage.HasValue) // This might be null if the image is deleted
					existingPost.SpoilerImage = modifiedPost.SpoilerImage;

				// To mark deleted images, we still set this property but leave all other image-related properties intact
				existingPost.FileDeleted = modifiedPost.FileDeleted;

				// These are all OP-only properties. They're always null for replies so there's no harm in ignoring them

				if (existingPost.ReplyPostNumber == 0) // OP check
				{
					existingPost.Archived = modifiedPost.Archived;
					existingPost.ArchivedOn = modifiedPost.ArchivedOn;
					existingPost.Closed = modifiedPost.Closed;
					existingPost.BumpLimit = modifiedPost.BumpLimit;
					existingPost.ImageLimit = modifiedPost.ImageLimit;
					existingPost.TotalReplies = modifiedPost.TotalReplies;
					existingPost.TotalImages = modifiedPost.TotalImages;
					existingPost.UniqueIps = modifiedPost.UniqueIps;
				}
			}
		}
		
		/// <inheritdoc />
		public override uint CalculateHash(YotsubaPost post)
		{
			return HaydenMysqlThreadConsumer.CalculatePostHash(post.Comment, post.SpoilerImage, post.FileDeleted, post.OriginalFilename,
				post.Archived, post.Closed, post.BumpLimit, post.ImageLimit, post.TotalReplies, post.TotalImages, post.UniqueIps);
		}
	}
}