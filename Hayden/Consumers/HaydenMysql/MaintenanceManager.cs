using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.IO.Abstractions;
using Serilog;
using System.Linq;
using System.IO;

namespace Hayden.Consumers.HaydenMysql;
internal class MaintenanceManager
{
	private DbContextOptions<HaydenDbContext> DbContextOptions { get; set; }
	private ConsumerConfig ConsumerConfig { get; set; }

	public MaintenanceManager(ConsumerConfig consumerConfig)
	{
		DbContextOptions = new DbContextOptionsBuilder<HaydenDbContext>()
			.SetupHaydenDb(consumerConfig: consumerConfig)
			.Options;

		ConsumerConfig = consumerConfig;
	}

	public async Task DeleteThreads(ThreadPointer[] threads)
	{
		using var context = new HaydenDbContext(DbContextOptions);

		var boards = await context.Boards.ToDictionaryAsync(x => x.ShortName, x => x);

		var deletedThreadCount = 0;
		var deletedPostCount = 0;
		//var deletedFileCount = 0;
		//var deletedFileSize = 0;

		foreach (var threadPointer in threads)
		{
			var boardId = boards[threadPointer.Board].Id;

			var thread = await context.Threads
				.FirstOrDefaultAsync(x => x.BoardId == boardId && x.ThreadId == threadPointer.ThreadId);

			if (thread == null)
			{
				Log.Warning($"Could not find thread {threadPointer}");
				continue;
			}

			var posts = await context.Posts
				.Where(x => x.BoardId == boardId && x.ThreadId == threadPointer.ThreadId)
				.ToArrayAsync();

			var postIds = posts.Select(x => x.PostId).ToArray();

			var mappings = await context.FileMappings
				.Where(x => x.BoardId == boardId && postIds.Contains(x.PostId))
				.ToArrayAsync();

			context.RemoveRange(mappings);
			await context.SaveChangesAsync();

			deletedPostCount += posts.Length;
			context.RemoveRange(posts);
			await context.SaveChangesAsync();

			deletedThreadCount++;
			context.RemoveRange(thread);
			await context.SaveChangesAsync();
		}

		Log.Information($"Deleted {deletedThreadCount:N0} threads / {deletedPostCount:N0} posts");
	}

	public async Task PurgeOrphanedFiles()
	{
		using var context = new HaydenDbContext(DbContextOptions);

		Log.Information($"Finding orphaned files...");

		var orphanedFiles = await context.Files
			.AsNoTracking()
			.Where(file => !context.FileMappings.Any(fm => file.Id == fm.FileId))
			.ToArrayAsync();
		
		Log.Information($"Found {orphanedFiles.Length:N0} files");

		foreach (var file in orphanedFiles)
		{
			var fullFilePath = HaydenThreadConsumer.CalculateFilename(
				ConsumerConfig.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);

			if (File.Exists(fullFilePath))
				File.Delete(fullFilePath);

			var thumbFilePath = HaydenThreadConsumer.CalculateFilename(
				ConsumerConfig.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

			if (File.Exists(thumbFilePath))
				File.Delete(thumbFilePath);

			context.Remove(file);
		}

		await context.SaveChangesAsync();

		var deletedMb = orphanedFiles.Sum(x => (long)x.Size) / (1024d * 1024d);

		Log.Information($"Deleted {deletedMb:N1} MB");
	}

	public async Task PerformUpgrade()
	{
		var upgrader = new HaydenDbUpgrader();
		await upgrader.UpgradeAsync(ConsumerConfig, DbContextOptions, new FileSystem(), false);
	}

	//public async Task ImportMediaFromFolder(string folder, )
	//{
	//	using var context = new HaydenDbContext(DbContextOptions);

		
	//}
}
