﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using LiteDB;

namespace Hayden.Cache
{
	/// <summary>
	/// A state storage implementation using LiteDb.
	/// </summary>
	public class LiteDbStateStore : IStateStore, IDisposable
	{
		public LiteDatabase Database { get; private set; }

		protected Stream Stream { get; set; }

		private LiteCollection<QueuedImageDownload> QueuedImageDownloads { get; set; }

		static LiteDbStateStore()
		{
			BsonMapper.Global.Entity<QueuedImageDownload>()
					  .Id(x => x.Guid);
		}

		public LiteDbStateStore(string filepath)
		{
			Database = new LiteDatabase(filepath);

			QueuedImageDownloads = Database.GetCollection<QueuedImageDownload>("QueuedImageDownloads");
		}

		public LiteDbStateStore(Stream stream)
		{
			Database = new LiteDatabase(stream);
			Stream = stream;

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

        public void Dispose()
        {
            Database.Dispose();
			Stream?.Dispose();
        }
    }
}