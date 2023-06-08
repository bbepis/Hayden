using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Contract;
using Hayden.ImportExport;
using Hayden.Proxy;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public class ImportArchiver : BoardArchiver
	{
		protected IImporter Importer { get; }

		protected override bool LoopArchive => false;
		protected override bool NeedsToDelayThreadApiCall => false;

		public ImportArchiver(IImporter importer, SourceConfig sourceConfig, ConsumerConfig consumerConfig,
			IThreadConsumer threadConsumer, IFileSystem fileSystem, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		: base(sourceConfig, consumerConfig, null, threadConsumer, fileSystem, stateStore, proxyProvider)
		{
			Importer = importer;
		}
		
		protected override async Task<MaybeAsyncEnumerable<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
		{
			var threadQueue = new List<ThreadPointer>();

			foreach (var board in SourceConfig.Boards.Keys)
			{
				await foreach (var pointer in Importer.GetThreadList(board).WithCancellation(token))
					threadQueue.Add(pointer);

				// Check for threads that have already been downloaded by the consumer, noting the last time they were downloaded.
				var existingThreads = await ThreadConsumer.CheckExistingThreads(threadQueue.Where(x => x.Board == board).Select(x => x.ThreadId),
					board,
					false,
					true,
					false);

				foreach (var existingThread in existingThreads)
				{
					//var thread = threadQueue.First(x => x.Board == board && x.ThreadId == existingThread.ThreadId);

					//// Only remove threads to be downloaded if the downloaded thread is already up-to-date by comparing last post times
					//// This can't be done below as "last post time" is different to "last modified time"
					//if (thread.LastModified <= Utility.GetGMTTimestamp(existingThread.LastPostTime))
					//{
					   // threadList.Remove(thread);
					//}

					// Start tracking the thread

					lock (TrackedThreads)
						TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] =
							TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existingThread);
				}
			}
			
			return new MaybeAsyncEnumerable<ThreadPointer>(threadQueue);
		}

		protected override async Task<ApiResponse<Thread>> RetrieveThreadAsync(ThreadPointer threadPointer, HttpClientProxy client, CancellationToken token)
		{
			return new ApiResponse<Thread>(ResponseType.Ok, await Importer.RetrieveThread(threadPointer));
		}
	}
}