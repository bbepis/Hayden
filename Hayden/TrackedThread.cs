using System.Collections.Generic;
using System.Linq;
using Hayden.Consumers;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden
{
	/// <summary>
	/// Contains thread information used to determine which posts have been changed when polling.
	/// </summary>
	public abstract class TrackedThread<TThread, TPost> where TPost : IPost where TThread : IThread<TPost>
	{
		/// <summary>
		/// The amount of posts in the thread the last time it was updated.
		/// </summary>
		public int PostCount { get; set; }

		/// <summary>
		/// A dictionary containing FNV1a hashes for each post in the thread.
		/// </summary>
		protected SortedList<ulong, uint> PostHashes { get; set; }

		protected TrackedThread() { }

		/// <summary>
		/// Processes a polled thread, calculates a <see cref="ThreadUpdateInfo{,}"/> object with thread change information, and updates the state of this <see cref="TrackedThread"/>.
		/// </summary>
		/// <param name="threadPointer">The thread pointer referring to the polled thread.</param>
		/// <param name="updatedThread">The new thread to calculate change information from.</param>
		/// <returns>A <see cref="ThreadUpdateInfo{,}"/> object calculated from <param name="updatedThread">updatedThread</param>.</returns>
		public virtual ThreadUpdateInfo<TThread, TPost> ProcessThreadUpdates(in ThreadPointer threadPointer, TThread updatedThread)
		{
			var updateInfo = new ThreadUpdateInfo<TThread, TPost>(threadPointer, updatedThread, false);

			foreach (var post in updatedThread.Posts)
			{
				if (!PostHashes.TryGetValue(post.PostNumber, out var existingHash))
				{
					// new post
					updateInfo.NewPosts.Add(post);
					PostHashes[post.PostNumber] = CalculatePostHash(post);
				}
				else
				{
					// post already exists; check if it has changed

					var newHash = CalculatePostHash(post);

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

		/// <summary>
		/// Calculates an FNV1a (32-bit) hash for a post.
		/// </summary>
		/// <param name="post">The post to calculate a hash for.</param>
		/// <returns>An FNV1a hash of the post.</returns>
		public abstract uint CalculatePostHash(TPost post);
	}
}
