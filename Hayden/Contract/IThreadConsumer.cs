using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Models;

namespace Hayden.Contract
{
	public interface IThreadConsumer : IDisposable
	{
		Task<IList<QueuedImageDownload>> ConsumeThread(Thread thread, string board);

		Task ThreadUntracked(ulong threadId, string board, bool deleted);

		Task<ICollection<(ulong threadId, DateTime lastPostTime)>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getTimestamps = true);
	}
}
