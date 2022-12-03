using Hayden.Cache;
using Hayden.Contract;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hayden.Tests.State
{
    public abstract class BaseStateStoreTests
    {
        protected abstract IStateStore CreateStateStore();

        [Test]
        public async Task WriteAndReadTest()
        {
            var stateStore = CreateStateStore();

            try
            {
                CollectionAssert.IsEmpty(await stateStore.GetDownloadQueue());

                var downloads = new List<QueuedImageDownload>
                {
                    new QueuedImageDownload(new Uri("http://example.org/fullimage.jpg"), new Uri("http://example.org/thumb.jpg"), new Dictionary<string, object>
                    {
                        ["testprop"] = "testvalue"
                    })
                };

                await stateStore.WriteDownloadQueue(downloads);

                var persistedQueue = await stateStore.GetDownloadQueue();
                CollectionAssert.AreEquivalent(downloads, persistedQueue);


                //Assert.AreEqual(downloads[0].Guid, persistedQueue[0].Guid);
                //Assert.AreEqual(downloads[0].FullImageUri.AbsoluteUri, persistedQueue[0].FullImageUri.AbsoluteUri);
                //Assert.AreEqual(downloads[0].ThumbnailImageUri.AbsoluteUri, persistedQueue[0].ThumbnailImageUri.AbsoluteUri);
                CollectionAssert.AreEquivalent(downloads[0].Properties, persistedQueue[0].Properties);

                var secondDownload = new QueuedImageDownload(new Uri("http://example.org/fullimage2.jpg"), new Uri("http://example.org/thumb2.jpg"), new Dictionary<string, object>
                {
                    ["testprop2"] = "testvalue2"
                });
                downloads.Add(secondDownload);

                await stateStore.InsertToDownloadQueue(new[] { secondDownload });
                persistedQueue = await stateStore.GetDownloadQueue();
                CollectionAssert.AreEquivalent(downloads, persistedQueue);

                var secondStoredDownload = persistedQueue.First(x => x.Guid == secondDownload.Guid);

                CollectionAssert.AreEquivalent(secondDownload.Properties, secondStoredDownload.Properties);


                await stateStore.WriteDownloadQueue(Array.Empty<QueuedImageDownload>());

                CollectionAssert.IsEmpty(await stateStore.GetDownloadQueue());
            }
            finally
            {
                if (stateStore is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    public class SqliteStateStoreTests : BaseStateStoreTests
    {
        protected override IStateStore CreateStateStore()
        {
            var connection = new SqliteConnection("Data Source=:memory:;");
            connection.Open();

            return new SqliteStateStore(connection);
        }
    }

    public class LiteDBStateStoreTests : BaseStateStoreTests
    {
        protected override IStateStore CreateStateStore()
        {
            return new LiteDbStateStore(new MemoryStream());
        }
    }
}
