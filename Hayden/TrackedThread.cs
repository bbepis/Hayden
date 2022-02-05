using System.Collections.Generic;
using System.Linq;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden
{
	/// <summary>
	/// Contains thread information used to determine which posts have been changed when polling.
	/// </summary>
	public class TrackedThread
	{
		/// <summary>
		/// The amount of posts in the thread the last time it was updated.
		/// </summary>
		public int PostCount { get; set; }

		/// <summary>
		/// A dictionary containing FNV1a hashes for each post in the thread.
		/// </summary>
		private SortedList<ulong, uint> PostHashes { get; set; }

		/// <summary>
		/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
		/// </summary>
		public static uint CalculateYotsubaPostHash(string postHtml, bool? spoilerImage, bool? fileDeleted, string originalFilenameNoExt,
			bool? archived, bool? closed, bool? bumpLimit, bool? imageLimit, uint? replyCount, ushort? imageCount, int? uniqueIpAddresses)
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
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(spoilerImage), hashCode);
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(fileDeleted), hashCode);
			hashCode = Utility.FNV1aHash32(originalFilenameNoExt, hashCode);

			// The OP of a thread can have numerous properties change.
			// As such, these properties are only considered mutable for OPs (because that's the only place they can exist) and immutable for replies.
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(archived), hashCode);
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(closed), hashCode);
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(bumpLimit), hashCode);
			hashCode = Utility.FNV1aHash32(EvaluateNullableBool(imageLimit), hashCode);
			hashCode = Utility.FNV1aHash32((int?)replyCount ?? -1, hashCode);
			hashCode = Utility.FNV1aHash32(imageCount ?? -1, hashCode);
			hashCode = Utility.FNV1aHash32(uniqueIpAddresses ?? -1, hashCode);

			return hashCode;
		}

		/// <summary>
		/// Calculates an FNV1a hash for a post.
		/// </summary>
		/// <param name="post">The post to calculate a hash for.</param>
		/// <returns>An FNV1a hash of the post.</returns>
		public static uint CalculateYotsubaPostHash(Post post)
			=> CalculateYotsubaPostHash(post.Comment, post.SpoilerImage, post.FileDeleted, post.OriginalFilename,
				post.Archived, post.Closed, post.BumpLimit, post.ImageLimit, post.TotalReplies, post.TotalImages, post.UniqueIps);

		private TrackedThread() { }

		/// <summary>
		/// Creates a new <see cref="TrackedThread"/> instance, utilizing information derived from an <see cref="IThreadConsumer"/> implementation.
		/// </summary>
		/// <param name="existingThreadInfo">The thread information to initialize with.</param>
		/// <returns>An initialized <see cref="TrackedThread"/> instance.</returns>
		public static TrackedThread StartTrackingThread(ExistingThreadInfo existingThreadInfo) //, bool trackingEnabled)
		{
			var trackedThread = new TrackedThread();

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
		/// Creates a blank <see cref="TrackedThread"/> instance. Intended for completely new threads, or threads that the backend hasn't encountered before.
		/// </summary>
		/// <returns>A blank <see cref="TrackedThread"/> instance.</returns>
		public static TrackedThread StartTrackingThread() //, bool trackingEnabled)
		{
			var trackedThread = new TrackedThread();

			trackedThread.PostHashes = new();
			trackedThread.PostCount = 0;

			return trackedThread;
		}

		/// <summary>
		/// Processes a polled thread, calculates a <see cref="ThreadUpdateInfo"/> object with thread change information, and updates the state of this <see cref="TrackedThread"/>.
		/// </summary>
		/// <param name="threadPointer">The thread pointer referring to the polled thread.</param>
		/// <param name="updatedThread">The new thread to calculate change information from.</param>
		/// <returns>A <see cref="ThreadUpdateInfo"/> object calculated from <param name="updatedThread">updatedThread</param>.</returns>
		public ThreadUpdateInfo ProcessThreadUpdates(in ThreadPointer threadPointer, Thread updatedThread)
		{
			var updateInfo = new ThreadUpdateInfo(threadPointer, updatedThread, false);

			foreach (var post in updatedThread.Posts)
			{
				if (!PostHashes.TryGetValue(post.PostNumber, out var existingHash))
				{
					// new post
					updateInfo.NewPosts.Add(post);
					PostHashes[post.PostNumber] = CalculateYotsubaPostHash(post);
				}
				else
				{
					// post already exists; check if it has changed

					var newHash = CalculateYotsubaPostHash(post);

					if (newHash != existingHash)
					{
						// it has changed
						updateInfo.UpdatedPosts.Add(post);
						PostHashes[post.PostNumber] = newHash;
					}
				}
			}

			foreach (var postId in PostHashes.Keys.ToArray())
			{
				if (updatedThread.Posts.All(x => x.PostNumber != postId))
				{
					// post is no longer in the thread; it has been deleted
					updateInfo.DeletedPosts.Add(postId);
					PostHashes.Remove(postId);
				}
			}

			PostCount = updatedThread.Posts.Count;

			return updateInfo;
		}
	}
}
