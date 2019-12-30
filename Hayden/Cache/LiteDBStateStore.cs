using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using LiteDB;

namespace Hayden.Cache
{
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

		public async Task WriteDownloadQueue(IList<QueuedImageDownload> imageDownloads)
		{
			List<QueuedImageDownload> downloads = QueuedImageDownloads.FindAll().ToList();

			QueuedImageDownloads.Insert(imageDownloads.Except(downloads));

			foreach (var removedItem in downloads.Where(x => imageDownloads.All(y => !Equals(x, y))))
				QueuedImageDownloads.Delete(removedItem.DownloadPath);
		}

		public async Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return QueuedImageDownloads.FindAll().ToArray();
		}
	}
}