using System;
using System.Linq;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Threading.Tasks;
using System.IO.Abstractions;

namespace Hayden.Consumers.HaydenMysql;
public class HaydenDbUpgrader
{
	private DbContextOptions<HaydenDbContext> ContextOptions { get; set; }

	public async Task UpgradeAsync(ConsumerConfig config, DbContextOptions<HaydenDbContext> contextOptions, IFileSystem fileSystem, bool onlyAuto = true)
	{
		ContextOptions = contextOptions;

		await using var context = new HaydenDbContext(ContextOptions);

		var migrator = context.Database.GetService<IMigrator>();

		var completedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
		var isNew = completedMigrations.Length == 0;

		while (true)
		{
			var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();

			if (pendingMigrations.Length == 0)
				break; // no migrations to do

			// TODO: replace with logger calls
			Console.WriteLine($"Applying migration \"{pendingMigrations[0]}\"");

			if (pendingMigrations[0] == "v2_Version2")
			{
				var count = await context.Database.SqlQuery<int>($"SELECT COUNT(*) as Value FROM files").FirstAsync();

				if (onlyAuto && (!isNew || count == 0))
					throw new Exception("Migrating to version v2 requires manual upgrading; stopping to preserve data integrity");

				await PerformVersion2Upgrade(fileSystem, migrator, config);
				continue;
			}

			await migrator.MigrateAsync(pendingMigrations[0]);
		}

		Console.WriteLine($"Upgrade complete");
	}

	private async Task PerformVersion2Upgrade(IFileSystem fs, IMigrator migrator, ConsumerConfig config)
	{
		var premigrationPath = fs.Path.Combine(config.DownloadLocation, "premigration");

		var directoriesToMove = fs.Directory.EnumerateDirectories(config.DownloadLocation).ToArray();

		if (!fs.Directory.Exists(premigrationPath))
		{
			fs.Directory.CreateDirectory(premigrationPath);

			foreach (var directory in directoriesToMove)
				fs.Directory.Move(directory, fs.Path.Combine(premigrationPath, fs.Path.GetFileName(directory)));
		}

		fs.Directory.CreateDirectory(fs.Path.Combine(config.DownloadLocation, "image"));
		fs.Directory.CreateDirectory(fs.Path.Combine(config.DownloadLocation, "thumb"));

		string V1CalculateFilename(string baseFolder, string board, Common.MediaType mediaType, byte[] hash, string extension)
		{
			var base36Name = Utility.ConvertToBase(hash, 36);

			string mediaTypeString = mediaType switch
			{
				Common.MediaType.FullImage => "image",
				Common.MediaType.Thumbnail => "thumb",
				_                   => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null)
			};

			return fs.Path.Combine(baseFolder, board, mediaTypeString, $"{base36Name}.{extension.TrimStart('.').ToLower()}");
		}

		DBFileV1[] files;

		Console.WriteLine($"- Collecting file list");

		using (var v1Context = new HaydenContextV1(ContextOptions))
		{
			files = await v1Context.FileV1.OrderBy(x => x.Id).ToArrayAsync();
		}

		Console.WriteLine($"- Found {files.Length} files to migrate");

		await migrator.MigrateAsync("v2_Version2");

		using var context = new HaydenDbContext(ContextOptions);

		var boards = await context.Boards.AsNoTracking().ToDictionaryAsync(x => x.Id);
		//var files = await context.Database.SqlQueryRaw<DBFileV1>("SELECT * FROM files;").ToArrayAsync();
		

		foreach (var file in files)
		{
			// check if the file exists, even if we marked it as doesn't exist

			var v1Path = V1CalculateFilename(premigrationPath, boards[file.BoardId].ShortName,
				Common.MediaType.FullImage, file.Sha256Hash, file.Extension);
			var v1ThumbPath = V1CalculateFilename(premigrationPath, boards[file.BoardId].ShortName,
				Common.MediaType.Thumbnail, file.Sha256Hash, file.ThumbnailExtension);

			var v2Path = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation,
				Common.MediaType.FullImage, file.Id, file.Extension);
			var v2ThumbPath = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation,
				Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

