using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.ImportExport;
using Hayden.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public class ImportArchiver : BoardArchiver
	{
		protected IImporter Importer { get; }
		protected IForwardOnlyImporter ForwardOnlyImporter { get; }

		protected override bool LoopArchive => false;
		protected override bool NeedsToDelayThreadApiCall => false;

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Importer");

		public ImportArchiver(IServiceProvider serviceProvider, SourceConfig sourceConfig, ConsumerConfig consumerConfig,
			IThreadConsumer threadConsumer, IFileSystem fileSystem, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		: base(sourceConfig, consumerConfig, null, threadConsumer, fileSystem, stateStore, proxyProvider)
		{
			Importer = serviceProvider.GetService<IImporter>();
			ForwardOnlyImporter = serviceProvider.GetService<IForwardOnlyImporter>();

			if (Importer == null && ForwardOnlyImporter == null)
				throw new InvalidOperationException("Requires either a valid IImporter or IForwardOnlyImporter instance");
		}

		public override async Task Execute(CancellationToken token)
		{
			_ = Task.Run(() => ReportingTask(CancellationToken.None));

			int readParallelism = 20;
			int writeParallelism = 100;

			var threadChannel = Channel.CreateBounded<(ThreadPointer, Thread)>(1000);

			var readerTask = Task.Run(async () =>
				{
					await foreach (var thread in ReadThreads(token, readParallelism))
					{
						if (thread.Item2 == null)
							continue;

						await threadChannel.Writer.WriteAsync(thread, token);
					}
				})
				.ContinueWith(task => threadChannel.Writer.Complete(task.Exception));

			var writerTask = Parallel.ForEachAsync(threadChannel.Reader.ReadAllAsync(token), new ParallelOptions{
				MaxDegreeOfParallelism = writeParallelism,
				CancellationToken = token
			}, async (thread, token) =>
			{
				try
				{
					var threadInfo = await ThreadConsumer.CheckExistingThread(thread.Item1.ThreadId, thread.Item1.Board,
						ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative ? MetadataMode.FullHashMetadata : MetadataMode.ThreadIdAndPostId,
						false);

					var trackedThread = TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, threadInfo);
					var updateInfo = trackedThread.ProcessThreadUpdates(thread.Item1, thread.Item2, ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative);

					await ThreadConsumer.ConsumeThread(updateInfo);

					Interlocked.Increment(ref LastProgressThreadsProcessed);
					Interlocked.Add(ref LastProgressPostsProcessed, updateInfo.NewPosts.Count);
				}
				catch (Exception ex)
				{
					Logger.Error(ex, "Failed to write thread");
				}
			});

			await Task.WhenAll(readerTask, writerTask);
		}

		protected IAsyncEnumerable<(ThreadPointer, Thread)> ReadThreads(CancellationToken token, int parallelism)
		{
			if (ForwardOnlyImporter != null)
				return ForwardOnlyImporter.RetrieveThreads(SourceConfig.Boards.Keys.ToArray());

			async IAsyncEnumerable<(ThreadPointer, Thread)> InnerEnumerable()
			{
				foreach (var board in SourceConfig.Boards.Keys)
				{
					var threadQueue = new List<ThreadPointer>();

					await foreach (var pointer in Importer.GetThreadList(board).WithCancellation(token))
						threadQueue.Add(pointer);

					Logger.Information("Found {threadCount:N0} threads for board /{board}/", threadQueue.Count, board);

					var threadChannel = Channel.CreateBounded<(ThreadPointer, Thread)>(1000);

					var parallelismTask = Parallel.ForEachAsync(threadQueue, new ParallelOptions
						{
							CancellationToken = token,
							MaxDegreeOfParallelism = parallelism
						}, async (threadPointer, token) =>
						{
							var thread = await Importer.RetrieveThread(threadPointer);

							if (thread == null)
								return;

							await threadChannel.Writer.WriteAsync((threadPointer, thread), token);
						})
						.ContinueWith(task => threadChannel.Writer.Complete(task.Exception));

					await foreach (var thread in threadChannel.Reader.ReadAllAsync(token))
						yield return thread;
				}
			}

			return InnerEnumerable();
		}

		private DateTime LastProgressTime = DateTime.UtcNow;
		private long LastProgressThreadsProcessed = 0;
		private long LastProgressPostsProcessed = 0;
		private long TotalThreadsProcessed = 0;
		private long TotalPostsProcessed = 0;

		protected override void ReportProgress(ThreadPointer completedThread, ThreadUpdateTaskResult result, int enqueuedImageCount, int newCompletedCount, int? totalThreadCount)
		{
			Interlocked.Increment(ref LastProgressThreadsProcessed);
			Interlocked.Add(ref LastProgressPostsProcessed, result.PostCountChange);

			if (result.Status == ThreadUpdateStatus.Error || result.Status == ThreadUpdateStatus.Deleted)
				base.ReportProgress(completedThread, result, enqueuedImageCount, newCompletedCount, totalThreadCount);
		}

		private async Task ReportingTask(CancellationToken token)
		{
			const int waitTime = 5;

			try
			{
				while (!token.IsCancellationRequested)
				{
					await Task.Delay(TimeSpan.FromSeconds(waitTime));

					if (token.IsCancellationRequested)
						break;

					var sinceThreadsProcessed = Interlocked.Exchange(ref LastProgressThreadsProcessed, 0);
					var sincePostsProcessed = Interlocked.Exchange(ref LastProgressPostsProcessed, 0);

					TotalThreadsProcessed += sinceThreadsProcessed;
					TotalPostsProcessed += sincePostsProcessed;

					var timeSince = DateTime.UtcNow - LastProgressTime;
					var threadsPerSecond = Math.Round(sinceThreadsProcessed / timeSince.TotalSeconds);
					var postsPerSecond = Math.Round(sincePostsProcessed / timeSince.TotalSeconds);

					Logger.Information($"{$"{TotalThreadsProcessed:N0}t",-5} / {$"{TotalPostsProcessed:N0}p",-5} ({$"+{sinceThreadsProcessed:N0}t",-5}, {threadsPerSecond:N0}t/s) ({$"+{sincePostsProcessed:N0}p",-5}, {postsPerSecond:N0}p/s)");

					LastProgressTime = DateTime.UtcNow;
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Reporting task failure");
			}
		}
	}
}