using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;

namespace Hayden.Tests
{
	internal static class TestCommon
	{
		internal static DbContextOptions<HaydenDbContext> CreateMemoryContextOptions(bool createTestBoard = true)
		{
			var options = new DbContextOptionsBuilder<HaydenDbContext>()
				.UseInMemoryDatabase(Guid.NewGuid().ToString("D"))
				.Options;

			if (createTestBoard)
			{
				using var context = new HaydenDbContext(options);

				context.Boards.Add(new DBBoard { Id = 1, Category = "Test", ShortName = "test", LongName = "Testing Board" });
				context.SaveChanges();
			}

			return options;
		}

		internal static (Thread, ThreadPointer) GenerateThread()
		{
			var thread = new Thread
			{
				ThreadId = 123,
				Title = "Thread Title",
				IsArchived = false,
				Posts = new[]
				{
					new Post
					{
						PostNumber = 123,
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
								Sha256Hash = new byte[32] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
								Sha1Hash = new byte[20] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
								Md5Hash = new byte[16] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
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
								Sha256Hash = new byte[32] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 },
								Sha1Hash = new byte[20] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 },
								Md5Hash = new byte[16] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 },
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
						PostNumber = 124,
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
			return (thread, new ThreadPointer("test", 123));
		}

		internal const string DownloadPath = @"C:\temp\hayden_test";
	}
}
