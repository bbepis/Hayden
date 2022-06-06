using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using LiteDB;

namespace Hayden.Cache
{
	/// <summary>
	/// A state storage implementation using LiteDb.
	/// </summary>
	public class LiteDbStateStore : IStateStore
	{
		protected string FilePath { get; set; }

		public LiteDatabase Database { get; private set; }

		private LiteCollection<QueuedImageDownload> QueuedImageDownloads { get; set; }

		static LiteDbStateStore()
		{
			BsonMapper.Global.Entity<QueuedImageDownload>()
					  .Id(x => x.Guid);
		}

		public LiteDbStateStore(string filepath)
		{
			FilePath = filepath;

			Database = new LiteDatabase(FilePath);

			QueuedImageDownloads = Database.GetCollection<QueuedImageDownload>("QueuedImageDownloads");
		}

		/// <inheritdoc/>
		public Task WriteDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			foreach (var item in QueuedImageDownloads.FindAll())
			{
				if (!imageDownloads.Contains(item))
				{
					QueuedImageDownloads.Delete(item.Guid);
				}
			}

			foreach (var item in imageDownloads)
			{
				if (!QueuedImageDownloads.Exists(x => x.Guid == item.Guid))
				{
					QueuedImageDownloads.Insert(item);
				}
			}

			Program.Log("Shrinking state database...", true);

			Database.Shrink();

			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public Task InsertToDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			lock (QueuedImageDownloads)
				QueuedImageDownloads.Upsert(imageDownloads);

			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return Task.FromResult((IList<QueuedImageDownload>)QueuedImageDownloads.FindAll().ToArray());
		}
	}
}