using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Proxy;
using Serilog;

namespace Hayden;

public class InteractiveSettings
{
	public string[] ThreadUrls { get; set; }
}

public class InteractiveArchiver : BoardArchiver
{
	protected override bool LoopArchive => false;

	// TODO: replace with TrackedThreads integration
	private List<ThreadPointer> TrackedPointers;

	public InteractiveArchiver(InteractiveSettings interactiveSettings,
		SourceConfig sourceConfig,
		ConsumerConfig consumerConfig,
		IFrontendApi frontendApi,
		IThreadConsumer threadConsumer,
		IFileSystem fileSystem,
		IStateStore stateStore = null,
		ProxyProvider proxyProvider = null)
		: base(sourceConfig, consumerConfig, frontendApi, threadConsumer, fileSystem, stateStore, proxyProvider)
	{
		TrackedPointers = interactiveSettings.ThreadUrls.Select(x =>
			{
				var match = Regex.Match(x, @"boards\.4chan\.org/([^/]+)/thread/(\d+)");

				if (match.Success)
				{
					return (ThreadPointer?)new ThreadPointer(match.Groups[1].Value, ulong.Parse(match.Groups[2].Value));
				}
				else
				{
					return null;
				}
			})
			.Where(x => x.HasValue)
			.Select(x => x.Value)
			.ToList();

		Log.Information("Read {threadCount} threads from arguments", TrackedPointers.Count);
	}

	protected override void HandleThreadRemoval(ThreadPointer threadPointer)
	{
		base.HandleThreadRemoval(threadPointer);

		TrackedPointers.Remove(threadPointer);
	}

	public override async Task Execute(CancellationToken token)
	{
		bool firstRun = true;

		// We only loop if cancellation has not been requested (i.e. "Q" has not been pressed)
		// Every time you see "token" mentioned, its performing a check

		MaybeAsyncEnumerable<ThreadPointer> queuedThreads = new MaybeAsyncEnumerable<ThreadPointer>([]);
		var queuedImages = new List<QueuedImageDownload>();

		while (!token.IsCancellationRequested)
		{
			var boardsToCheck = TrackedPointers.Select(x => x.Board).Distinct().ToArray();

			foreach (var board in boardsToCheck)
			{
				var threads = await (await GetBoardThreads(token, board, firstRun, true)).ToArrayAsync(cancellationToken: token);
				threads = threads.Intersect(TrackedPointers).ToArray();

				var nonExistent = TrackedPointers.Where(x => x.Board == board).Except(threads);

				foreach (var toDelete in nonExistent)
				{

					TrackedPointers.Remove(toDelete);
				}

				queuedThreads.Append(threads);
			}

			Log.Information("{queuedThreadsCount} threads have been queued total", queuedThreads.Count);
			Log.Information("{activeThreadsCount} threads remain active", TrackedPointers.Count);

			var (requeuedThreads, requeuedImages) = await PerformScrape(firstRun, queuedThreads, queuedImages, token);

			if (requeuedImages == null)
				break;

			queuedThreads = new MaybeAsyncEnumerable<ThreadPointer>(requeuedThreads);
			queuedImages = requeuedImages;

			firstRun = false;

			if (TrackedPointers.Count == 0 && queuedThreads.Count == 0 && queuedImages.Count == 0)
				break;
		}
	}
}