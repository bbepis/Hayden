using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class NullThreadConsumer : IThreadConsumer
	{
		public Task<IList<QueuedImageDownload>> ConsumeThread(Thread thread, string board)
			=> Task.FromResult((IList<QueuedImageDownload>)new QueuedImageDownload[0]);

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
			=> Task.CompletedTask;

		public Task<ICollection<(ulong threadId, DateTimeOffset lastPostTime)>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getTimestamps = true)
			=> Task.FromResult((ICollection<(ulong threadId, DateTimeOffset lastPostTime)>)new (ulong threadId, DateTimeOffset lastPostTime)[0]);

		public void Dispose() { }
	}
}