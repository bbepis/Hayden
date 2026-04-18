using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.ImportExport;
using Hayden.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;
using ZstdSharp;
using Thread = Hayden.Models.Thread;

namespace Hayden;

public class ExportSettings
{
	public string OutputFile { get; set; }
	public int? CompressionLevel { get; set; }
}

public class ExportArchiver : IArchiver
{
	protected IImporter Importer { get; }
	protected IForwardOnlyImporter ForwardOnlyImporter { get; }

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Exporter");

	private SourceConfig SourceConfig { get; set; }
	private ExportSettings ExportSettings { get; set; }

	public ExportArchiver(IServiceProvider serviceProvider, SourceConfig sourceConfig, ExportSettings exportSettings)
	{
		SourceConfig = sourceConfig;
		ExportSettings = exportSettings;
		Importer = serviceProvider.GetService<IImporter>();
		ForwardOnlyImporter = serviceProvider.GetService<IForwardOnlyImporter>();

		if (Importer == null && ForwardOnlyImporter == null)
			throw new InvalidOperationException("Requires either a valid IImporter or IForwardOnlyImporter instance");
	}

	private static JsonExporter CreateExporter(ExportSettings exportSettings)
	{
		if (exportSettings.OutputFile.EndsWith(".json.zst"))
			return new JsonExporter(exportSettings.OutputFile, exportSettings.CompressionLevel);

		if (exportSettings.OutputFile.EndsWith(".json") || exportSettings.OutputFile == "-")
			return new JsonExporter(exportSettings.OutputFile);

		throw new Exception("Expected .json.zst or .json file");
	}

	public Task Initialize() => Task.CompletedTask;

	public async Task Execute(CancellationToken token)
	{
		_ = Task.Run(() => ReportingTask(CancellationToken.None));

		var threadChannel = Channel.CreateBounded<(ThreadPointer, Thread)>(1000);

		var readerTask = Task.Run(async () =>
		{
			await foreach (var thread in ReadThreads(token, 20))
			{
				if (thread.Item2 == null)
					continue;

				await threadChannel.Writer.WriteAsync(thread, token);
			}
		})
			.ContinueWith(task => threadChannel.Writer.Complete(task.Exception));

		var writerTask = Task.Run(async () =>
		{
			using var exporter = CreateExporter(ExportSettings);

			await foreach (var (pointer, thread) in threadChannel.Reader.ReadAllAsync(token))
			{
				await exporter.ConsumeThread(pointer, thread, false, false);

				Interlocked.Increment(ref LastProgressThreadsProcessed);
				Interlocked.Add(ref LastProgressPostsProcessed, thread.Posts.Length);
			}
		});

		await Task.WhenAll(readerTask, writerTask);
	}

	protected IAsyncEnumerable<(ThreadPointer, Thread)> ReadThreads(CancellationToken token, int parallelism)
	{
		if (ForwardOnlyImporter != null)
			return ForwardOnlyImporter.RetrieveThreads(SourceConfig.Boards.Select(x => x.Board).ToArray());

		string[] boardList = SourceConfig.Boards != null && SourceConfig.Boards.Length > 0
			? SourceConfig.Boards.Select(x => x.Board).ToArray()
			: Importer.GetBoardList().Result;

		async IAsyncEnumerable<(ThreadPointer, Thread)> InnerEnumerable()
		{
			foreach (var board in boardList)
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
					await threadChannel.Writer.WriteAsync((threadPointer, await Importer.RetrieveThread(threadPointer)), token);
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

	//protected void ReportProgress(ThreadPointer completedThread, ThreadUpdateTaskResult result, int enqueuedImageCount, int newCompletedCount, int? totalThreadCount)
	//{

	//	if (result.Status == ThreadUpdateStatus.Error || result.Status == ThreadUpdateStatus.Deleted)
	//		base.ReportProgress(completedThread, result, enqueuedImageCount, newCompletedCount, totalThreadCount);
	//}

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

	public void Dispose() { }

	private class JsonExporter : IThreadConsumer
	{
		private Stream FileStream { get; set; }
		private CompressionStream ZstdStream { get; set; }
		private Utf8JsonWriter JsonWriter { get; set; }

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Exporter");

		private Task ConsumerTask { get; set; }
		
		private Channel<DumpedThread> ThreadChannel { get; set; } = Channel.CreateBounded<DumpedThread>(new BoundedChannelOptions(32)
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
			if (filename == "-")
			{
				FileStream = Console.OpenStandardOutput();
			}
			else
			{
				FileStream = new FileStream(filename, FileMode.Create);

				if (compressionLevel != null)
					ZstdStream = new CompressionStream(FileStream, compressionLevel.Value, leaveOpen: false);
			}

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

		public Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo, bool downloadFullImages, bool downloadThumbnails)
			=> ConsumeThread(threadUpdateInfo.ThreadPointer, threadUpdateInfo.Thread, downloadFullImages, downloadThumbnails);

		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadPointer pointer, Thread thread, bool downloadFullImages, bool downloadThumbnails)
		{
			if (IsDisposed)
				throw new Exception("Consumer disposed");

			thread.OriginalObject = null;

			if (thread.AdditionalMetadata != null && !JObject.FromObject(thread.AdditionalMetadata, Common.LeanSerializer).HasValues)
				thread.AdditionalMetadata = null;

			foreach (var post in thread.Posts)
			{
				post.OriginalObject = null;

				if (post.AdditionalMetadata != null)
				{
					if (!JObject.FromObject(post.AdditionalMetadata, Common.LeanSerializer).HasValues)
						post.AdditionalMetadata = null;
				}


				if (post.Media != null)
				{
					if (post.Media.Length == 0)
						post.Media = null;
					else
						foreach (var media in post.Media)
						{
							if (media.AdditionalMetadata == null)
								continue;

							if (!JObject.FromObject(media.AdditionalMetadata, Common.LeanSerializer).HasValues)
							{
								media.AdditionalMetadata = null;
							}
						}
				}
			}

			await ThreadChannel.Writer.WriteAsync(DumpedThread.Create(thread, pointer.Board));

			return Array.Empty<QueuedImageDownload>();
		}

		public Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
		{
			throw new NotImplementedException();
		}

		public Task ThreadUntracked(ulong threadId, string board, DateTimeOffset? timeDeleted, DateTimeOffset? timeArchived) => Task.CompletedTask;

		public Task<IList<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly,
			MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
		{
			return Task.FromResult((IList<ExistingThreadInfo>)Array.Empty<ExistingThreadInfo>());
		}

		public Task<ExistingThreadInfo> CheckExistingThread(ulong threadId, string board, MetadataMode metadataMode = MetadataMode.FullHashMetadata,
			bool excludeDeletedPosts = true)
		{
			throw new NotImplementedException();
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