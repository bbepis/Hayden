using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nito.AsyncEx;
using Serilog;

namespace Hayden.Cache
{
	/// <summary>
	/// A state storage implementation using SQLite.
	/// </summary>
	public class SqliteStateStore : IStateStore, IDisposable
	{
		protected class SqliteStateContext : DbContext
		{ 
			public DbSet<QueuedImageDownload> QueuedImageDownloads { get; set; }
			public DbSet<KeyValue> KeyValues { get; set; }

			public SqliteStateContext(DbContextOptions options) : base(options)
			{
				Database.EnsureCreated();
			}

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
				//base.OnModelCreating(modelBuilder);

				modelBuilder.Entity<QueuedImageDownload>(x =>
				{
					x.ToTable("QueuedImageDownload");

					x.Property(x => x.FullImageUri)
						.HasConversion(
							v => v.AbsoluteUri,
							v => new Uri(v));

					x.Property(x => x.ThumbnailImageUri)
						.HasConversion(
							v => v.AbsoluteUri,
							v => new Uri(v));

					x.Property(x => x.Properties)
						.HasConversion(
							v => JsonConvert.SerializeObject(v),
							v => JsonConvert.DeserializeObject<Dictionary<string, object>>(v));

					x.HasKey(x => x.Guid);
				});

				modelBuilder.Entity<KeyValue>(x =>
				{
					x.ToTable("KeyValue");

					x.HasKey(k => k.Key);
				});
            }

            public void DetachAllEntities()
            {
                var changedEntriesCopy = ChangeTracker.Entries()
                    .ToList();

                foreach (var entry in changedEntriesCopy)
                    entry.State = EntityState.Detached;
            }

            public class KeyValue
            {
				public string Key { get; set; }
				public string Value { get; set; }
            }
		}

		private AsyncLock @lock { get; } = new();

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("SQLite");

		protected SqliteConnection Connection { get; set; }

        protected SqliteStateContext Context { get; set; }

		public SqliteStateStore(string filepath)
		{
			Context = new SqliteStateContext(new DbContextOptionsBuilder<SqliteStateContext>()
				.UseSqlite(filepath)
				.Options);
        }

		public SqliteStateStore(SqliteConnection connection)
		{
			Connection = connection;

            Context = new SqliteStateContext(new DbContextOptionsBuilder<SqliteStateContext>()
				.UseSqlite(connection)
				.Options);

            //Context.Database.EnsureCreated();
        }

		/// <inheritdoc/>
		public async Task WriteDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			using var lockObj = await @lock.LockAsync();
			
			var existingGuids = new HashSet<Guid>();
			var incomingGuids = new HashSet<Guid>(imageDownloads.Select(x => x.Guid));

			await foreach (var item in Context.QueuedImageDownloads.AsNoTracking().AsAsyncEnumerable())
			{
				if (!incomingGuids.Contains(item.Guid))
				{
					Context.QueuedImageDownloads.Remove(item);
				}

				existingGuids.Add(item.Guid);
            }

			foreach (var item in imageDownloads)
			{
				if (!existingGuids.Contains(item.Guid))
				{
					Context.QueuedImageDownloads.Add(item);
				}
            }

            await Context.SaveChangesAsync();
			Context.DetachAllEntities();

            Logger.Debug("Shrinking state database...");

			await Context.Database.ExecuteSqlRawAsync("VACUUM;");
		}

		/// <inheritdoc/>
		public async Task InsertToDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			using var lockObj = await @lock.LockAsync();

			Context.AddRange(imageDownloads);

			await Context.SaveChangesAsync();
            Context.DetachAllEntities();
        }

		public async Task StoreKeyValue(string key, string value)
		{
			var existingObj = await Context.KeyValues.FirstOrDefaultAsync(x => x.Key == key);

			if (existingObj != null)
			{
				if (value == null)
					Context.KeyValues.Remove(existingObj);
				else
				{
					existingObj.Value = value;
					Context.KeyValues.Update(existingObj);
				}
			}
			else
			{
				if (value != null)
					Context.KeyValues.Add(new SqliteStateContext.KeyValue { Key = key, Value = value });
			}

			await Context.SaveChangesAsync();
			Context.DetachAllEntities();
		}

		public async Task<string> ReadKeyValue(string key)
		{
			return (await Context.KeyValues.FirstOrDefaultAsync(x => x.Key == key))?.Value ?? null;
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			using var lockObj = await @lock.LockAsync();

			return await Context.QueuedImageDownloads.AsNoTracking().ToListAsync();
		}

        public void Dispose()
        {
            Context.Dispose();
			Connection?.Dispose();
        }
    }
}