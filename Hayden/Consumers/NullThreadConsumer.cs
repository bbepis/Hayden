using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class NullThreadConsumer : IThreadConsumer
	{
		public Task ConsumeThread(Thread thread, string board)
			=> Task.CompletedTask;

		public Task ThreadUntracked(ulong threadId, string board)
			=> Task.CompletedTask;

		public Task<ulong[]> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly)
			=> Task.FromResult(new ulong[0]);

		public void Dispose() { }
	}
}