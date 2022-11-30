using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using Hayden.Proxy;
using Moq;
using NUnit.Framework;

namespace Hayden.Tests.Archivers
{
    internal class BoardArchiverTests
    {
        private readonly SourceConfig SourceConfig = new SourceConfig()
        {
            Boards = new()
            {
                ["a"] = new BoardRulesConfig(),
                ["b"] = new BoardRulesConfig(),
                ["c"] = new BoardRulesConfig(),
            }
        };
        private readonly ConsumerConfig ConsumerConfig = new ConsumerConfig();

        private readonly ThreadPointer[] ExpectedThreads = new[]
        {
            new ThreadPointer("a", 123),
            new ThreadPointer("a", 126),
            new ThreadPointer("a", 128),
            new ThreadPointer("b", 170),
            new ThreadPointer("c", 1456),
        };

        private (Mock<IThreadConsumer> consumerMock, Mock<IFrontendApi> sourceMock) CreateMocks()
        {
            var consumerMock = new Mock<IThreadConsumer>(MockBehavior.Strict);
            var sourceMock = new Mock<IFrontendApi>(MockBehavior.Strict);

            var timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();

            consumerMock.Setup(x => x.CheckExistingThreads(It.IsAny<IEnumerable<ulong>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(Task.FromResult<ICollection<ExistingThreadInfo>>(Array.Empty<ExistingThreadInfo>()));

            sourceMock.Setup(x => x.GetBoard(It.IsAny<string>(), It.IsAny<HttpClient>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .Returns((string board, HttpClient client, DateTimeOffset? since, CancellationToken token) => {

                    PageThread[] pageThreads;

                    switch (board)
                    {
                        case "a":
                        case "b":
                        case "c":
                            pageThreads = ExpectedThreads
                                .Where(x => x.Board == board)
                                .Select(x => new PageThread(x.ThreadId, timestamp, "", ""))
                                .ToArray();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("Board argument was not expected");
                    }

                    return Task.FromResult(new ApiResponse<PageThread[]>(ResponseType.Ok, pageThreads));
                });

            return (consumerMock, sourceMock);
        }

        [Test, Timeout(10_000)]
        public async Task EnqueuesThreadsWhenExpected()
        {
            var (consumerMock, sourceMock) = CreateMocks();
            var fileSystem = new MockFileSystem();

            var cts = new CancellationTokenSource();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);

            var threadList = await boardArchiver.ReadBoards(true, cts.Token);

            CollectionAssert.AreEquivalent(ExpectedThreads, threadList);

            threadList = await boardArchiver.ReadBoards(false, cts.Token);

            CollectionAssert.IsEmpty(threadList);
        }

        [Test, Timeout(10_000)]
        public async Task ScrapesThreads()
        {
            var (consumerMock, sourceMock) = CreateMocks();

            var fileSystem = new MockFileSystem();

            consumerMock.Setup(x => x.CalculateHash(It.IsAny<Post>()))
                .Returns((Post post) => post.PostNumber);

            consumerMock.Setup(x => x.ConsumeThread(It.IsAny<ThreadUpdateInfo>()))
                .Returns((ThreadUpdateInfo updateInfo) => updateInfo.NewPosts
                    .SelectMany(x => x.Media, (post, media) => new QueuedImageDownload(new Uri(media.FileUrl), new Uri(media.ThumbnailUrl)))
                    .ToArray());

            var processedFiles = new List<(QueuedImageDownload download, string tempFilePath, string tempThumbPath)>();

            consumerMock.Setup(x => x.ProcessFileDownload(It.IsAny<QueuedImageDownload>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((QueuedImageDownload imageDownload, string tempFilePath, string tempThumbPath) =>
                {
                    Assert.IsTrue(fileSystem.FileExists(tempFilePath));
                    Assert.IsTrue(fileSystem.FileExists(tempThumbPath));

                    processedFiles.Add((imageDownload, tempFilePath, tempThumbPath));
                })
                .Returns(Task.CompletedTask);

            var cts = new CancellationTokenSource();

            var boardArchiver = new BoardArchiverTestable(SourceConfig, ConsumerConfig, sourceMock.Object, consumerMock.Object, fileSystem);

            var threadList = await boardArchiver.ReadBoards(true, cts.Token);

            CollectionAssert.AreEquivalent(ExpectedThreads, threadList);

            threadList = await boardArchiver.ReadBoards(false, cts.Token);

            CollectionAssert.IsEmpty(threadList);
        }

        private class BoardArchiverTestable : BoardArchiver
        {
            public BoardArchiverTestable(SourceConfig sourceConfig, ConsumerConfig consumerConfig,
                IFrontendApi frontendApi, IThreadConsumer threadConsumer, IFileSystem fileSystem,
                IStateStore stateStore = null, ProxyProvider proxyProvider = null) 
                : base(sourceConfig, consumerConfig, frontendApi, threadConsumer, fileSystem, stateStore, proxyProvider)
            {
            }

            public new Task<(List<ThreadPointer> requeuedThreads, List<QueuedImageDownload> requeuedImages)> PerformScrape(bool firstRun, List<ThreadPointer> threadQueue, List<QueuedImageDownload> additionalImages, CancellationToken token)
            {
                return base.PerformScrape(firstRun, threadQueue, additionalImages, token);
            }

            public new Task<List<ThreadPointer>> ReadBoards(bool firstRun, CancellationToken token)
            {
                return base.ReadBoards(firstRun, token);
            }
        }
    }
}
