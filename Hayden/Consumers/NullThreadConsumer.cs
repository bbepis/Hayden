using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;

namespace Hayden.Consumers
{
	public class NullThreadConsumer<TThread, TPost>
	{
		public Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo<TThread, TPost> threadUpdateInfo)
			=> Task.FromResult<IList<QueuedImageDownload>>(new List<QueuedImageDownload>());

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
			=> Task.CompletedTask;

		public Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getMetadata = true)
			=> Task.FromResult<ICollection<ExistingThreadInfo>>(new List<ExistingThreadInfo>());

		public void Dispose() { }
	}
}