using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.MediaInfo;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Hayden.Tests.Consumers
{
	internal partial class HaydenConsumerTests
	{
		const string file1path = @"C:\temp\temp1.jpg";
		const string file1Thumbpath = @"C:\temp\temp1-thumb.jpg";
		const string file2path = @"C:\temp\temp2.png";
		const string file2Thumbpath = @"C:\temp\temp2-thumb.webp";

		static readonly string[] ExpectedPathList = new[]
		{
			@"C:\temp\hayden_test\image\1.jpg",
			@"C:\temp\hayden_test\thumb\1.jpg",
			@"C:\temp\hayden_test\image\2.png",
			@"C:\temp\hayden_test\thumb\2.webp"
		};

		private static async Task<TestHaydenConsumer> CreateHaydenConsumerAsync(DbContextOptions<HaydenDbContext> options, IFileSystem fileSystem,
			Action<ConsumerConfig, SourceConfig>? setupConfig = null, bool createTestBoard = true)
		{
			var mockMediaInspector = new Mock<IMediaInspector>(MockBehavior.Strict);

			mockMediaInspector.Setup(x => x.DetermineMediaInfoAsync(It.IsAny<string>(), It.IsAny<DBFile>()))
				.Returns((string filename, DBFile dbFile) => {
					dbFile.ImageHeight = 24;
					dbFile.ImageWidth = 24;
					return Task.FromResult(dbFile);
				});

			mockMediaInspector.Setup(x => x.DetermineMediaTypeAsync(It.IsAny<Stream>(), It.IsAny<string>()))
				.Returns<MediaStream[]>(null);

			var consumerConfig = new ConsumerConfig
			{
				DownloadLocation = TestCommon.DownloadPath,
				FullImagesEnabled = true,
				ThumbnailsEnabled = true
			};

			var sourceConfig = new SourceConfig
			{
				Boards = [],
				BoardScrapeDelay = 0,
				ApiDelay = 0
			};

			if (createTestBoard)
				sourceConfig.Boards = [new BoardConfig("test")];

			if (setupConfig != null)
				setupConfig(consumerConfig,  sourceConfig);

			var consumer = new TestHaydenConsumer(consumerConfig, sourceConfig, () => new HaydenDbContext(options), fileSystem, mockMediaInspector.Object);

			await consumer.InitializeAsync();

			return consumer;
		}

		private static void CreateMockTempFiles(MockFileSystem mockFilesystem)
		{
			mockFilesystem.AddFileFromEmbeddedResource(file1path, typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.1.jpg");
			mockFilesystem.AddFileFromEmbeddedResource(file1Thumbpath, typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.1-thumb.jpg");

			mockFilesystem.AddFileFromEmbeddedResource(file2path, typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.2.png");
			mockFilesystem.AddFileFromEmbeddedResource(file2Thumbpath, typeof(HaydenConsumerTests).Assembly,
				"Hayden.Tests.TestImages.2-thumb.webp");
		}

		private static void CleanupTempFiles(MockFileSystem mockFilesystem)
		{
			foreach (var path in new[] { file1path, file1Thumbpath, file2path, file2Thumbpath })
			{
				if (mockFilesystem.FileExists(path))
					mockFilesystem.File.Delete(path);
			}
		}

		private void AssertDataIsSame(Thread thread, DBPost dbPost)
		{
			AssertDataIsSame(thread.Posts.Single(x => x.PostNumber == dbPost.PostId), dbPost);
		}

		private void AssertDataIsSame(Post post, DBPost dbPost)
		{
			Assert.AreEqual(post.PostNumber, dbPost.PostId);
			Assert.AreEqual(post.ContentRendered, dbPost.ContentHtml);
			Assert.AreEqual(post.ContentRaw, dbPost.ContentRaw);
			Assert.AreEqual(post.ContentType, dbPost.ContentType);
			Assert.AreEqual(post.Author, dbPost.Author);
			Assert.AreEqual(post.Tripcode, dbPost.Tripcode);
			Assert.AreEqual(post.Email, dbPost.Email);
			Assert.AreEqual(post.TimePosted.UtcDateTime, dbPost.DateTime);
			Assert.AreEqual(post.TimeDeleted ?? null, dbPost.TimeDeleted);

			var additionalMetadata = Common.SerializeAdditionalMetadata(post.AdditionalMetadata);
			if (additionalMetadata == null)
				Assert.IsNull(dbPost.AdditionalMetadata);
			else
				Assert.IsTrue(JToken.DeepEquals(JToken.Parse(additionalMetadata), JToken.Parse(dbPost.AdditionalMetadata)));
		}

		private void AssertDataIsSame(Post post, QueuedImageDownload queuedImageDownload)
		{
			AssertDataIsSame(post.Media.Single(x => x.FileUrl == queuedImageDownload.FullImageUri.AbsoluteUri), queuedImageDownload);
		}

		private void AssertDataIsSame(Media media, QueuedImageDownload queuedImageDownload)
		{
			Assert.AreEqual(media.FileUrl, queuedImageDownload.FullImageUri.AbsoluteUri);
			Assert.AreEqual(media.ThumbnailUrl, queuedImageDownload.ThumbnailImageUri.AbsoluteUri);
			// TODO: properties
		}

		private void AssertDataIsSame(Post post, Media media, DBFileMapping[] dbFileMappings)
		{
			AssertDataIsSame(media, dbFileMappings.Single(x => x.PostId == post.PostNumber && x.Index == media.Index));
		}

		private void AssertDataIsSame(Media media, DBFileMapping dbFileMapping)
		{
			Assert.AreEqual(media.Filename, dbFileMapping.Filename);
			Assert.AreEqual(media.Index, dbFileMapping.Index);
			Assert.AreEqual(media.TimestampedFilename, dbFileMapping.TimestampedFilename);

			// we want the file id to be null here as we haven't actually downloaded the file yet
			Assert.IsNotNull(dbFileMapping.FileId);
			
			// TODO: properties
		}

		[Test]
		public async Task IngestNewPostsTest()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);

			Assert.AreEqual(2, pendingDownloads.Count);
			AssertDataIsSame(thread.Posts[0], pendingDownloads[0]);
			AssertDataIsSame(thread.Posts[0], pendingDownloads[1]);


			await using (var context = new HaydenDbContext(options))
			{
				var posts = context.Posts.ToList();

				Assert.AreEqual(2, posts.Count);
				AssertDataIsSame(thread, posts[0]);
				AssertDataIsSame(thread, posts[1]);
				
				var files = context.Files.ToArray();
				var mappings = context.FileMappings.ToArray();

				Assert.AreEqual(2, files.Length);
				foreach (var file in files)
				{
					Assert.IsFalse(file.FileExists);
					Assert.IsFalse(file.ThumbnailExists);
				}

				Assert.AreEqual(2, mappings.Length);
				AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[0], mappings);
				AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[1], mappings);
				
				var threads = context.Threads.ToArray();

				Assert.AreEqual(1, threads.Length);
				Assert.AreEqual(thread.ThreadId, threads[0].ThreadId);
				Assert.AreEqual(thread.ArchivedTime, threads[0].TimeArchived);
				Assert.AreEqual(thread.DeletedTime, threads[0].TimeDeleted);
				Assert.AreEqual(thread.Title, threads[0].Title);
				Assert.AreEqual(null, threads[0].AdditionalMetadata);
			}
			
			async Task testFile(Media media)
			{
				await using var context = new HaydenDbContext(options);

				var fileMapping = context.FileMappings.Single(x => x.BoardId == 1
															   && x.PostId == thread.Posts[0].PostNumber
															   && x.Index == media.Index);

				Assert.IsNotNull(fileMapping.FileId);

				var file = context.Files.Single(x => x.Id == fileMapping.FileId);

				Assert.AreEqual(media.FileExtension, file.Extension);
				Assert.AreEqual(media.ThumbnailExtension, file.ThumbnailExtension);

				Assert.AreEqual(24, file.ImageHeight);
				Assert.AreEqual(24, file.ImageWidth);

				Assert.IsFalse(file.FileBanned);
				Assert.IsTrue(file.FileExists);
			}

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);
			
			await testFile(thread.Posts[0].Media[0]);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			await testFile(thread.Posts[0].Media[1]);
			
			await using (var context = new HaydenDbContext(options))
				foreach (var file in context.Files)
				{
					Assert.IsTrue(file.FileExists);
					Assert.IsTrue(file.ThumbnailExists);
				}
			
			CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}

		[TestCase(false, true)]
		[TestCase(false, false)]
		public async Task IngestNewPostsTest_ImagesDisabled(bool fullImagesEnabled, bool thumbnailsEnabled)
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem, (cConfig, sConfig) =>
			{
				cConfig.FullImagesEnabled = fullImagesEnabled;
				cConfig.ThumbnailsEnabled = thumbnailsEnabled;
			});

			var anyImages = fullImagesEnabled || thumbnailsEnabled;

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, fullImagesEnabled, thumbnailsEnabled);

			if (anyImages)
			{
				Assert.AreEqual(2, pendingDownloads.Count);
				//AssertDataIsSame(thread.Posts[0], pendingDownloads[0]);
				//AssertDataIsSame(thread.Posts[0], pendingDownloads[1]);
			}
			else
			{
				Assert.AreEqual(0, pendingDownloads.Count);
			}

			await using (var context = new HaydenDbContext(options))
			{
				var posts = context.Posts.ToList();

				Assert.AreEqual(2, posts.Count);
				AssertDataIsSame(thread, posts[0]);
				AssertDataIsSame(thread, posts[1]);

				var mappings = context.FileMappings.ToArray();

				Assert.AreEqual(2, mappings.Length);
				AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[0], mappings);
				AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[1], mappings);

				// Files should *always* be created
				Assert.AreEqual(2, context.Files.Count());

				var threads = context.Threads.ToArray();

				Assert.AreEqual(1, threads.Length);
				Assert.AreEqual(thread.ThreadId, threads[0].ThreadId);
				Assert.AreEqual(thread.ArchivedTime, threads[0].TimeArchived);
				Assert.AreEqual(thread.DeletedTime, threads[0].TimeDeleted);
				Assert.AreEqual(thread.Title, threads[0].Title);
				Assert.AreEqual(null, threads[0].AdditionalMetadata);
			}

			if (anyImages)
			{
				CreateMockTempFiles(mockFilesystem);

				QueuedImageDownload FindQueuedDownload(Media media)
				{
					return pendingDownloads.Single(x => x.FullImageUri?.AbsoluteUri == media.FileUrl || x.ThumbnailImageUri?.AbsoluteUri == media.ThumbnailUrl);
				}

				await consumer.ProcessFileDownload(FindQueuedDownload(thread.Posts[0].Media[0]),
					fullImagesEnabled ? file1path : null, 
					thumbnailsEnabled ? file1Thumbpath : null);

				await consumer.ProcessFileDownload(FindQueuedDownload(thread.Posts[0].Media[1]),
					fullImagesEnabled ? file2path : null,
					thumbnailsEnabled ? file2Thumbpath : null);

				// clean up any unused temp files, as a regular hayden instance would
				CleanupTempFiles(mockFilesystem);
				
				await using (var context = new HaydenDbContext(options))
					foreach (var file in context.Files)
					{
						Assert.AreEqual(fullImagesEnabled, file.FileExists);
						Assert.AreEqual(thumbnailsEnabled, file.ThumbnailExists);
					}

				if (fullImagesEnabled && thumbnailsEnabled)
				{
					CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
				}
				else
				{
					Assert.AreEqual(2, mockFilesystem.AllFiles.Count());

					if (fullImagesEnabled)
					{
						CollectionAssert.Contains(mockFilesystem.AllFiles, ExpectedPathList[0]);
						CollectionAssert.Contains(mockFilesystem.AllFiles, ExpectedPathList[2]);
					}
					else
					{
						CollectionAssert.Contains(mockFilesystem.AllFiles, ExpectedPathList[1]);
						CollectionAssert.Contains(mockFilesystem.AllFiles, ExpectedPathList[3]);
					}
				}				
			}
		}

		[Test]
		public async Task IngestPosts_Embed_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			const string embedUrl = "http://example.com";

			// remove images, replace with external url / embed
			thread.Posts[0].Media = new[]
			{
				new Media
				{
					AdditionalMetadata = new Media.MediaAdditionalMetadata()
					{
						ExternalMediaUrl = embedUrl
					}
				}
			};

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, false, false);

			Assert.AreEqual(0, pendingDownloads.Count);

			await using (var context = new HaydenDbContext(options))
			{
				var mappings = context.FileMappings.ToArray();

				Assert.AreEqual(1, mappings.Length);

				var mapping = mappings[0];

				Assert.IsNull(mapping.FileId);
				Assert.IsNotNull(mapping.AdditionalMetadata);

				var additionalMetadata = JObject.Parse(mapping.AdditionalMetadata).ToObject<Media.MediaAdditionalMetadata>();

				Assert.AreEqual(embedUrl, additionalMetadata.ExternalMediaUrl);
			}
			
			CollectionAssert.IsEmpty(mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}

		[Test]
		public async Task IngestPosts_PostDeleted_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);

			// simulate post deletion
			var deletedPost = thread.Posts[1];
			thread.Posts = new[]
			{
				thread.Posts[0]
			};

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				var deletedDbPost = context.Posts.First(x => x.PostId == deletedPost.PostNumber);

				Assert.IsTrue(deletedDbPost.TimeDeleted != null);
			}
		}

		[Test]
		public async Task IngestPosts_ThreadDeleted_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);

			var deletedTime = DateTimeOffset.Now;

			await consumer.ThreadUntracked(threadPointer.ThreadId, threadPointer.Board, deletedTime, null);

			await using (var context = new HaydenDbContext(options))
			{
				var dbThread = context.Threads.First(x => x.ThreadId == threadPointer.ThreadId);

				Assert.AreEqual(deletedTime.UtcDateTime, dbThread.TimeDeleted);
			}
		}

		[Test]
		public async Task IngestPosts_ThreadArchived_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);


			thread.ArchivedTime = DateTimeOffset.Now;

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				var dbThread = context.Threads.First(x => x.ThreadId == threadPointer.ThreadId);

				Assert.AreEqual(thread.ArchivedTime?.UtcDateTime, dbThread.TimeArchived);
				Assert.IsNull(dbThread.TimeDeleted);
			}
		}

		[Test]
		public async Task IngestPosts_ThreadLastModified_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);


			await using (var context = new HaydenDbContext(options))
			{
				var dbThread = context.Threads.First(x => x.ThreadId == threadPointer.ThreadId);

				Assert.AreEqual(thread.Posts[1].TimePosted.UtcDateTime, dbThread.LastModified);
			}

			var newPost = new Post
			{
				PostNumber = thread.Posts[1].PostNumber + 10,
				TimePosted = thread.Posts[1].TimePosted + TimeSpan.FromMinutes(10),
				Media = Array.Empty<Media>()
			};

			thread.Posts = new[]
			{
				thread.Posts[0],
				thread.Posts[1],
				newPost
			};

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				var dbThread = context.Threads.First(x => x.ThreadId == threadPointer.ThreadId);

				Assert.AreEqual(newPost.TimePosted.UtcDateTime, dbThread.LastModified);
			}

			// simulate post deletion, to ensure the updated bump time remains in DB
			thread.Posts = new[]
			{
				thread.Posts[0],
				thread.Posts[1]
			};

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				var dbThread = context.Threads.First(x => x.ThreadId == threadPointer.ThreadId);

				Assert.AreEqual(newPost.TimePosted.UtcDateTime, dbThread.LastModified);
			}
		}
		
		/// <summary>
		/// Tests if files marked as missing from disk can be redownloaded if found in another thread
		/// </summary>
		[Test]
		public async Task IngestPosts_NoFileExists_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			// delete the files on disk, mark them as non-existent

			foreach (var filename in ExpectedPathList)
				mockFilesystem.File.Delete(filename);

			await using (var context = new HaydenDbContext(options))
			{
				foreach (var file in context.Files.ToArray())
				{
					file.FileExists = false;
					file.ThumbnailExists = false;
					context.Update(file);
				}

				await context.SaveChangesAsync();
			}

			// simulate a different thread
			// ideally we should be able to track image redownloads within the same thread, but that's for a different day
			(thread, threadPointer) = TestCommon.GenerateThread(200);

			threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);
			
			Assert.AreEqual(2, pendingDownloads.Count);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);
			
			await using (var context = new HaydenDbContext(options))
			{
				foreach (var file in context.Files.ToArray())
				{
					Assert.IsTrue(file.FileExists);
					Assert.IsTrue(file.ThumbnailExists);
				}
			}

			CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}
		
		/// <summary>
		/// Tests if files marked as banned do not get marked for download
		/// </summary>
		[Test]
		public async Task IngestPosts_BannedFile_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			// Set up a banned image.
			DBFile bannedImage;

			await using (var context = new HaydenDbContext(options))
			{
				var media = thread.Posts[0].Media[0];

				bannedImage = new DBFile
				{
					Sha256Hash = media.Sha256Hash,
					Sha1Hash = media.Sha1Hash,
					Md5Hash = media.Md5Hash,
					FileBanned = true,
					FileExists = false,
					ThumbnailExists = false,
					Extension = media.FileExtension
				};

				context.Add(bannedImage);
				await context.SaveChangesAsync();
			}

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);

			// There are 2 files supplied with the thread, one should be banned from being downloaded
			Assert.AreEqual(1, pendingDownloads.Count);
			Assert.AreEqual(thread.Posts[0].Media[1].FileUrl, pendingDownloads[0].FullImageUri.AbsoluteUri);
		}
		
		/// <summary>
		/// Tests if files marked as deleted get updated correctly
		/// </summary>
		[Test]
		public async Task IngestPosts_DeletedFile_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			void DeleteImage(Media media)
			{
				media.FileUrl = null;
				media.ThumbnailUrl = null;
				media.IsDeleted = true;
			}

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				Assert.AreEqual(2, context.FileMappings.Count(x => !x.IsDeleted));
			}

			// delete image 1
			DeleteImage(thread.Posts[0].Media[0]);

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			Assert.AreEqual(1, threadUpdate.UpdatedPosts.Count);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				Assert.AreEqual(true, context.FileMappings.First(x => x.Index == 0).IsDeleted);
				Assert.AreEqual(false, context.FileMappings.First(x => x.Index == 1).IsDeleted);
			}

			// delete image 2
			DeleteImage(thread.Posts[0].Media[1]);

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			Assert.AreEqual(1, threadUpdate.UpdatedPosts.Count);
			await consumer.ConsumeThread(threadUpdate, false, false);

			await using (var context = new HaydenDbContext(options))
			{
				Assert.AreEqual(true, context.FileMappings.First(x => x.Index == 0).IsDeleted);
				Assert.AreEqual(true, context.FileMappings.First(x => x.Index == 1).IsDeleted);
			}
		}
		
		/// <summary>
		/// Tests if new files correctly get matched to existing files, when hashes aren't available from the source
		/// </summary>
		[Test]
		public async Task IngestPosts_UnhashedExistingFile_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			// Set up an existing image.
			DBFile existingImage;

			await using (var context = new HaydenDbContext(options))
			{
				var media = thread.Posts[0].Media[0];

				existingImage = new DBFile
				{
					Sha256Hash = media.Sha256Hash,
					Sha1Hash = media.Sha1Hash,
					Md5Hash = media.Md5Hash,
					FileExists = false,
					ThumbnailExists = false,
					Extension = media.FileExtension
				};

				context.Add(existingImage);
				await context.SaveChangesAsync();
			}

			// Remove the hashes from the polled thread
			foreach (var media in thread.Posts[0].Media)
			{
				media.Sha256Hash = null;
				media.Sha1Hash = null;
				media.Md5Hash = null;
			}

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);
			Assert.AreEqual(2, pendingDownloads.Count);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			// Before now, there were 3 files in the database as it couldn't match them.
			// Downloading the files and processing them should have the files merge back together
			await using (var context = new HaydenDbContext(options))
				Assert.AreEqual(2, context.Files.Count());

			// And since the files got merged, the actual file IDs have changed too so we must check different filenames
			string[] expectedPathList = new[]
			{
				@"C:\temp\hayden_test\image\1.jpg",
				@"C:\temp\hayden_test\thumb\1.jpg",
				@"C:\temp\hayden_test\image\3.png",
				@"C:\temp\hayden_test\thumb\3.webp"
			};

			CollectionAssert.AreEquivalent(expectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}
		
		/// <summary>
		/// Tests if banned files correctly do not get stored, when hashes aren't available from the source
		/// </summary>
		[Test]
		public async Task IngestPosts_UnhashedBannedFile_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			// Set up an existing image.
			DBFile existingImage;

			await using (var context = new HaydenDbContext(options))
			{
				var media = thread.Posts[0].Media[0];

				existingImage = new DBFile
				{
					Sha256Hash = media.Sha256Hash,
					Sha1Hash = media.Sha1Hash,
					Md5Hash = media.Md5Hash,
					FileBanned = true,
					FileExists = false,
					ThumbnailExists = false,
					Extension = media.FileExtension
				};

				context.Add(existingImage);
				await context.SaveChangesAsync();
			}

			// Remove the hashes from the polled thread
			foreach (var media in thread.Posts[0].Media)
			{
				media.Sha256Hash = null;
				media.Sha1Hash = null;
				media.Md5Hash = null;
			}

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);

			// Since we can't compare hashes yet, it'll try downloading the banned image
			Assert.AreEqual(2, pendingDownloads.Count);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);
			
			// clean up any unused temp files, as a regular hayden instance would
			CleanupTempFiles(mockFilesystem);

			// Check if the files got merged correctly
			await using (var context = new HaydenDbContext(options))
				Assert.AreEqual(2, context.Files.Count());

			// Check if the banned image never got saved to disk
			string[] expectedPathList = new[]
			{
				@"C:\temp\hayden_test\image\3.png",
				@"C:\temp\hayden_test\thumb\3.webp"
			};

			CollectionAssert.AreEquivalent(expectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}

		[TestCase(ConsolidationMode.Authoritative)]
		[TestCase(ConsolidationMode.Pessimistic)]
		public async Task IngestPosts_ConsolidationMethod_Test(ConsolidationMode consolidationMode)
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			consumer.ConsumerConfig.ConsolidationMode = consolidationMode;

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate, true, true);
			
			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			var originalContent = thread.Posts[0].ContentRaw;
			var originalPostNumber = thread.Posts[1].PostNumber;

			thread.Posts[0].ContentRaw = "abcd"; // simulate a post update
			thread.Posts[1].PostNumber += 10; // simulate a new post & a post deletion
			thread.Posts[1].Media = thread.Posts[0].Media; // simulate new post media

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			pendingDownloads = await consumer.ConsumeThread(threadUpdate, false, false);

			// NOTE: we don't change the hash here, so the image should be reused
			Assert.AreEqual(0, pendingDownloads.Count);

			await using (var context = new HaydenDbContext(options))
			{
				Assert.AreEqual(3, await context.Posts.CountAsync());
				Assert.AreEqual(4, await context.FileMappings.CountAsync());

				if (consolidationMode == ConsolidationMode.Authoritative)
				{
					Assert.IsTrue(context.Posts.Single(x => x.PostId == originalPostNumber).TimeDeleted != null);
					
					Assert.AreEqual("abcd", context.Posts.Single(x => x.PostId == thread.Posts[0].PostNumber).ContentRaw);
				}
				else if (consolidationMode == ConsolidationMode.Pessimistic)
				{
					Assert.IsFalse(context.Posts.Single(x => x.PostId == originalPostNumber).TimeDeleted != null);

					Assert.AreEqual(originalContent, context.Posts.Single(x => x.PostId == thread.Posts[0].PostNumber).ContentRaw);
				}
			}
		}

		[Test]
		public async Task CheckExistingPosts_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);

			async Task<ExistingThreadInfo> GetExistingThreadInfo()
			{
				var existingThreads =
					await consumer.CheckExistingThreads(new[] { threadPointer.ThreadId }, threadPointer.Board, false, MetadataMode.FullHashMetadata);

				return existingThreads[0];
			}

			threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash, await GetExistingThreadInfo());
			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			Assert.AreEqual(0, threadUpdate.NewPosts.Count);
			Assert.AreEqual(0, threadUpdate.UpdatedPosts.Count);


			thread.Posts[0].ContentRaw = "new content";

			threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash, await GetExistingThreadInfo());
			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			Assert.AreEqual(0, threadUpdate.NewPosts.Count);
			Assert.AreEqual(1, threadUpdate.UpdatedPosts.Count);
		}

		[Test]
		public async Task HandlesMovedPosts()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			await consumer.ConsumeThread(threadUpdate, false, false);

			async Task<ExistingThreadInfo> GetExistingThreadInfo()
			{
				var existingThreads =
					await consumer.CheckExistingThreads(new[] { threadPointer.ThreadId }, threadPointer.Board, false, MetadataMode.FullHashMetadata);

				return existingThreads[0];
			}

			threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash, await GetExistingThreadInfo());
			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			Assert.AreEqual(0, threadUpdate.NewPosts.Count);
			Assert.AreEqual(0, threadUpdate.UpdatedPosts.Count);


			thread.Posts[0].ContentRaw = "new content";

			threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash, await GetExistingThreadInfo());
			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

			Assert.AreEqual(0, threadUpdate.NewPosts.Count);
			Assert.AreEqual(1, threadUpdate.UpdatedPosts.Count);
		}

		//[Test]
		//public async Task HandlesBooleanArchivedTime()
		//{
		//	var options = TestCommon.CreateMemoryContextOptions();
		//	var mockFilesystem = new MockFileSystem();

		//	using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

		//	var (thread, threadPointer) = TestCommon.GenerateThread();

		//	var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
		//	var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);

		//	await consumer.ConsumeThread(threadUpdate);

		//	thread.ArchivedTime = DateTimeOffset.MinValue;

		//	threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
		//	await consumer.ConsumeThread(threadUpdate);

		//	await using (var context = new HaydenDbContext(options))
		//	{
		//		var dbThread = context.Threads.First();
		//		Assert.IsNotNull(dbThread.TimeArchived);
		//		Assert.IsTrue(DateTime.UtcNow - dbThread.TimeArchived < TimeSpan.FromSeconds(1));
		//	}
		//}
	}

	internal class TestHaydenConsumer : HaydenThreadConsumer
	{
		private Func<HaydenDbContext> GetContext { get; set; }

		public TestHaydenConsumer(ConsumerConfig consumerConfig, SourceConfig sourceConfig, Func<HaydenDbContext> getContext, IFileSystem fileSystem, IMediaInspector mediaInspector)
			: base(consumerConfig, sourceConfig, fileSystem, mediaInspector)
		{
			GetContext = getContext;
		}

		protected override HaydenDbContext GetDBContext() => GetContext();

		protected override void SetUpDBContext() { }

		public new ConsumerConfig ConsumerConfig => base.ConsumerConfig;
	}
}
