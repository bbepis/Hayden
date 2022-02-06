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

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<LynxChanThread, LynxChanPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var file in threadUpdateInfo.NewPosts.SelectMany(x => x.Files))
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, file.DirectPath);
					string fullImageUrl = $"{ImageboardWebsite}{file.Path.Substring(1)}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled && file.ThumbnailUrl != null)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, file.DirectThumbPath);
					string thumbUrl = $"{ImageboardWebsite}{file.ThumbnailUrl.Substring(1)}";

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
				}
			}

			return imageDownloads;
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
		
		#region Thread tracking

		/// <inheritdoc />
		public override TrackedThread<LynxChanThread, LynxChanPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo) => LynxChanTrackedThread.StartTrackingThread(existingThreadInfo);

		/// <inheritdoc />
		public override TrackedThread<LynxChanThread, LynxChanPost> StartTrackingThread() => LynxChanTrackedThread.StartTrackingThread();

		protected override uint CalculateHash(LynxChanPost post) => LynxChanTrackedThread.CalculatePostHashObject(post);

		internal class LynxChanTrackedThread : TrackedThread<LynxChanThread, LynxChanPost>
		{
			/// <summary>
			/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
			/// </summary>
			public static uint CalculatePostHash(string postMessage, string postMarkdown) // I have no idea how to check for deleted images
			{
				// The HTML content of a post can change due to public warnings and bans.
				uint hashCode = Utility.FNV1aHash32(postMessage);
				
				Utility.FNV1aHash32(postMarkdown, ref hashCode);

				return hashCode;
			}

			/// <summary>
			/// Creates a new <see cref="TrackedThread{,}"/> instance, utilizing information derived from an <see cref="IThreadConsumer{,}"/> implementation.
			/// </summary>
			/// <param name="existingThreadInfo">The thread information to initialize with.</param>
			/// <returns>An initialized <see cref="TrackedThread{,}"/> instance.</returns>
			public static TrackedThread<LynxChanThread, LynxChanPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
			{
				var trackedThread = new LynxChanTrackedThread();

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
			public static TrackedThread<LynxChanThread, LynxChanPost> StartTrackingThread()
			{
				var trackedThread = new LynxChanTrackedThread();

				trackedThread.PostHashes = new();
				trackedThread.PostCount = 0;

				return trackedThread;
			}

			public static uint CalculatePostHashObject(LynxChanPost post)
				=> CalculatePostHash(post.Message, post.Markdown);

			public override uint CalculatePostHash(LynxChanPost post)
				=> CalculatePostHashObject(post);
		}

		#endregion
	}
}