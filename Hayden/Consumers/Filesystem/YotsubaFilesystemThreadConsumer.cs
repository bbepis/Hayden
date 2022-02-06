using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class YotsubaFilesystemThreadConsumer : BaseFilesystemThreadConsumer<YotsubaThread, YotsubaPost>
	{
		public YotsubaFilesystemThreadConsumer(FilesystemConfig config) : base(config) { }

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<YotsubaThread, YotsubaPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var post in threadUpdateInfo.NewPosts.Where(x => x.TimestampedFilename.HasValue))
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, post.TimestampedFilenameFull);
					string fullImageUrl = $"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilenameFull}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, $"{post.TimestampedFilename}s.jpg");
					string thumbUrl = $"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilename}s.jpg";

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
				}
			}

			return imageDownloads;
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

		public override TrackedThread<YotsubaThread, YotsubaPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
		{
			return HaydenMysqlThreadConsumer.HaydenTrackedThread.StartTrackingThread(existingThreadInfo);
		}

		public override TrackedThread<YotsubaThread, YotsubaPost> StartTrackingThread()
		{
			return HaydenMysqlThreadConsumer.HaydenTrackedThread.StartTrackingThread();
		}

		protected override uint CalculateHash(YotsubaPost post)
		{
			return HaydenMysqlThreadConsumer.HaydenTrackedThread.CalculateYotsubaPostHash(post);
		}
	}
}