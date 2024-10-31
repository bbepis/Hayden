using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Consumers.HaydenMysql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;
using Hayden.Config;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;

using Assert = NUnit.Framework.Legacy.ClassicAssert;
using System;
using Hayden.Consumers;

namespace Hayden.Tests.Consumers;

internal partial class HaydenConsumerTests
{
	internal class MigrationTests
	{
		private SqliteConnection SetupV1DatabaseConnection()
		{
			using var sqlStream = typeof(HaydenConsumerTests).Assembly.GetManifestResourceStream(
				"Hayden.Tests.Consumers.HaydenMigrationTestV1.sql");
			using var reader = new StreamReader(sqlStream, System.Text.Encoding.UTF8);

			var connection = new SqliteConnection("Data Source=:memory:");
			connection.Open();

			using var command = new SqliteCommand(reader.ReadToEnd(), connection);
			command.ExecuteNonQuery();

			return connection;
		}

		private DbContextOptions<HaydenDbContext> GetOptions(SqliteConnection connection)
		{
			return new DbContextOptionsBuilder<HaydenDbContext>()
				.SetupHaydenDb(DatabaseType.None, "")
				.UseSqlite(connection)
				.Options;
		}

		private readonly string[] OldFilepaths = new string[]
		{
			@"C:\temp\hayden_test\board1\image\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\board1\thumb\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\board1\image\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.png",
			@"C:\temp\hayden_test\board1\thumb\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.webp"
		};

		private readonly string[] OldFilepathsAlt = new string[]
		{
			@"C:\temp\hayden_test\board2\image\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\board2\thumb\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\board1\image\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.png",
			@"C:\temp\hayden_test\board1\thumb\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.webp"
		};

		private MockFileSystem SetupFilesystem(bool addThumbs = true, bool alt = false)
		{
			var paths = alt ? OldFilepathsAlt : OldFilepaths;

			var mockFilesystem = new MockFileSystem();

			mockFilesystem.AddFileFromEmbeddedResource(paths[0], typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.1.jpg");

			if (addThumbs)
				mockFilesystem.AddFileFromEmbeddedResource(paths[1], typeof(HaydenConsumerTests).Assembly,
					"Hayden.Tests.TestImages.1-thumb.jpg");

			mockFilesystem.AddFileFromEmbeddedResource(paths[2], typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.2.png");

			if (addThumbs)
				mockFilesystem.AddFileFromEmbeddedResource(paths[3], typeof(HaydenConsumerTests).Assembly,
					"Hayden.Tests.TestImages.2-thumb.webp");

			return mockFilesystem;
		}

		private ConsumerConfig GetConfig()
		{
			return new ConsumerConfig
			{
				ConnectionString = "",
				DatabaseType = DatabaseType.Sqlite,
				DownloadLocation = @"C:\temp\hayden_test"
			};
		}

		[Test]
		public async Task CanMigrateSuccessfully()
		{
			using var connection = SetupV1DatabaseConnection();
			var contextOptions = GetOptions(connection);
			var filesystem = SetupFilesystem();

			var upgrader = new HaydenDbUpgrader();
			var config = GetConfig();
			await upgrader.UpgradeAsync(config, contextOptions, filesystem, false);

			Console.WriteLine("Filesystem:");
			foreach (var file in filesystem.AllFiles)
				Console.WriteLine(file);

			using var context = new HaydenDbContext(contextOptions);

			var files = context.Files.ToArray();
			Assert.AreEqual(4, files.Length);
			Assert.AreEqual(4, filesystem.AllFiles.Count());

			var shouldExist = new bool[] { true, true, false, false };

			for (int i = 0; i < files.Length; i++)
			{
				Assert.AreEqual(shouldExist[i], files[i].FileExists);
				Assert.AreEqual(shouldExist[i], files[i].ThumbnailExists);
			}

			Assert.AreEqual(files[0].Id, context.FileMappings.First(x => x.BoardId == 1 && x.PostId == 1).FileId);
			Assert.AreEqual(files[0].Id, context.FileMappings.First(x => x.BoardId == 2 && x.PostId == 1).FileId);

			foreach (var file in files)
			{
				var fullFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);
				var thumbFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				Assert.AreEqual(file.FileExists, filesystem.FileExists(fullFilename));
				Assert.AreEqual(file.ThumbnailExists, filesystem.FileExists(thumbFilename));
			}
		}

		[Test]
		public async Task CanMigrateSuccessfully_NoThumbs()
		{
			using var connection = SetupV1DatabaseConnection();
			var contextOptions = GetOptions(connection);
			var filesystem = SetupFilesystem(false);

			var upgrader = new HaydenDbUpgrader();
			var config = GetConfig();
			await upgrader.UpgradeAsync(config, contextOptions, filesystem, false);

			Console.WriteLine("Filesystem:");
			foreach (var file in filesystem.AllFiles)
				Console.WriteLine(file);

			using var context = new HaydenDbContext(contextOptions);

			var files = context.Files.ToArray();
			Assert.AreEqual(4, files.Length);
			Assert.AreEqual(2, filesystem.AllFiles.Count());

			var shouldExist = new bool[] { true, true, false, false };

			for (int i = 0; i < files.Length; i++)
			{
				Assert.AreEqual(shouldExist[i], files[i].FileExists);
				Assert.IsFalse(files[i].ThumbnailExists);
			}

			foreach (var file in files)
			{
				var fullFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);
				var thumbFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				Assert.AreEqual(file.FileExists, filesystem.FileExists(fullFilename));
				Assert.AreEqual(file.ThumbnailExists, filesystem.FileExists(thumbFilename));
			}
		}

