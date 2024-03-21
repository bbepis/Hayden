using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Contract;
using Hayden.ImportExport;
using Hayden.Models;
using Hayden.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Nito.AsyncEx;
using Serilog;
using ZstdSharp;
using Thread = Hayden.Models.Thread;

namespace Hayden;

public class ExportSettings
{
	public string OutputFile { get; set; }
}

public class ExportArchiver : BoardArchiver
{
	protected IImporter Importer { get; }
	protected IForwardOnlyImporter ForwardOnlyImporter { get; }

	protected override bool LoopArchive => false;
	protected override bool ForceSingleRun { get; } = false;

	protected override bool NeedsToDelayThreadApiCall => false;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Importer");

	public ExportArchiver(IServiceProvider serviceProvider, SourceConfig sourceConfig,
		ExportSettings exportSettings, IFileSystem fileSystem, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		: base(sourceConfig, GetConsumerConfig(), null, CreateExporter(exportSettings.OutputFile), fileSystem, stateStore, proxyProvider)
	{
		Importer = serviceProvider.GetService<IImporter>();
		ForwardOnlyImporter = serviceProvider.GetService<IForwardOnlyImporter>();

		sourceConfig.ApiDelay = 0;
		sourceConfig.BoardScrapeDelay = 5;

		if (Importer == null && ForwardOnlyImporter == null)
			throw new InvalidOperationException("Requires either a valid IImporter or IForwardOnlyImporter instance");

		ForceSingleRun = Importer == null && ForwardOnlyImporter != null;
	}

	private static ConsumerConfig GetConsumerConfig()
	{
		return new ConsumerConfig()
		{
			ConsolidationMode = ConsolidationMode.Authoritative,
			DatabaseType = null,
			FullImagesEnabled = false,
			ThumbnailsEnabled = false,
			DownloadLocation = "."
		};
	}

	private static JsonExporter CreateExporter(string filename)
	{
		if (filename.EndsWith(".json.zst"))
			return new JsonExporter(filename, 10);

		if (filename.EndsWith(".json"))
			return new JsonExporter(filename);

		throw new Exception("Expected .json.zst or .json file");
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
		Task.Run(() => ReportingThread(CancellationToken.None));

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

		return new MaybeAsyncEnumerable<ThreadPointer>(internalEnumerate(), threadQueue.Count);
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

	private DateTime LastProgressTime = DateTime.UtcNow;
	private int LastProgressThreadsProcessed = 0;
	private int LastProgressPostsProcessed = 0;
	private int TotalThreadsProcessed = 0;
	private int TotalPostsProcessed = 0;

	protected override void ReportProgress(ThreadPointer completedThread, ThreadUpdateTaskResult result, int enqueuedImageCount, int newCompletedCount, int? totalThreadCount)
	{
		Interlocked.Increment(ref LastProgressThreadsProcessed);
		Interlocked.Add(ref LastProgressPostsProcessed, result.PostCountChange);

		if (result.Status == ThreadUpdateStatus.Error)
			base.ReportProgress(completedThread, result, enqueuedImageCount, newCompletedCount, totalThreadCount);
	}

	private async Task ReportingThread(CancellationToken token)
	{
		const int waitTime = 5;

		while (token.IsCancellationRequested)
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

			Log.Information($"{"[Thread]",-9} {$"{TotalThreadsProcessed}t",-5} / {$"{TotalPostsProcessed}p",-5} ({$"+{sinceThreadsProcessed}t",-5}, {threadsPerSecond}/s) ({$"+{sincePostsProcessed}t",-5}, {postsPerSecond}/s)");

			LastProgressTime = DateTime.UtcNow;
		}
	}

	private class JsonExporter : IThreadConsumer
	{
		private FileStream FileStream { get; set; }
		private CompressionStream ZstdStream { get; set; }
		private JsonTextWriter JsonWriter { get; set; }

		private bool IsDisposed { get; set; } = false;

		private static JsonSerializer JsonSerializer { get; set; } = new JsonSerializer
		{
			Formatting = Formatting.None,
			NullValueHandling = NullValueHandling.Ignore,
			DefaultValueHandling = DefaultValueHandling.Include,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			Converters =
			{
				new StringEnumConverter(new DefaultNamingStrategy(), false)
			}
		};

		public JsonExporter(string filename, int? compressionLevel = null)
		{
			FileStream = new FileStream(filename, FileMode.Create);
		
			if (compressionLevel != null)
				ZstdStream = new CompressionStream(FileStream, compressionLevel.Value, leaveOpen: false);

			JsonWriter = new JsonTextWriter(new StreamWriter((Stream)ZstdStream ?? FileStream))
			{
				Formatting = Formatting.None,
				DateTimeZoneHandling = DateTimeZoneHandling.Utc
			};

			JsonWriter.WriteStartArray();
		}

		public Task InitializeAsync() => Task.CompletedTask;

		public Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			lock (JsonSerializer)
				JsonSerializer.Serialize(JsonWriter, threadUpdateInfo.Thread);

			return Task.FromResult((IList<QueuedImageDownload>)Array.Empty<QueuedImageDownload>());
		}

		public Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
		{
			throw new NotImplementedException();
		}

		public Task ThreadUntracked(ulong threadId, string board, bool deleted) => Task.CompletedTask;

		public Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly,
			MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
		{
			return Task.FromResult((ICollection<ExistingThreadInfo>)Array.Empty<ExistingThreadInfo>());
		}

		public uint CalculateHash(Post post) => 0;

		public void Dispose()
		{
			if (IsDisposed)
				return;

			JsonWriter.WriteEndArray();

			((IDisposable)JsonWriter).Dispose();
			ZstdStream?.Dispose();
			FileStream.Dispose();

			IsDisposed = true;
		}
	}
}