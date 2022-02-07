using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class InfinityNextFilesystemThreadConsumer : BaseFilesystemThreadConsumer<InfinityNextThread, InfinityNextPost>
	{
		public string ImageboardWebsite { get; }

		public InfinityNextFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<InfinityNextThread, InfinityNextPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var attachment in threadUpdateInfo.NewPosts.SelectMany(x => x.Attachments))
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, Path.GetFileName(attachment.FileUrl));
					string fullImageUrl = $"{ImageboardWebsite}{attachment.FileUrl.Substring(1)}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, Path.GetFileName(attachment.ThumbnailUrl));
					string thumbUrl = $"{ImageboardWebsite}{attachment.ThumbnailUrl.Substring(1)}";

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
				}
			}

			return imageDownloads;
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<InfinityNextThread, InfinityNextPost> threadUpdateInfo, InfinityNextThread writtenThread)
		{
			writtenThread.ReplyCount = threadUpdateInfo.Thread.ReplyCount;
			writtenThread.ReplyFileCount = threadUpdateInfo.Thread.ReplyFileCount;
			writtenThread.ReplyLast = threadUpdateInfo.Thread.ReplyLast;
			writtenThread.BumpedLast = threadUpdateInfo.Thread.BumpedLast;
			writtenThread.Stickied = threadUpdateInfo.Thread.Stickied;
			writtenThread.StickiedAt = threadUpdateInfo.Thread.StickiedAt;
			writtenThread.BumpLockedAt = threadUpdateInfo.Thread.BumpLockedAt;
			writtenThread.LockedAt = threadUpdateInfo.Thread.LockedAt;
			writtenThread.CyclicalAt = threadUpdateInfo.Thread.CyclicalAt;
			writtenThread.Locked = threadUpdateInfo.Thread.Locked;
			writtenThread.GlobalBumpedLast = threadUpdateInfo.Thread.GlobalBumpedLast;
			writtenThread.FeaturedAt = threadUpdateInfo.Thread.FeaturedAt;
			writtenThread.IsDeleted = threadUpdateInfo.Thread.IsDeleted;

			// overwrite any modified posts

			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties
				
				existingPost.UpdatedAt = modifiedPost.UpdatedAt;
				existingPost.UpdatedBy = modifiedPost.UpdatedBy;
				existingPost.DeletedAt = modifiedPost.DeletedAt;
				existingPost.AuthorIpNulledAt = modifiedPost.AuthorIpNulledAt;
				existingPost.CapcodeId = modifiedPost.CapcodeId;
				existingPost.Subject = modifiedPost.Subject;
				existingPost.AdventureId = modifiedPost.AdventureId;
				existingPost.BodyTooLong = modifiedPost.BodyTooLong;
				existingPost.BodyHasContent = modifiedPost.BodyHasContent;
				existingPost.BodyRightToLeft = modifiedPost.BodyRightToLeft;
				existingPost.BodySigned = modifiedPost.BodySigned;
				existingPost.ContentHtml = modifiedPost.ContentHtml;
				existingPost.ContentRaw = modifiedPost.ContentRaw;
				existingPost.GlobalBumpedLast = modifiedPost.GlobalBumpedLast;
				existingPost.Attachments = modifiedPost.Attachments;
			}
		}
		
		#region Thread tracking

		/// <inheritdoc />
		public override TrackedThread<InfinityNextThread, InfinityNextPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo) => InfinityNextTrackedThread.StartTrackingThread(existingThreadInfo);

		/// <inheritdoc />
		public override TrackedThread<InfinityNextThread, InfinityNextPost> StartTrackingThread() => InfinityNextTrackedThread.StartTrackingThread();

		protected override uint CalculateHash(InfinityNextPost post) => InfinityNextTrackedThread.CalculatePostHashObject(post);

		internal class InfinityNextTrackedThread : TrackedThread<InfinityNextThread, InfinityNextPost>
		{
			/// <summary>
			/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
			/// </summary>
			public static uint CalculatePostHash(string postContent, string postHtml, ulong updatedAt)
			{
				uint hashCode = Utility.FNV1aHash32(postContent);
				
				Utility.FNV1aHash32(postHtml, ref hashCode);
				Utility.FNV1aHash32(updatedAt, ref hashCode);

				return hashCode;
			}

			/// <summary>
			/// Creates a new <see cref="TrackedThread{,}"/> instance, utilizing information derived from an <see cref="IThreadConsumer{,}"/> implementation.
			/// </summary>
			/// <param name="existingThreadInfo">The thread information to initialize with.</param>
			/// <returns>An initialized <see cref="TrackedThread{,}"/> instance.</returns>
			public static TrackedThread<InfinityNextThread, InfinityNextPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
			{
				var trackedThread = new InfinityNextTrackedThread();

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
			public static TrackedThread<InfinityNextThread, InfinityNextPost> StartTrackingThread()
			{
				var trackedThread = new InfinityNextTrackedThread();

				trackedThread.PostHashes = new();
				trackedThread.PostCount = 0;

				return trackedThread;
			}

			public static uint CalculatePostHashObject(InfinityNextPost post)
				=> CalculatePostHash(post.ContentRaw, post.ContentHtml, post.UpdatedAt.GetValueOrDefault());

			public override uint CalculatePostHash(InfinityNextPost post)
				=> CalculatePostHashObject(post);
		}

		#endregion
	}
}