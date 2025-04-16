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

	public async Task ScrubFiles()
	{
		using var context = new HaydenDbContext(DbContextOptions);

		Log.Information($"Counting files...");

		var fileCount = await context.Files
			.AsNoTracking()
			.CountAsync();
		
		Log.Information($"Found {fileCount:N0} files");

		long lastId = -1;
		long counter = 0;

		while (true)
		{
			context.ChangeTracker.Clear();

			var nextBatch = await context.Files
				.AsNoTracking()
				.Where(x => x.Id > lastId)
				.OrderBy(x => x.Id)
				.Take(1000)
				.ToArrayAsync();

			if (nextBatch.Length == 0)
				break;

			foreach (var file in nextBatch)
			{
				counter++;

				var fullFilePath = HaydenThreadConsumer.CalculateFilename(
					ConsumerConfig.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);

				var thumbFilePath = HaydenThreadConsumer.CalculateFilename(
					ConsumerConfig.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				var fileExists = File.Exists(fullFilePath);
				if (fileExists != file.FileExists)
				{
					Log.Warning("File {fileId} was {actual} when it was recorded as {expected}", file.Id,
						fileExists ? "found" : "missing",
						file.FileExists ? "found" : "missing");

					file.FileExists = fileExists;
					context.Update(file);
				}

				var thumbExists = File.Exists(thumbFilePath);
				if (thumbExists != file.ThumbnailExists)
				{
					Log.Warning("File {fileId} thumbnail was {actual} when it was recorded as {expected}", file.Id,
						thumbExists ? "found" : "missing",
						file.ThumbnailExists ? "found" : "missing");

					file.ThumbnailExists = thumbExists;
					context.Update(file);
				}

				if (fileExists)
				{
					if (file.Md5Hash == null || file.Sha256Hash == null || file.Sha1Hash == null)
					{
						using var fileStream = new FileStream(fullFilePath, FileMode.Open);
						var (md5, sha1, sha256) = Utility.CalculateHashes(fileStream);

						if (file.Md5Hash == null)
							file.Md5Hash = md5;
						if (file.Sha1Hash == null)
							file.Sha1Hash = sha1;
						if (file.Sha256Hash == null)
							file.Sha256Hash = sha256;

						context.Update(file);
					}

					//if (file.PerceptualHash == null)
					//{

					//}
				}

				if (file.Sha256Hash != null)
				{
					var existingFile = await context.Files
						.Where(x => x.Sha256Hash == file.Sha256Hash && x.Id < file.Id)
						.OrderBy(x => x.Id)
						.FirstOrDefaultAsync();

					if (existingFile != null)
					{
						var localFile = context.Files.Local.FindEntry(existingFile.Id);
						if (localFile != null)
							existingFile = localFile.Entity;
					}
					else
					{
						existingFile = nextBatch.FirstOrDefault(x =>
							x.Id < file.Id
							&& x.Sha256Hash != null
							&& Utility.ByteArrayEquals(x.Sha256Hash, file.Sha256Hash));
					}

					if (existingFile != null)
					{
						Log.Warning("Found duplicate file at ID {fileId}, merging into file {existingFileId}", file.Id, existingFile.Id);

						if (file.FileBanned)
						{
							existingFile.FileBanned = true;
							context.Update(existingFile);
						}

						var existingFullFilePath = HaydenThreadConsumer.CalculateFilename(
							ConsumerConfig.DownloadLocation, Common.MediaType.FullImage, existingFile.Id, existingFile.Extension);
						var existingThumbFilePath = HaydenThreadConsumer.CalculateFilename(
							ConsumerConfig.DownloadLocation, Common.MediaType.Thumbnail, existingFile.Id, existingFile.ThumbnailExtension);

						if (!existingFile.FileExists && fileExists && !existingFile.FileBanned)
						{
							File.Move(fullFilePath, existingFullFilePath);
							existingFile.FileExists = true;

							existingFile.Sha256Hash = file.Sha256Hash;
							existingFile.Sha1Hash = file.Sha1Hash;
							existingFile.Md5Hash = file.Md5Hash;

							context.Update(existingFile);
						}

						if (!existingFile.ThumbnailExists && thumbExists && !existingFile.FileBanned)
						{
							File.Move(thumbFilePath, existingThumbFilePath);
							existingFile.ThumbnailExists = true;
							context.Update(existingFile);
						}

						await context.SaveChangesAsync();

						await context.FileMappings
							.Where(x => x.FileId == file.Id)
							.ExecuteUpdateAsync(x => x.SetProperty(y => y.FileId, existingFile.Id));

						if (existingFile.FileBanned)
						{
							if (existingFile.FileExists)
								File.Delete(existingFullFilePath);
							if (existingFile.ThumbnailExists)
								File.Delete(existingThumbFilePath);
						}

						if (File.Exists(fullFilePath))
							File.Delete(fullFilePath);
						if (File.Exists(thumbFilePath))
							File.Delete(thumbFilePath);

						context.Remove(file);
					}
				}
			}

			await context.SaveChangesAsync();

			lastId = nextBatch.Max(x => x.Id);

			Log.Information("Processed {processed} / {total}", counter, fileCount);
		}

		Log.Information("Completed processing {processed} / {total}", counter, fileCount);
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