			var existingFile = await context.Files.FirstOrDefaultAsync(x =>
				x.Sha256Hash == file.Sha256Hash && x.Id < file.Id);

			if (existingFile != null)
			{
				var localFile = context.Files.Local.FindEntry(existingFile.Id);
				if (localFile != null)
					existingFile = localFile.Entity;
			}

			if (existingFile != null)
			{
				await context.FileMappings
					.Where(x => x.FileId == file.Id)
					.ExecuteUpdateAsync(x => x.SetProperty(y => y.FileId, existingFile.Id));

				await context.Files.Where(x => x.Id == file.Id).ExecuteDeleteAsync();

				await context.SaveChangesAsync();

				var existingFilePath = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation,
					Common.MediaType.FullImage, existingFile.Id, existingFile.Extension);
				var existingThumbPath = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation,
					Common.MediaType.Thumbnail, existingFile.Id, existingFile.Extension);

				if (file.FileBanned)
				{
					existingFile.FileBanned = true;

					if (existingFile.FileExists)
					{
						fs.File.Delete(existingFilePath);
						existingFile.FileExists = false;
					}

					if (existingFile.ThumbnailExists)
					{
						fs.File.Delete(existingThumbPath);
						existingFile.ThumbnailExists = false;
					}

					context.Update(existingFile);
				}
				else if (!existingFile.FileBanned)
				{
					if (!existingFile.FileExists && fs.File.Exists(v1Path))
					{
						fs.File.Move(v1Path, existingFilePath);

						existingFile.FileExists = true;
					}

					if (!existingFile.ThumbnailExists && fs.File.Exists(v1ThumbPath))
					{
						fs.File.Move(v1ThumbPath, existingThumbPath);

						existingFile.ThumbnailExists = true;
					}

					context.Update(existingFile);
				}

				continue;
			}

			var dbFile = await context.Files.FirstAsync(x => x.Id == file.Id);

			if (fs.File.Exists(v1Path))
			{
				var hashes = Utility.CalculateHashes(fs.File.OpenRead(v1Path));

				if (!Utility.ByteArrayEquals(file.Sha256Hash, hashes.sha256Hash)
					|| !Utility.ByteArrayEquals(file.Sha1Hash, hashes.sha1Hash)
					|| !Utility.ByteArrayEquals(file.Md5Hash, hashes.md5Hash)
					|| !file.FileExists)
				{
					dbFile.Sha256Hash = hashes.sha256Hash;
					dbFile.Sha1Hash = hashes.sha1Hash;
					dbFile.Md5Hash = hashes.md5Hash;
					dbFile.FileExists = true;

					context.Update(dbFile);
				}

				fs.File.Move(v1Path, v2Path);
			}
			else if (file.FileExists)
			{
				dbFile.FileExists = false;
				context.Update(dbFile);
			}

			if (fs.File.Exists(v1ThumbPath))
			{
				dbFile.ThumbnailExists = true;
				context.Update(dbFile);

				fs.File.Move(v1ThumbPath, v2ThumbPath);
			}
		}

		await context.SaveChangesAsync();

		Console.WriteLine($"- Complete!");
	}

	class HaydenContextV1 : HaydenDbContext
	{
		public DbSet<DBFileV1> FileV1 { get; set; }

		public HaydenContextV1(DbContextOptions<HaydenDbContext> context) : base(context) { }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			modelBuilder.Entity<DBFileV1>().ToTable("files");
			modelBuilder.Ignore<DBFile>();
		}
	}

	private class DBFileV1
	{
		public uint Id { get; set; }
		public ushort BoardId { get; set; }
		public byte[] Md5Hash { get; set; }
		public byte[] Sha1Hash { get; set; }
		public byte[] Sha256Hash { get; set; }
		public byte[] PerceptualHash { get; set; }
		public byte[] StreamHash { get; set; }
		public string Extension { get; set; }
		public string ThumbnailExtension { get; set; }
		public bool FileExists { get; set; }
		public bool FileBanned { get; set; }
		public ushort? ImageWidth { get; set; }
		public ushort? ImageHeight { get; set; }
		public uint Size { get; set; }
		public string AdditionalMetadata { get; set; }
	}
}