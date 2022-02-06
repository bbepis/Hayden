using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class VichanFilesystemThreadConsumer : BaseFilesystemThreadConsumer<VichanThread, VichanPost>
	{
		public string ImageboardWebsite { get; }

		public VichanFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<VichanThread, VichanPost> threadUpdateInfo, string threadDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			var vichanFiles = new List<VichanExtraFile>();
			
			foreach (var post in threadUpdateInfo.NewPosts.Where(x => x.OriginalFilename != null))
			{
				if (post.OriginalFilename != null)
				{
					vichanFiles.Add(new VichanExtraFile
					{
						FileExtension = post.FileExtension,
						FileSize = post.FileSize,
						ImageHeight = post.ImageHeight,
						ImageWidth = post.ImageWidth,
						OriginalFilename = post.OriginalFilename,
						ThumbnailHeight = post.ThumbnailHeight,
						ThumbnailWidth = post.ThumbnailWidth,
						TimestampedFilename = post.TimestampedFilename
					});
				}

				if (post.ExtraFiles != null)
				{
					vichanFiles.AddRange(post.ExtraFiles);
				}
			}

			foreach (var vichanFile in vichanFiles)
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, $"{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
					string fullImageUrl = $"{ImageboardWebsite}{pointer.Board}/thumb/{vichanFile.TimestampedFilename}{vichanFile.FileExtension}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, $"{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
					string thumbUrl = $"{ImageboardWebsite}{pointer.Board}/src/{vichanFile.TimestampedFilename}{vichanFile.FileExtension}";

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
				}
			}

			return imageDownloads;
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<VichanThread, VichanPost> threadUpdateInfo, VichanThread writtenThread)
		{
			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties

				existingPost.Comment = modifiedPost.Comment;

				// These are all OP-only properties. They're always null for replies so there's no harm in ignoring them

				if (existingPost.ReplyPostNumber == 0) // OP check
				{
					existingPost.Closed = modifiedPost.Closed;
					existingPost.TotalReplies = modifiedPost.TotalReplies;
					existingPost.TotalImages = modifiedPost.TotalImages;
				}
			}
		}
		/// <inheritdoc />
		public override TrackedThread<VichanThread, VichanPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo) => VichanTrackedThread.StartTrackingThread(existingThreadInfo);

		/// <inheritdoc />
		public override TrackedThread<VichanThread, VichanPost> StartTrackingThread() => VichanTrackedThread.StartTrackingThread();

		protected override uint CalculateHash(VichanPost post)
		{
			return VichanTrackedThread.CalculatePostHashFromObject(post);
		}

		internal class VichanTrackedThread : TrackedThread<VichanThread, VichanPost>
		{
			/// <summary>
			/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
			/// </summary>
			public static uint CalculatePostHash(string postHtml, string originalFilenameNoExt, bool? closed, uint? replyCount, ushort? imageCount)
			{
				// Null bool? values should evaluate to false everywhere
				static int EvaluateNullableBool(bool? value)
				{
					return value.HasValue
						? (value.Value ? 1 : 2)
						: 2;
				}

				// The HTML content of a post can change due to public warnings and bans.
				uint hashCode = Utility.FNV1aHash32(postHtml);

				// Attached files can be removed, and have their spoiler status changed
				Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

				// The OP of a thread can have numerous properties change.
				// As such, these properties are only considered mutable for OPs (because that's the only place they can exist) and immutable for replies.
				Utility.FNV1aHash32(EvaluateNullableBool(closed), ref hashCode);
				Utility.FNV1aHash32((int?)replyCount ?? -1, ref hashCode);
				Utility.FNV1aHash32(imageCount ?? -1, ref hashCode);

				return hashCode;
			}

			/// <summary>
			/// Creates a new <see cref="TrackedThread{,}"/> instance, utilizing information derived from an <see cref="IThreadConsumer{,}"/> implementation.
			/// </summary>
			/// <param name="existingThreadInfo">The thread information to initialize with.</param>
			/// <returns>An initialized <see cref="TrackedThread{,}"/> instance.</returns>
			public static TrackedThread<VichanThread, VichanPost> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
			{
				var trackedThread = new VichanTrackedThread();

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
			public static TrackedThread<VichanThread, VichanPost> StartTrackingThread()
			{
				var trackedThread = new VichanTrackedThread();

				trackedThread.PostHashes = new();
				trackedThread.PostCount = 0;

				return trackedThread;
			}

			public static uint CalculatePostHashFromObject(VichanPost post)
				=> CalculatePostHash(post.Comment, post.OriginalFilename, post.Closed, post.TotalReplies, post.TotalImages);

			public override uint CalculatePostHash(VichanPost post)
				=> CalculatePostHashFromObject(post);
		}
	}
}