using System;
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
		protected SortedList<ulong, uint> PostHashes { get; set; }

		/// <summary>
		/// A function that performs the hashing of a post.
		/// </summary>
		protected Func<Post, uint> HashFunction { get; set; }

		protected TrackedThread() { }

		/// <summary>
		/// Processes a polled thread, calculates a <see cref="ThreadUpdateInfo{,}"/> object with thread change information, and updates the state of this <see cref="TrackedThread"/>.
		/// </summary>
		/// <param name="threadPointer">The thread pointer referring to the polled thread.</param>
		/// <param name="updatedThread">The new thread to calculate change information from.</param>
		/// <returns>A <see cref="ThreadUpdateInfo{,}"/> object calculated from <param name="updatedThread">updatedThread</param>.</returns>
		public virtual ThreadUpdateInfo ProcessThreadUpdates(in ThreadPointer threadPointer, Thread updatedThread, bool processModifications = true)
		{
			var updateInfo = new ThreadUpdateInfo(threadPointer, updatedThread, false);

			foreach (var post in updatedThread.Posts)
			{
				if (!PostHashes.TryGetValue(post.PostNumber, out var existingHash))
				{
					// new post
					updateInfo.NewPosts.Add(post);
					PostHashes[post.PostNumber] = HashFunction(post);
				}
				else if (processModifications)
				{
					// post already exists; check if it has changed

					var newHash = HashFunction(post);

					if (newHash != existingHash)
					{
						// it has changed
						updateInfo.UpdatedPosts.Add(post);
						PostHashes[post.PostNumber] = newHash;
					}
				}
			}

			if (processModifications)
				foreach (var postId in PostHashes.Keys.ToArray())
				{
					if (updatedThread.Posts.All(x => x.PostNumber != postId))
					{
						// post is no longer in the thread; it has been deleted
						updateInfo.DeletedPosts.Add(postId);
						PostHashes.Remove(postId);
					}
				}

			PostCount = updatedThread.Posts.Length;

			return updateInfo;
		}

		/// <summary>
		/// Creates a new <see cref="TrackedThread{,}"/> instance, utilizing information derived from an <see cref="IThreadConsumer{,}"/> implementation.
		/// </summary>
		/// <param name="existingThreadInfo">The thread information to initialize with.</param>
		/// <returns>An initialized <see cref="TrackedThread{,}"/> instance.</returns>
		public static TrackedThread StartTrackingThread(Func<Post, uint> hashFunction, ExistingThreadInfo existingThreadInfo)
		{
			var trackedThread = new TrackedThread();

			trackedThread.HashFunction = hashFunction;
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
		public static TrackedThread StartTrackingThread(Func<Post, uint> hashFunction)
		{
			var trackedThread = new TrackedThread();

			trackedThread.HashFunction = hashFunction;
			trackedThread.PostHashes = new();
			trackedThread.PostCount = 0;

			return trackedThread;
		}
	}
}
