using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using Hayden.Proxy;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Hayden.Tests.Archivers
{
    internal class BoardArchiverTests
    {
        private readonly SourceConfig SourceConfig = new SourceConfig()
        {
            Boards =
			[
				new BoardConfig("a"),
				new BoardConfig("b"),
				new BoardConfig("c"),
            ],
            ApiDelay = 0,
            BoardScrapeDelay = 0
        };
        private readonly ConsumerConfig ConsumerConfig = new ConsumerConfig();

		private Dictionary<ThreadPointer, ThreadOverviewInfo> CreateMockThreadData()
		{
			var sampleThreads = new[]
			{
				new ThreadPointer("a", 123),
				new ThreadPointer("a", 126),
				new ThreadPointer("a", 128),
				new ThreadPointer("b", 170),
				new ThreadPointer("c", 1456),
			};

			var seededRandom = new Random(1234);

			return sampleThreads.ToDictionary(x => x,
				x => new ThreadOverviewInfo
				{
					ThreadId = x.ThreadId,
					ContentHtml = $"thread{x.ThreadId}",
					Subject = null,
					Position = 0, // shouldn't matter?
					LastModified = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(seededRandom.Next(5, 500)),
					ReplyCount = seededRandom.Next(5, 500)
				});
		}

        private (Mock<IThreadConsumer> consumerMock, Mock<IFrontendApi> sourceMock) CreateMocks(
			Dictionary<ThreadPointer, ThreadOverviewInfo> mockData, bool replyCountMode = false)
        {
            var consumerMock = new Mock<IThreadConsumer>(MockBehavior.Strict);
            var sourceMock = new Mock<IFrontendApi>(MockBehavior.Strict);

            consumerMock.Setup(x => x.CheckExistingThreads(It.IsAny<IEnumerable<ulong>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<MetadataMode>(), It.IsAny<bool>()))
                .Returns(Task.FromResult<IList<ExistingThreadInfo>>(Array.Empty<ExistingThreadInfo>()));

            sourceMock.Setup(x => x.GetBoard(It.IsAny<string>(), It.IsAny<HttpClient>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .Returns((string board, HttpClient client, DateTimeOffset? since, CancellationToken token) => {

					if (SourceConfig.Boards.All(x => x.Board != board))
						throw new ArgumentOutOfRangeException("board", "Board argument was not expected");

					var threadInfos = mockData
						.Where(x => x.Key.Board == board)
						.Select(x => x.Value)
						.ToArray();

                    return Task.FromResult(new ApiResponse<ThreadOverviewInfo[]>(ResponseType.Ok, threadInfos));
                });

			sourceMock.Setup(x => x.GetThread(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<HttpClient>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
				.Returns((string board, ulong threadId, HttpClient client, DateTimeOffset? since, CancellationToken token) =>
				{
					var overviewInfo = mockData[new ThreadPointer(board, threadId)];

					return Task.FromResult(new ApiResponse<Models.Thread>(ResponseType.Ok, new Models.Thread
					{
						ThreadId = threadId,
						Posts = new[]
						{
							new Post
							{
								PostNumber = 1234,
								ContentRaw = $"thread{threadId}",
								Media = [
									new Media()
									{
										FileUrl = "http://example.com/fullimage.jpg",
										ThumbnailUrl = "http://example.com/thumb.jpg"
                                    }
								]
							},
							new Post
							{
								PostNumber = 1235,
								ContentRaw = "test2",
								Media = []
							},
							new Post
							{
								PostNumber = 1236,
								ContentRaw = "test3",
								Media = []
							},
						}
					}));
				});

			sourceMock.Setup(x => x.DetermineCapabilitiesAsync(It.IsAny<HttpClient>()))
				.Returns((HttpClient client) =>
				{
					return Task.FromResult(new ApiCapabilities
					{
						SupportsBoardLastModified = !replyCountMode,
						SupportsBoardReplyCount = replyCountMode
					});
				});

            return (consumerMock, sourceMock);
        }

        private void SetupConsumerMock(Mock<IThreadConsumer> consumerMock, MockFileSystem fileSystem, List<(QueuedImageDownload download, string tempFilePath, string tempThumbPath)> processedFiles)
        {
	        consumerMock.Setup(x => x.CalculateHash(It.IsAny<Post>()))
		        .Returns((Post post) => (uint)post.PostNumber);

	        consumerMock.Setup(x => x.ConsumeThread(It.IsAny<ThreadUpdateInfo>(), It.IsAny<bool>(), It.IsAny<bool>()))
		        .Returns((ThreadUpdateInfo updateInfo, bool fullImages, bool thumbnails) => Task.FromResult<IList<QueuedImageDownload>>(updateInfo.NewPosts
			        .SelectMany(x => fullImages || thumbnails ? x.Media : [], (post, media) => new QueuedImageDownload(
				        fullImages ? new Uri(media.FileUrl) : null,
				        thumbnails ? new Uri(media.ThumbnailUrl) : null))
			        .ToArray()));

	        consumerMock.Setup(x => x.ProcessFileDownload(It.IsAny<QueuedImageDownload>(), It.IsAny<string>(), It.IsAny<string>()))
		        .Callback((QueuedImageDownload imageDownload, string tempFilePath, string tempThumbPath) =>
		        {
			        //Assert.IsTrue(fileSystem.FileExists(tempFilePath));
			        //Assert.IsTrue(fileSystem.FileExists(tempThumbPath));

			        processedFiles.Add((imageDownload, tempFilePath, tempThumbPath));
		        })
		        .Returns(Task.CompletedTask);
        }

        [Test, CancelAfter(10_000)]
        public async Task EnqueuesThreadsWhenExpected()
        {
			var mockData = CreateMockThreadData();
            var (consumerMock, sourceMock) = CreateMocks(mockData);
            var fileSystem = new MockFileSystem();

            var cts = new CancellationTokenSource();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);
			await boardArchiver.Initialize();

            var threadList = await boardArchiver.ReadBoards(true, cts.Token);

            CollectionAssert.AreEquivalent(mockData.Keys, await threadList.ToListAsync());

            threadList = await boardArchiver.ReadBoards(false, cts.Token);

            CollectionAssert.IsEmpty(await threadList.ToListAsync());
        }

        [Test, CancelAfter(10_000)]
        public async Task ProcessesThreadFilters()
        {
			var mockData = CreateMockThreadData();
            var (consumerMock, sourceMock) = CreateMocks(mockData);

            var imageQueue = new List<(QueuedImageDownload download, string tempFilePath, string tempThumbPath)>();

			SetupConsumerMock(consumerMock, new MockFileSystem(), imageQueue);

            var fileSystem = new MockFileSystem();

            var cts = new CancellationTokenSource();

            var sourceConfig = new SourceConfig()
            {
	            Boards =
	            [
		            new BoardConfig("a"),
		            new BoardConfig("b"),
		            new BoardConfig("c"),
	            ],
	            Filters = [
					new ThreadFilter( new ThreadFilterConfig { Board = "a", AnyFilter = "thread123", FullImages = true, Thumbnails = false }),
					//new ThreadFilter("*", new ThreadFilterConfig { AnyFilter = "thread2", FullImages = false, Thumbnails = true }),
				],
	            ApiDelay = 0,
	            BoardScrapeDelay = 0
            };

			var boardArchiver = new BoardArchiverTestable(sourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);
			await boardArchiver.Initialize();

            var threadQueue = await boardArchiver.ReadBoards(true, cts.Token);

            await boardArchiver.PerformScrape(true, threadQueue, new List<QueuedImageDownload>(), CancellationToken.None);

			Assert.AreEqual(1, imageQueue.Count);

			Assert.IsNotNull(imageQueue[0].tempFilePath);
			Assert.IsNull(imageQueue[0].tempThumbPath);
        }

        [Test, CancelAfter(10_000)]
        public async Task EnqueuesThreadsThatArePresumedMissing()
        {
	        var mockData = CreateMockThreadData();
            var (consumerMock, sourceMock) = CreateMocks(mockData);
            var fileSystem = new MockFileSystem();

            var cts = new CancellationTokenSource();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);
			await boardArchiver.Initialize();

			var fallenOffThread = new ThreadPointer("a", 999);

            boardArchiver.TrackedThreads.Add(fallenOffThread, TrackedThread.StartTrackingThread(p => 0));

            var threadList = await boardArchiver.ReadBoards(true, cts.Token);

            CollectionAssert.AreEquivalent(mockData.Keys.Append(fallenOffThread), await threadList.ToListAsync());
		}

		[CancelAfter(10_000)]
		[TestCase(false, TestName = "ScrapesThreads - LastModified mode")]
		[TestCase(true, TestName = "ScrapesThreads - ReplyCount mode")]
        public async Task ScrapesThreads(bool replyCountMode)
        {
	        var mockData = CreateMockThreadData();
            var (consumerMock, sourceMock) = CreateMocks(mockData, replyCountMode);

			// make sure that it's not accidentally using the other data, when it might not be available in actual usage
			foreach (var thread in mockData.Values)
			{
				if (replyCountMode)
					thread.LastModified = null;
				else
					thread.ReplyCount = null;
			}


			var fileSystem = new MockFileSystem();
			var processedFiles = new List<(QueuedImageDownload download, string tempFilePath, string tempThumbPath)>();

			SetupConsumerMock(consumerMock, fileSystem, processedFiles);

			var cts = new CancellationTokenSource();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);
			await boardArchiver.Initialize();

			var threadList = await boardArchiver.ReadBoards(true, cts.Token);

            CollectionAssert.AreEquivalent(mockData.Keys, await threadList.ToListAsync());

            threadList = await boardArchiver.ReadBoards(false, cts.Token);

            CollectionAssert.IsEmpty(await threadList.ToListAsync());

			var updatedThreadPointer = mockData.Keys.First();

			if (replyCountMode)
				mockData[updatedThreadPointer].ReplyCount += 10;
			else
				mockData[updatedThreadPointer].LastModified = DateTimeOffset.Now;

			threadList = await boardArchiver.ReadBoards(false, cts.Token);

			CollectionAssert.AreEquivalent(new[] { updatedThreadPointer }, await threadList.ToListAsync());
        }

        [CancelAfter(10_000)]
		[Test]
        public async Task MeasuresMetrics()
        {
	        var mockData = CreateMockThreadData();
            var (consumerMock, sourceMock) = CreateMocks(mockData);

			var fileSystem = new MockFileSystem();
			var processedFiles = new List<(QueuedImageDownload download, string tempFilePath, string tempThumbPath)>();

			SetupConsumerMock(consumerMock, fileSystem, processedFiles);

			var metrics = new ScraperMetrics();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem, metrics: metrics);
			await boardArchiver.Initialize();

			//var threadList = await boardArchiver.ReadBoards(true, cts.Token);

			await boardArchiver.PerformScrape(false,
				new MaybeAsyncEnumerable<ThreadPointer>(mockData.Keys.ToList()),
				new(), CancellationToken.None);

			var memoryStream = new MemoryStream();
			await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(memoryStream);
			var str = Encoding.UTF8.GetString(memoryStream.ToArray());
			Console.WriteLine(str);

			Assert.AreEqual(3d, metrics.TotalThreadsScraped.WithLabels("a").Value);
			Assert.AreEqual(1d, metrics.TotalThreadsScraped.WithLabels("b").Value);
			Assert.AreEqual(1d, metrics.TotalThreadsScraped.WithLabels("c").Value);
        }

        private class BoardArchiverTestable : BoardArchiver
        {
            public BoardArchiverTestable(SourceConfig sourceConfig, ConsumerConfig consumerConfig,
                IFrontendApi frontendApi, IThreadConsumer threadConsumer, IFileSystem fileSystem,
                IStateStore stateStore = null, ProxyProvider proxyProvider = null, ScraperMetrics metrics = null) 
                : base(sourceConfig, consumerConfig, frontendApi, threadConsumer, fileSystem, stateStore, proxyProvider, metrics)
            {
            }

            public new Task<(List<ThreadPointer> requeuedThreads, List<QueuedImageDownload> requeuedImages)> PerformScrape(bool firstRun, MaybeAsyncEnumerable<ThreadPointer> threadQueue, List<QueuedImageDownload> additionalImages, CancellationToken token)
            {
                return base.PerformScrape(firstRun, threadQueue, additionalImages, token);
            }

            public new Task<MaybeAsyncEnumerable<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
            {
                return base.ReadBoards(firstRun, token);
            }

            protected override Task<string> DownloadFileTask(Uri imageUrl, HttpClient httpClient)
            {
	            return Task.FromResult(imageUrl.ToString());
            }

            public new SortedList<ThreadPointer, TrackedThread> TrackedThreads => base.TrackedThreads;
        }
    }
}
