using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Contract;
using Hayden.ImportExport;
using Hayden.Models;
using Hayden.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using Serilog;
using ZstdSharp;
using Thread = Hayden.Models.Thread;

namespace Hayden;

public class ExportSettings
{
	public string OutputFile { get; set; }
	public int? CompressionLevel { get; set; }
}

public class ExportArchiver : BoardArchiver
{
	protected IImporter Importer { get; }
	protected IForwardOnlyImporter ForwardOnlyImporter { get; }

	protected override bool LoopArchive => false;
	protected override bool ForceSingleRun { get; } = false;

	protected override bool NeedsToDelayThreadApiCall => false;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Exporter");

	public ExportArchiver(IServiceProvider serviceProvider, SourceConfig sourceConfig,
		ExportSettings exportSettings, IFileSystem fileSystem, IStateStore stateStore = null, ProxyProvider proxyProvider = null)
		: base(sourceConfig, GetConsumerConfig(), null, CreateExporter(exportSettings), fileSystem, stateStore, proxyProvider)
	{
		Importer = serviceProvider.GetService<IImporter>();
		ForwardOnlyImporter = serviceProvider.GetService<IForwardOnlyImporter>();

		sourceConfig.ApiDelay = 0;
		sourceConfig.BoardScrapeDelay = 5;
		sourceConfig.SingleScan = true;

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

	private static JsonExporter CreateExporter(ExportSettings exportSettings)
	{
		if (exportSettings.OutputFile.EndsWith(".json.zst"))
			return new JsonExporter(exportSettings.OutputFile, exportSettings.CompressionLevel);

		if (exportSettings.OutputFile.EndsWith(".json"))
			return new JsonExporter(exportSettings.OutputFile);

		throw new Exception("Expected .json.zst or .json file");
	}

	private MultiDictionary<ThreadPointer, Thread> ThreadCacheDictionary { get; } = new();


	private async IAsyncEnumerable<ThreadPointer> ForwardOnlyEnumerate()
	{
		await foreach (var threadBatch in ForwardOnlyImporter.RetrieveThreads(SourceConfig.Boards.Keys.ToArray()).Batch(1000))
		{
			foreach (var thread in threadBatch)
				yield return thread.Item1;
		}
	}

	protected override async Task<MaybeAsyncEnumerable<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
	{
		_ = Task.Run(() => ReportingTask(CancellationToken.None));

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
		
		return new MaybeAsyncEnumerable<ThreadPointer>(threadQueue);
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

	private class JsonExporter : IThreadConsumer
	{
		private FileStream FileStream { get; set; }
		private CompressionStream ZstdStream { get; set; }
		private Utf8JsonWriter JsonWriter { get; set; }

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Exporter");

		private Task ConsumerTask { get; set; }
		
		private Channel<Thread> ThreadChannel { get; set; } = Channel.CreateBounded<Thread>(new BoundedChannelOptions(32)
		{
			AllowSynchronousContinuations = false,
		});

		private bool IsDisposed { get; set; } = false;

		private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
		{
			AllowTrailingCommas = true,
			IncludeFields = true,
			PropertyNamingPolicy = null,
			NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
			ReadCommentHandling = JsonCommentHandling.Skip,
			DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
			Converters =
			{
				new JsonStringEnumConverter(null, false)
			}
		};

		public JsonExporter(string filename, int? compressionLevel = null)
		{
			FileStream = new FileStream(filename, FileMode.Create);
		
			if (compressionLevel != null)
				ZstdStream = new CompressionStream(FileStream, compressionLevel.Value, leaveOpen: false);

			JsonWriter = new Utf8JsonWriter((Stream)ZstdStream ?? FileStream, new JsonWriterOptions
			{
				Indented = false
			});

			ConsumerTask = Task.Run(async () =>
			{
				try
				{
					await foreach (var thread in ThreadChannel.Reader.ReadAllAsync())
					{
						System.Text.Json.JsonSerializer.Serialize(JsonWriter, thread, serializerOptions);
					}
				}
				catch (Exception ex)
				{
					Logger.Error(ex, "JSON writer failure");
				}
			});

			JsonWriter.WriteStartArray();
		}

		public Task InitializeAsync()
		{
			return Task.CompletedTask;
		}

		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			if (IsDisposed)
				throw new Exception("Consumer disposed");

			var thread = threadUpdateInfo.Thread;
			thread.OriginalObject = null;

			foreach (var post in thread.Posts)
				post.OriginalObject = null;

			await ThreadChannel.Writer.WriteAsync(thread);

			return Array.Empty<QueuedImageDownload>();
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

			ThreadChannel.Writer.Complete();

			ConsumerTask.Wait();

			JsonWriter.WriteEndArray();

			((IDisposable)JsonWriter).Dispose();
			ZstdStream?.Dispose();
			FileStream.Dispose();

			IsDisposed = true;
		}
	}
}