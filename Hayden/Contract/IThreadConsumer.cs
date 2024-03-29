using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Models;

namespace Hayden.Contract;

/// <summary>
/// An interface for consuming and storing threads from the archiver process.
/// </summary>
public interface IThreadConsumer : IDisposable
{
	/// <summary>
	/// Initializes the thread consumer.
	/// </summary>
	Task InitializeAsync();

	/// <summary>
	/// Tells the thread consumer to commit any pending writes.
	/// </summary>
	//Task CommitAsync();

	/// <summary>
	/// Consumes a thread, and returns a list of images to be downloaded.
	/// <para>For implementers: Assume that the thread always has changes whenever this method is called</para>
	/// </summary>
	/// <param name="threadUpdateInfo">Data object containing information about the thread's updates.</param>
	/// <returns>A list of images to be downloaded</returns>
	Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo);

	/// <summary>
	/// Executed when the Engine module has downloaded an image & thumbnail, to store the image somewhere and mark it in a possible database.
	/// </summary>
	/// <param name="queuedImageDownload">The queued image download that was performed.</param>
	/// <param name="imageTempFilename">A path to a temporary file containing the media data.</param>
	/// <param name="thumbTempFilename">A path to a temporary file containing the thumbnail data.</param>
	Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename);

	/// <summary>
	/// Executed when a thread has been pruned or deleted, to mark it as complete.
	/// </summary>
	/// <param name="threadId">The ID of the thread that has been completed.</param>
	/// <param name="board">The board of the thread.</param>
	/// <param name="deleted">True if the thread was deleted, otherwise false if archived.</param>
	Task ThreadUntracked(ulong threadId, string board, bool deleted);

	/// <summary>
	/// Returns a list of threads that are already stored in the consumer, to prevent re-scraping of them.
	/// </summary>
	/// <param name="threadIdsToCheck">The IDs of the threads to check if they have been previously consumed.</param>
	/// <param name="board">The board of the threads.</param>
	/// <param name="archivedOnly">True to only return threads that have been marked as completed, otherwise false to return all.</param>
	/// <param name="getMetadata">True to return timestamps alongside the thread numbers, otherwise false to return minimum values.</param>
	/// <param name="excludeDeletedPosts">True to remove deleted posts from the output, otherwise false to return all posts.</param>
	/// <returns>A list of threads that are already stored in the consumer.</returns>
	Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, MetadataMode metadataMode = MetadataMode.FullHashMetadata,
		bool excludeDeletedPosts = true);

	/// <summary>
	/// Calculates a 32-bit hash for a post, using its mutable properties. Used for detecting post changes within a thread.
	/// </summary>
	/// <param name="post">The post to calculate a hash for</param>
	/// <returns>The hash of the post</returns>
	uint CalculateHash(Post post);
}

public enum MetadataMode
{
	ThreadIdOnly,
	ThreadIdAndPostId,
	FullHashMetadata
}

public struct ExistingThreadInfo
{
	public ulong ThreadId;
	public bool Archived;
	public DateTimeOffset LastPostTime;
	public IReadOnlyCollection<(ulong PostId, uint PostHash)> PostHashes;

	public ExistingThreadInfo(ulong threadId)
	{
		ThreadId = threadId;
		Archived = false;
		LastPostTime = DateTimeOffset.MinValue;
		PostHashes = null;
	}

	public ExistingThreadInfo(ulong threadId, bool archived, DateTimeOffset lastPostTime, IReadOnlyCollection<(ulong PostId, uint PostHash)> postHashes)
	{
		ThreadId = threadId;
		Archived = archived;
		LastPostTime = lastPostTime;
		PostHashes = postHashes;
	}
}

public struct ThreadUpdateInfo
{
	public ThreadPointer ThreadPointer;
	public Thread Thread;

	/// <summary>
	/// True if this thread is the first time Hayden has encountered it (and it does not exist in the backend), otherwise false.
	/// </summary>
	public bool IsNewThread;

	/// <summary>
	/// A collection of posts that have not been added to the database yet.
	/// </summary>
	public ICollection<Post> NewPosts;

	/// <summary>
	/// A collection of posts that have been modified since the last time they were committed to the backend.
	/// </summary>
	public ICollection<Post> UpdatedPosts;

	/// <summary>
	/// A collection of post numbers that exist in the backend, but are no longer present in the thread (i.e. they have been deleted).
	/// </summary>
	public ICollection<ulong> DeletedPosts;

	/// <summary>
	/// Calculates if the thread has actually had any changes made.
	/// </summary>
	public bool HasChanges => NewPosts.Count + UpdatedPosts.Count + DeletedPosts.Count > 0;

	public ThreadUpdateInfo(in ThreadPointer threadPointer, Thread thread, bool isNewThread)
	{
		ThreadPointer = threadPointer;
		Thread = thread;
		IsNewThread = isNewThread;
		NewPosts = new List<Post>();
		UpdatedPosts = new List<Post>();
		DeletedPosts = new List<ulong>();
	}

	public ThreadUpdateInfo(in ThreadPointer threadPointer, Thread thread, bool isNewThread, ICollection<Post> newPosts, ICollection<Post> updatedPosts, ICollection<ulong> deletedPosts)
	{
		ThreadPointer = threadPointer;
		Thread = thread;
		IsNewThread = isNewThread;
		NewPosts = newPosts;
		UpdatedPosts = updatedPosts;
		DeletedPosts = deletedPosts;
	}
}