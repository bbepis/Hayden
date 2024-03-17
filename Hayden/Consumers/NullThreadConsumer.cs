using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class NullThreadConsumer : IThreadConsumer
	{
		public Task InitializeAsync() => Task.CompletedTask;

		public Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
			=> Task.FromResult<IList<QueuedImageDownload>>(new List<QueuedImageDownload>());

		public Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
			=> Task.CompletedTask;

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
			=> Task.CompletedTask;

		public Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
			=> Task.FromResult<ICollection<ExistingThreadInfo>>(new List<ExistingThreadInfo>());

		public uint CalculateHash(Post post)
			=> 0;

		public void Dispose() { }
	}
}