		[Test]
		public async Task CanMigrateSuccessfully_AllFilesMissing()
		{
			using var connection = SetupV1DatabaseConnection();
			var contextOptions = GetOptions(connection);
			var filesystem = new MockFileSystem();
			var config = GetConfig();
			filesystem.AddDirectory(config.DownloadLocation);

			var upgrader = new HaydenDbUpgrader();
			await upgrader.UpgradeAsync(config, contextOptions, filesystem, false);

			Console.WriteLine("Filesystem:");
			foreach (var file in filesystem.AllFiles)
				Console.WriteLine(file);

			using var context = new HaydenDbContext(contextOptions);

			var files = context.Files.ToArray();
			Assert.AreEqual(4, files.Length);
			Assert.AreEqual(0, filesystem.AllFiles.Count());

			var shouldExist = new bool[] { false, false, false, false };

			for (int i = 0; i < files.Length; i++)
			{
				Assert.AreEqual(shouldExist[i], files[i].FileExists);
				Assert.AreEqual(shouldExist[i], files[i].ThumbnailExists);
			}

			foreach (var file in files)
			{
				var fullFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);
				var thumbFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				Assert.AreEqual(file.FileExists, filesystem.FileExists(fullFilename));
				Assert.AreEqual(file.ThumbnailExists, filesystem.FileExists(thumbFilename));
			}
		}

		[Test]
		public async Task CanMigrateSuccessfully_CanFillGaps()
		{
			using var connection = SetupV1DatabaseConnection();
			var contextOptions = GetOptions(connection);
			var filesystem = SetupFilesystem(true, true);

			var upgrader = new HaydenDbUpgrader();
			var config = GetConfig();
			await upgrader.UpgradeAsync(config, contextOptions, filesystem, false);

			Console.WriteLine("Filesystem:");
			foreach (var file in filesystem.AllFiles)
				Console.WriteLine(file);

			using var context = new HaydenDbContext(contextOptions);

			var files = context.Files.ToArray();
			Assert.AreEqual(4, files.Length);
			Assert.AreEqual(4, filesystem.AllFiles.Count());

			var shouldExist = new bool[] { true, true, false, false };

			for (int i = 0; i < files.Length; i++)
			{
				Assert.AreEqual(shouldExist[i], files[i].FileExists);
				Assert.AreEqual(shouldExist[i], files[i].ThumbnailExists);
			}

			foreach (var file in files)
			{
				var fullFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);
				var thumbFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				Assert.AreEqual(file.FileExists, filesystem.FileExists(fullFilename));
				Assert.AreEqual(file.ThumbnailExists, filesystem.FileExists(thumbFilename));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task CanMigrateSuccessfully_ManagesBannedFiles(bool alternateCase)
		{
			using var connection = SetupV1DatabaseConnection();
			var contextOptions = GetOptions(connection);
			var filesystem = SetupFilesystem(true, alternateCase);

			var targetId = alternateCase ? 1 : 3;

			using (var command = new SqliteCommand($"UPDATE files SET FileBanned = 1 WHERE Id = {targetId}", connection))
			{
				await command.ExecuteNonQueryAsync();
			}

			var upgrader = new HaydenDbUpgrader();
			var config = GetConfig();
			await upgrader.UpgradeAsync(config, contextOptions, filesystem, false);

			Console.WriteLine("Filesystem:");
			foreach (var file in filesystem.AllFiles)
				Console.WriteLine(file);

			using var context = new HaydenDbContext(contextOptions);

			var files = context.Files.OrderBy(x => x.Id).ToArray();
			Assert.AreEqual(4, files.Length);
			Assert.AreEqual(2, filesystem.AllFiles.Where(x => !x.Contains("premigration")).Count());

			Assert.IsTrue(files[0].FileBanned);

			var shouldExist = new bool[] { false, true, false, false };

			for (int i = 0; i < files.Length; i++)
			{
				Assert.AreEqual(shouldExist[i], files[i].FileExists);
				Assert.AreEqual(shouldExist[i], files[i].ThumbnailExists);
			}

			foreach (var file in files)
			{
				var fullFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.FullImage, file.Id, file.Extension);
				var thumbFilename = HaydenThreadConsumer.CalculateFilename(config.DownloadLocation, Common.MediaType.Thumbnail, file.Id, file.ThumbnailExtension);

				Assert.AreEqual(file.FileExists, filesystem.FileExists(fullFilename));
				Assert.AreEqual(file.ThumbnailExists, filesystem.FileExists(thumbFilename));
			}
		}
	}
}
