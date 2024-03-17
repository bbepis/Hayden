using System;
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
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
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

		private MultiDictionary<ThreadPointer, Thread> ThreadCacheDictionary { get; } = new();


		private async IAsyncEnumerable<ThreadPointer> ForwardOnlyEnumerate()
		{
			await foreach (var threadBatch in ForwardOnlyImporter.RetrieveThreads(SourceConfig.Boards.Keys.ToArray()).Batch(1000))
			{
				foreach (var board in SourceConfig.Boards.Keys)
				{
					if (!threadBatch.Any(x => x.Item1.Board == board))
						continue;

					// Check for threads that have already been downloaded by the consumer, noting the last time they were downloaded.
					var existingThreads = await ThreadConsumer.CheckExistingThreads(threadBatch.Where(x => x.Item1.Board == board).Select(x => x.Item1.ThreadId),
						board,
						false,
						ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative ? MetadataMode.FullHashMetadata : MetadataMode.ThreadIdAndPostId,
						false);

					lock (TrackedThreads)
						foreach (var existingThread in existingThreads)
						{
							TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] =
								TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existingThread);
						}
				}

				lock (ThreadCacheDictionary)
				{
					foreach (var thread in threadBatch)
						ThreadCacheDictionary.Add(thread.Item1, thread.Item2);
				}
				
				foreach (var thread in threadBatch)
					yield return thread.Item1;
			}
		}

		protected override async Task<MaybeAsyncEnumerable<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
		{
			if (ForwardOnlyImporter != null)
			{
				var asyncThreadQueue = new AsyncProducerConsumerQueue<ThreadPointer>(1000);

				_ = Task.Run(async () =>
				{
					try
					{
						await foreach (var threadPointer in ForwardOnlyEnumerate())
							await asyncThreadQueue.EnqueueAsync(threadPointer, token);
					}
					catch (Exception ex)
					{
						Logger.Error(ex, "Failed when enumerating over source material");
					}
					finally
					{
						asyncThreadQueue.CompleteAdding();
					}
				});
				
				return new MaybeAsyncEnumerable<ThreadPointer>(asyncThreadQueue.GetAsyncEnumerable());
			}

			var threadQueue = new List<ThreadPointer>();
			var stopwatch = new System.Diagnostics.Stopwatch();
			stopwatch.Start();

			foreach (var board in SourceConfig.Boards.Keys)
			await foreach (var pointer in Importer.GetThreadList(board).WithCancellation(token))
				threadQueue.Add(pointer);

			Logger.Debug("Read thread list in {time}", stopwatch.Elapsed);

			if (threadQueue.Count < 10_000) // super memory-inefficient at this size
			{
				foreach (var board in SourceConfig.Boards.Keys)
				{
					// Check for threads that have already been downloaded by the consumer, noting the last time they were downloaded.
					var existingThreads = await ThreadConsumer.CheckExistingThreads(threadQueue.Where(x => x.Board == board).Select(x => x.ThreadId),
						board,
						false,
						ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative ? MetadataMode.FullHashMetadata : MetadataMode.ThreadIdAndPostId,
						false);

					lock (TrackedThreads)
						foreach (var existingThread in existingThreads)
						{
							TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] =
								TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash, existingThread);
						}
				}

				return new MaybeAsyncEnumerable<ThreadPointer>(threadQueue);
			}
			else
			{
				async IAsyncEnumerable<ThreadPointer> internalEnumerate()
				{
					AsyncProducerConsumerQueue<ICollection<ThreadPointer>> pointerQueue = new(1);

					var producerTask = Task.Run(async () =>
					{
						try
						{
							foreach (var board in SourceConfig.Boards.Keys)
							{
								if (token.IsCancellationRequested)
									break;

								stopwatch.Restart();
								foreach (var pointerBatch in threadQueue.Where(x => x.Board == board).Batch(10000))
								{
									if (token.IsCancellationRequested)
										break;

									Logger.Debug("Read thread batch in {time}", stopwatch.Elapsed);

									stopwatch.Restart();

									var existingThreads = await ThreadConsumer.CheckExistingThreads(
										pointerBatch.Select(x => x.ThreadId),
										board,
										false,
										ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative ? MetadataMode.FullHashMetadata : MetadataMode.ThreadIdAndPostId,
										false);

									Logger.Debug("Read existing threads in {time}", stopwatch.Elapsed);
									stopwatch.Restart();

									lock (TrackedThreads)
									{
										foreach (var existingThread in existingThreads)
										{
											TrackedThreads[new ThreadPointer(board, existingThread.ThreadId)] =
												TrackedThread.StartTrackingThread(ThreadConsumer.CalculateHash,
													existingThread);
										}
									}

									Logger.Debug("Set tracked threads in {time}", stopwatch.Elapsed);

									try
									{
										await pointerQueue.EnqueueAsync(pointerBatch, token);
									}
									catch (OperationCanceledException)
									{
										break;
									}

									stopwatch.Restart();
								}
							}
						}
						catch (Exception ex)
						{
							Logger.Error(ex, "Error while importing");
						}

						pointerQueue.CompleteAdding();
					});

					while (await pointerQueue.OutputAvailableAsync(token))
					{
						foreach (var item in await pointerQueue.DequeueAsync(token))
							yield return item;
					}
				}

				return new MaybeAsyncEnumerable<ThreadPointer>(internalEnumerate());
			}
		}

		protected override async Task<ApiResponse<Thread>> RetrieveThreadAsync(ThreadPointer threadPointer, HttpClientProxy client, CancellationToken token)
		{
			Thread thread;

			if (ForwardOnlyImporter != null)
			{
				lock (ThreadCacheDictionary)
				{
					thread = ThreadCacheDictionary.PopValue(threadPointer);
				}
			}
			else
			{
				thread = await Importer.RetrieveThread(threadPointer);
			}

			return new ApiResponse<Thread>(thread != null ? ResponseType.Ok : ResponseType.NotFound, thread);
		}
	}
}