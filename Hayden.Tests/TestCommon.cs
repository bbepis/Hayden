using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json.Linq;
using System;

namespace Hayden.Tests
{
	internal static class TestCommon
	{
		private static bool HasSetLogger = false;

		internal static DbContextOptions<HaydenDbContext> CreateMemoryContextOptions()
		{
			if (!HasSetLogger)
				SerilogManager.SetLogger();

			HasSetLogger = true;
			SerilogManager.LevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;

			// Set this to true when debugging migration issues
			bool logEfCore = false;

			var builder = new DbContextOptionsBuilder<HaydenDbContext>()
				.UseSqlite(new SqliteConnection("Data Source=:memory:"))
				.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
				.ReplaceService<IMigrationsIdGenerator, VersionedMigrationIdGenerator>();

			if (logEfCore)
			{
				var efCoreLogger = SerilogManager.CreateSubLogger("EFCore");
				builder.LogTo(efCoreLogger.Information);
			}

			return builder.Options;
		}

		internal static (Thread, ThreadPointer) GenerateThread(ulong postNumberOffset = 0)
		{
			var thread = new Thread
			{
				ThreadId = 123 + postNumberOffset,
				Title = "Thread Title",
				IsArchived = false,
				Posts = new[]
				{
					new Post
					{
						PostNumber = 123 + postNumberOffset,
						TimePosted = new DateTimeOffset(2020, 02, 02, 02, 02, 02, TimeSpan.Zero),
						Author = "Big",
						Tripcode = "!chungus",
						ContentRaw = "My first post",
						ContentRendered = "<b>My first post</b>",
						Email = "email@example.com",
						IsDeleted = false,
						ContentType = ContentType.Yotsuba, // non-zero
						Media = new[]
						{
							new Media
							{
								Filename = "filename1",
								FileExtension = "jpg",
								ThumbnailExtension = "jpg",
								FileUrl = "test://my.com/test-file1.jpg",
								ThumbnailUrl = "test://my.com/test-file1-thumb.jpg",
								Sha256Hash = new byte[32] { 0x24, 0x10, 0x63, 0x94, 0x15, 0x4a, 0x78, 0x7a, 0x6b, 0x3c, 0xb9, 0xdf, 0xb7, 0xcd, 0xfa, 0x31, 0x1f, 0xdf, 0x07, 0x34, 0x18, 0xd4, 0x75, 0x22, 0x54, 0xa0, 0x5c, 0x55, 0xd6, 0xcf, 0x8f, 0x8d },
								Sha1Hash = new byte[20] { 0x28, 0x3f, 0xde, 0x05, 0xf4, 0x77, 0x0d, 0x8e, 0x97, 0xe8, 0x6f, 0x50, 0x54, 0xb7, 0x9d, 0xdd, 0x04, 0x77, 0x82, 0x85 },
								Md5Hash = new byte[16] { 0x9a, 0xa4, 0x19, 0xc5, 0xbc, 0x88, 0xb8, 0x72, 0x32, 0xa0, 0xeb, 0x09, 0x9b, 0xff, 0x9d, 0xb0 },
								FileSize = 1234,
								IsSpoiler = true,
								IsDeleted = false,
								Index = 0,
								AdditionalMetadata = new()
								{
									CustomSpoiler = 1
								}
							},
							new Media
							{
								Filename = "file2",
								FileExtension = "png",
								ThumbnailExtension = "webp",
								FileUrl = "test://my.com/test-file2.png",
								ThumbnailUrl = "test://my.com/test-file2.webp",
								Sha256Hash = new byte[32] { 0x42, 0x4c, 0xc8, 0xe9, 0x79, 0xf4, 0xd6, 0x1c, 0x6a, 0x38, 0xae, 0xbf, 0xdd, 0xa5, 0xc5, 0xe4, 0x98, 0xa0, 0xab, 0x07, 0x73, 0x35, 0x5b, 0x80, 0x4b, 0xad, 0x33, 0xba, 0xb0, 0x97, 0xa4, 0x62 },
								Sha1Hash = new byte[20] { 0x5f, 0x74, 0x7f, 0x65, 0x41, 0xc2, 0x71, 0x62, 0xae, 0x12, 0x33, 0xd0, 0x61, 0x39, 0xb3, 0xa9, 0x44, 0x2e, 0xc8, 0x0d },
								Md5Hash = new byte[16] { 0x5f, 0xa8, 0x40, 0x35, 0x96, 0xca, 0xc2, 0x75, 0xc0, 0x0f, 0xd1, 0x63, 0x7d, 0xc1, 0x31, 0x1a },
								FileSize = 1235,
								IsSpoiler = false,
								IsDeleted = false,
								Index = 1,
								AdditionalMetadata = new()
								{
									CustomSpoiler = 2
								}
							},
						}
					},
					new Post
					{
						PostNumber = 124 + postNumberOffset,
						TimePosted = new DateTimeOffset(2020, 02, 02, 03, 03, 03, TimeSpan.Zero),
						Author = null,
						Tripcode = null,
						ContentRaw = "Reply",
						ContentRendered = "Reply",
						Email = null,
						IsDeleted = false,
						ContentType = ContentType.Yotsuba, // non-zero
						Media = Array.Empty<Media>()
					}
				}
			};
			return (thread, new ThreadPointer("test", 123 + postNumberOffset));
		}

		internal const string DownloadPath = @"C:\temp\hayden_test";
	}
}
