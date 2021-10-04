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
					  .Id(x => x.DownloadPath);
		}

		public LiteDbStateStore(string filepath)
		{
			FilePath = filepath;

			Database = new LiteDatabase(FilePath);

			QueuedImageDownloads = Database.GetCollection<QueuedImageDownload>("QueuedImageDownloads");
		}

		/// <inheritdoc/>
		public async Task WriteDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			foreach (var item in QueuedImageDownloads.FindAll())
			{
				if (!imageDownloads.Contains(item))
				{
					QueuedImageDownloads.Delete(item.DownloadPath);
				}
			}

			foreach (var item in imageDownloads)
			{
				if (!QueuedImageDownloads.Exists(x => x.DownloadPath == item.DownloadPath))
				{
					QueuedImageDownloads.Insert(item);
				}
			}

			Program.Log("Shrinking state database...", true);

			Database.Shrink();
		}

		/// <inheritdoc/>
		public async Task InsertToDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			lock (QueuedImageDownloads)
				QueuedImageDownloads.Upsert(imageDownloads);
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return QueuedImageDownloads.FindAll().ToArray();
		}
	}
}