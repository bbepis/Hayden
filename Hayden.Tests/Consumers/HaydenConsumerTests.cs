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
using Hayden.MediaInfo;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hayden.Tests.Consumers
{
	internal class HaydenConsumerTests
	{
		const string file1path = @"C:\temp\temp1.jpg";
		const string file1Thumbpath = @"C:\temp\temp1-thumb.jpg";
		const string file2path = @"C:\temp\temp2.png";
		const string file2Thumbpath = @"C:\temp\temp2-thumb.webp";

		static readonly string[] ExpectedPathList = new[]
		{
			@"C:\temp\hayden_test\test\image\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\test\thumb\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
			@"C:\temp\hayden_test\test\image\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.png",
			@"C:\temp\hayden_test\test\thumb\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.webp"
		};

		private static async Task<TestHaydenConsumer> CreateHaydenConsumerAsync(DbContextOptions<HaydenDbContext> options, IFileSystem fileSystem, Action<ConsumerConfig, SourceConfig>? setupConfig = null)
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
				Boards = new Dictionary<string, BoardRulesConfig>(),
				BoardScrapeDelay = 0,
				ApiDelay = 0
			};

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
			Assert.AreEqual(post.IsDeleted, dbPost.IsDeleted);

			if (post.AdditionalMetadata.Serialize() == null)
				Assert.AreEqual(null, dbPost.AdditionalMetadata);
			else
				Assert.IsTrue(JToken.DeepEquals(JToken.Parse(post.AdditionalMetadata.Serialize()), JToken.Parse(dbPost.AdditionalMetadata)));
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

			// we want the file id to be null here as we haven't actually downloaded the file yet
			Assert.AreEqual(null, dbFileMapping.FileId);
			
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
			threadUpdate.IsNewThread = true;

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate);

			Assert.AreEqual(2, pendingDownloads.Count);
			AssertDataIsSame(thread.Posts[0], pendingDownloads[0]);
			AssertDataIsSame(thread.Posts[0], pendingDownloads[1]);


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
				
				var threads = context.Threads.ToArray();

				Assert.AreEqual(1, threads.Length);
				Assert.AreEqual(thread.ThreadId, threads[0].ThreadId);
				Assert.AreEqual(thread.IsArchived, threads[0].IsArchived);
				Assert.AreEqual(thread.Title, threads[0].Title);
				Assert.AreEqual(null, threads[0].AdditionalMetadata);
				Assert.AreEqual(false, threads[0].IsDeleted);
			}
			
			async Task testFile(Media media)
			{
				await using var context = new HaydenDbContext(options);

				var fileMapping = context.FileMappings.Single(x => x.BoardId == 1
															   && x.PostId == thread.Posts[0].PostNumber
															   && x.Index == media.Index);

				Assert.IsNotNull(fileMapping.FileId);

				var file = context.Files.Single(x => x.Id == fileMapping.FileId && x.BoardId == fileMapping.BoardId);

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
			
			CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}
		
	//    [TestCase(false, true)]
	//    public async Task IngestNewPostsTest_ImagesDisabled(bool fullImagesDisabled, bool thumbnailsDisabled)
	//    {
	//        var options = TestCommon.CreateMemoryContextOptions();
	//        var mockFilesystem = new MockFileSystem();

	//        using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem, (cConfig, sConfig) =>
	//        {
				//cConfig.FullImagesEnabled = !fullImagesDisabled;
				//cConfig.ThumbnailsEnabled = !thumbnailsDisabled;
	//        });

	//        var (thread, threadPointer) = TestCommon.GenerateThread();

	//        var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
	//        var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
	//        threadUpdate.IsNewThread = true;

	//        var pendingDownloads = await consumer.ConsumeThread(threadUpdate);

	//        Assert.AreEqual(2, pendingDownloads.Count);
	//        AssertDataIsSame(thread.Posts[0], pendingDownloads[0]);
	//        AssertDataIsSame(thread.Posts[0], pendingDownloads[1]);


	//        await using (var context = new HaydenDbContext(options))
	//        {
	//            var posts = context.Posts.ToList();

	//            Assert.AreEqual(2, posts.Count);
	//            AssertDataIsSame(thread, posts[0]);
	//            AssertDataIsSame(thread, posts[1]);

	//            var mappings = context.FileMappings.ToArray();

	//            Assert.AreEqual(2, mappings.Length);
	//            AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[0], mappings);
	//            AssertDataIsSame(thread.Posts[0], thread.Posts[0].Media[1], mappings);

	//            var threads = context.Threads.ToArray();

	//            Assert.AreEqual(1, threads.Length);
	//            Assert.AreEqual(thread.ThreadId, threads[0].ThreadId);
	//            Assert.AreEqual(thread.IsArchived, threads[0].IsArchived);
	//            Assert.AreEqual(thread.Title, threads[0].Title);
	//            Assert.AreEqual(null, threads[0].AdditionalMetadata);
	//            Assert.AreEqual(false, threads[0].IsDeleted);
	//        }

	//        const string file1path = @"C:\temp\temp1.jpg";
	//        const string file1Thumbpath = @"C:\temp\temp1-thumb.jpg";
	//        const string file2path = @"C:\temp\temp2.png";
	//        const string file2Thumbpath = @"C:\temp\temp2-thumb.webp";

	//        mockFilesystem.AddFileFromEmbeddedResource(file1path, typeof(HaydenConsumerTests).Assembly, "Hayden.Tests.TestImages.1.jpg");
	//        mockFilesystem.AddFileFromEmbeddedResource(file1Thumbpath, typeof(HaydenConsumerTests).Assembly, "Hayden.Tests.TestImages.1-thumb.jpg");

	//        mockFilesystem.AddFileFromEmbeddedResource(file2path, typeof(HaydenConsumerTests).Assembly, "Hayden.Tests.TestImages.2.png");
	//        mockFilesystem.AddFileFromEmbeddedResource(file2Thumbpath, typeof(HaydenConsumerTests).Assembly, "Hayden.Tests.TestImages.2-thumb.webp");


	//        async Task testFile(Media media)
	//        {
	//            await using var context = new HaydenDbContext(options);

	//            var fileMapping = context.FileMappings.Single(x => x.BoardId == 1
	//                                                           && x.PostId == thread.Posts[0].PostNumber
	//                                                           && x.Index == media.Index);

	//            Assert.IsNotNull(fileMapping.FileId);

	//            var file = context.Files.Single(x => x.Id == fileMapping.FileId && x.BoardId == fileMapping.BoardId);

	//            Assert.AreEqual(media.FileExtension, file.Extension);
	//            Assert.AreEqual(media.ThumbnailExtension, file.ThumbnailExtension);

	//            Assert.AreEqual(24, file.ImageHeight);
	//            Assert.AreEqual(24, file.ImageWidth);

	//            Assert.IsFalse(file.FileBanned);
	//            Assert.IsTrue(file.FileExists);
	//        }

	//        await consumer.ProcessFileDownload(pendingDownloads.First(x =>
	//            x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

	//        await testFile(thread.Posts[0].Media[0]);

	//        await consumer.ProcessFileDownload(pendingDownloads.First(x =>
	//            x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

	//        await testFile(thread.Posts[0].Media[1]);

	//        var expectedPathList = new[]
	//        {
	//            @"C:\temp\hayden_test\test\image\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
	//            @"C:\temp\hayden_test\test\thumb\g1rpbse4hsaikw0mac17fi5qn76jylm5zsgne31vf6s366n0j3.jpg",
	//            @"C:\temp\hayden_test\test\image\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.png",
	//            @"C:\temp\hayden_test\test\thumb\y8cdt4omqc6v5opfkx7askwvnd2pru5im985aa8mbh61rzaig2.webp"
	//        };
	//        CollectionAssert.AreEquivalent(expectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
	//    }

		[Test]
		public async Task IngestPosts_ZeroFileId_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			threadUpdate.IsNewThread = true;

			consumer.ConsumerConfig.FullImagesEnabled = false;
			consumer.ConsumerConfig.ThumbnailsEnabled = false;

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate);

			Assert.AreEqual(0, pendingDownloads.Count);

			await using (var context = new HaydenDbContext(options))
			{
				var mappings = context.FileMappings.ToArray();

				Assert.AreEqual(2, mappings.Length);

				var replyPost = thread.Posts[1];

				foreach (var media in replyPost.Media)
				{
					//missing_sha256hash
					var mapping = mappings.Single(x => x.PostId == replyPost.PostNumber && x.Index == media.Index);

					Assert.AreEqual(null, mapping.FileId);

					var jobj = JObject.Parse(mapping.AdditionalMetadata);

					Assert.AreEqual(media.FileExtension, jobj["missing_extension"]);
					Assert.AreEqual(media.FileSize, jobj["missing_size"]);

					Assert.AreEqual(Convert.ToBase64String(media.Sha256Hash), jobj["missing_sha256hash"]);
					Assert.AreEqual(Convert.ToBase64String(media.Sha1Hash), jobj["missing_sha1hash"]);
					Assert.AreEqual(Convert.ToBase64String(media.Md5Hash), jobj["missing_md5hash"]);
				}
			}

			consumer.ConsumerConfig.FullImagesEnabled = true;
			consumer.ConsumerConfig.ThumbnailsEnabled = true;

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			pendingDownloads = await consumer.ConsumeThread(threadUpdate);

			Assert.AreEqual(2, pendingDownloads.Count);
			
			CreateMockTempFiles(mockFilesystem);

			async Task testFile(Media media)
			{
				await using var context = new HaydenDbContext(options);

				var fileMapping = context.FileMappings.Single(x => x.BoardId == 1
															   && x.PostId == thread.Posts[0].PostNumber
															   && x.Index == media.Index);

				Assert.IsNotNull(fileMapping.FileId);

				var file = context.Files.Single(x => x.Id == fileMapping.FileId && x.BoardId == fileMapping.BoardId);

				Assert.AreEqual(media.FileExtension, file.Extension);
				Assert.AreEqual(media.ThumbnailExtension, file.ThumbnailExtension);

				Assert.AreEqual(24, file.ImageHeight);
				Assert.AreEqual(24, file.ImageWidth);

				Assert.IsFalse(file.FileBanned);
				Assert.IsTrue(file.FileExists);
			}

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await testFile(thread.Posts[0].Media[0]);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			await testFile(thread.Posts[0].Media[1]);
			
			CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
		}

		[Test]
		public async Task IngestPosts_NoFileExists_Test()
		{
			var options = TestCommon.CreateMemoryContextOptions();
			var mockFilesystem = new MockFileSystem();

			using var consumer = await CreateHaydenConsumerAsync(options, mockFilesystem);

			var (thread, threadPointer) = TestCommon.GenerateThread();

			var threadTracker = TrackedThread.StartTrackingThread(consumer.CalculateHash);
			var threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			threadUpdate.IsNewThread = true;

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate);

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
					context.Update(file);
				}

				await context.SaveChangesAsync();
			}

			threadUpdate = threadTracker.ProcessThreadUpdates(threadPointer, thread);
			pendingDownloads = await consumer.ConsumeThread(threadUpdate);
			
			Assert.AreEqual(2, pendingDownloads.Count);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			CollectionAssert.AreEquivalent(ExpectedPathList, mockFilesystem.AllFiles, "Unexpected changes were made to the filesystem.");
			
			await using (var context = new HaydenDbContext(options))
			{
				foreach (var file in context.Files.ToArray())
				{
					Assert.IsTrue(file.FileExists);
				}
			}
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
			threadUpdate.IsNewThread = true;

			var pendingDownloads = await consumer.ConsumeThread(threadUpdate);
			
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
			pendingDownloads = await consumer.ConsumeThread(threadUpdate);

			// this section is a result of me being too lazy to get the correct hashes for the files in the CreateThread() method.
			// if that gets corrected this block of code will break, and should be removed
			Assert.AreEqual(2, pendingDownloads.Count);

			CreateMockTempFiles(mockFilesystem);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[0].FileUrl), file1path, file1Thumbpath);

			await consumer.ProcessFileDownload(pendingDownloads.First(x =>
				x.FullImageUri.AbsoluteUri == thread.Posts[0].Media[1].FileUrl), file2path, file2Thumbpath);

			await using (var context = new HaydenDbContext(options))
			{
				Assert.AreEqual(3, await context.Posts.CountAsync());
				Assert.AreEqual(4, await context.FileMappings.CountAsync());

				if (consolidationMode == ConsolidationMode.Authoritative)
				{
					Assert.IsTrue(context.Posts.Single(x => x.PostId == originalPostNumber).IsDeleted);
					
					Assert.AreEqual("abcd", context.Posts.Single(x => x.PostId == thread.Posts[0].PostNumber).ContentRaw);
				}
				else if (consolidationMode == ConsolidationMode.Pessimistic)
				{
					Assert.IsFalse(context.Posts.Single(x => x.PostId == originalPostNumber).IsDeleted);

					Assert.AreEqual(originalContent, context.Posts.Single(x => x.PostId == thread.Posts[0].PostNumber).ContentRaw);
				}
			}
		}
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
