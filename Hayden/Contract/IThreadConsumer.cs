using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Models;

namespace Hayden.Contract
{
	/// <summary>
	/// An interface for consuming and storing threads from the archiver process.
	/// </summary>
	public interface IThreadConsumer : IDisposable
	{
		/// <summary>
		/// Consumes a thread, and returns a list of images to be downloaded.
		/// </summary>
		/// <param name="thread">The thread to consume.</param>
		/// <param name="board">The board that the thread belongs to.</param>
		/// <returns>A list of images to be downloaded</returns>
		Task<IList<QueuedImageDownload>> ConsumeThread(Thread thread, string board);

		/// <summary>
		/// Executed when a thread has been pruned or deleted, to mark it as complete.
		/// </summary>
		/// <param name="threadId">The ID of the thread that has been completed.</param>
		/// <param name="board">The board of the thread.</param>
		/// <param name="deleted">True if the thread was deleted, otherwise false if pruned.</param>
		Task ThreadUntracked(ulong threadId, string board, bool deleted);

		/// <summary>
		/// Returns a list of threads that are already stored in the consumer, to prevent re-scraping of them.
		/// </summary>
		/// <param name="threadIdsToCheck">The IDs of the threads to check if they have been previously consumed.</param>
		/// <param name="board">The board of the threads.</param>
		/// <param name="archivedOnly">True to only return threads that have been marked as completed, otherwise false to return all.</param>
		/// <param name="getTimestamps">True to return timestamps alongside the thread numbers, otherwise false to return minimum values.</param>
		/// <returns>A list of threads that are already stored in the consumer.</returns>
		Task<ICollection<(ulong threadId, DateTimeOffset lastPostTime)>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getTimestamps = true);
	}
}