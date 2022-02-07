using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class MegucaFilesystemThreadConsumer : BaseFilesystemThreadConsumer<MegucaThread, MegucaPost>
	{
		public string ImageboardWebsite { get; }

		public MegucaFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		// https://github.com/bakape/shamichan/blob/8e47f42785caa99bbbfd2b35221f47822dbec1f3/imager/common/images.go#L11
		public static Dictionary<uint, string> ExtensionMappings = new()
		{
			[0] = ".jpg",
			[1] = ".png",
			[2] = ".gif",
			[3] = ".webm",
			[4] = ".pdf",
			[5] = ".svg",
			[6] = ".mp4",
			[7] = ".mp3",
			[8] = ".ogg",
			[9] = ".zip",
			[10] = ".7z",
			[11] = ".tgz",
			[12] = ".txz",
			[13] = ".flac",
			[14] = "",
			[15] = ".txt",
			[16] = ".webp",
			[17] = ".rar",
			[18] = ".cbz",
			[19] = ".cbr",
			[20] = ".mp4", // HEVC
		};

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<MegucaThread, MegucaPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var post in threadUpdateInfo.NewPosts.Where(x => x.Image?.Filename != null))
			{
				if (Config.FullImagesEnabled)
				{
					string imageName = $"{post.Image.Sha1Hash}{ExtensionMappings[post.Image.FileType]}";

					string fullImageFilename = Path.Combine(threadDirectory, imageName);
					string fullImageUrl = $"{ImageboardWebsite}assets/images/src/{imageName}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled)
				{
					string thumbName = $"{post.Image.Sha1Hash}{ExtensionMappings[post.Image.ThumbType]}";

					string fullThumbFilename = Path.Combine(threadThumbsDirectory, thumbName);
					string fullThumbUrl = $"{ImageboardWebsite}assets/images/thumb/{thumbName}";

					if (!File.Exists(fullThumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullThumbUrl), fullThumbFilename));
				}
			}

			return imageDownloads;
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<MegucaThread, MegucaPost> threadUpdateInfo, MegucaThread writtenThread)
		{
			writtenThread.Abbreviated = threadUpdateInfo.Thread.Abbreviated;
			writtenThread.Sticky = threadUpdateInfo.Thread.Sticky;
			writtenThread.Locked = threadUpdateInfo.Thread.Locked;
			writtenThread.PostCount = threadUpdateInfo.Thread.PostCount;
			writtenThread.ImageCount = threadUpdateInfo.Thread.ImageCount;
			writtenThread.UpdateTime = threadUpdateInfo.Thread.UpdateTime;
			writtenThread.BumpTime = threadUpdateInfo.Thread.BumpTime;
			writtenThread.Subject = threadUpdateInfo.Thread.Subject;
			writtenThread.IsDeleted = threadUpdateInfo.Thread.IsDeleted;

			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);
				
				existingPost.ContentBody = modifiedPost.ContentBody;
			}
		}

		public override TrackedThread<MegucaThread, MegucaPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
		{
			return MegucaTrackedThread.StartTrackingThread(existingThreadInfo);
		}

		public override TrackedThread<MegucaThread, MegucaPost> StartTrackingThread()
		{
			return MegucaTrackedThread.StartTrackingThread();
		}

		protected override uint CalculateHash(MegucaPost post)
		{
			return MegucaTrackedThread.CalculatePostHashFromObject(post);
		}
		
		internal class MegucaTrackedThread : TrackedThread<MegucaThread, MegucaPost>
		{
			/// <summary>
			/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
			/// </summary>
			public static uint CalculatePostHash(string postContent, string originalFilenameNoExt)
			{
				// The HTML content of a post can change due to public warnings and bans.
				uint hashCode = Utility.FNV1aHash32(postContent);

				// Attached files can be removed, and have their spoiler status changed
				Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

				return hashCode;
			}

			/// <summary>
			/// Creates a new <see cref="TrackedThread{,}"/> instance, utilizing information derived from an <see cref="IThreadConsumer{,}"/> implementation.
			/// </summary>
			/// <param name="existingThreadInfo">The thread information to initialize with.</param>
			/// <returns>An initialized <see cref="TrackedThread{,}"/> instance.</returns>
			public static TrackedThread<MegucaThread, MegucaPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
			{
				var trackedThread = new MegucaTrackedThread();

				trackedThread.PostHashes = new();

				if (existingThreadInfo.PostHashes != null)
				{
					foreach (var hash in existingThreadInfo.PostHashes)
						trackedThread.PostHashes[hash.PostId] = hash.PostHash;

					trackedThread.PostCount = existingThreadInfo.PostHashes.Count;
				}
				else
				{
					trackedThread.PostCount = 0;
				}

				return trackedThread;
			}

			/// <summary>
			/// Creates a blank <see cref="TrackedThread{,}"/> instance. Intended for completely new threads, or threads that the backend hasn't encountered before.
			/// </summary>
			/// <returns>A blank <see cref="TrackedThread{,}"/> instance.</returns>
			public static TrackedThread<MegucaThread, MegucaPost> StartTrackingThread()
			{
				var trackedThread = new MegucaTrackedThread();

				trackedThread.PostHashes = new();
				trackedThread.PostCount = 0;

				return trackedThread;
			}

			public static uint CalculatePostHashFromObject(MegucaPost post)
				=> CalculatePostHash(post.ContentBody, post.Image?.Filename);

			public override uint CalculatePostHash(MegucaPost post)
				=> CalculatePostHashFromObject(post);
		}
	}
}