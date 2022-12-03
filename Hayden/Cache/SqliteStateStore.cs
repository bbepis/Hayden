using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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
            }

            public void DetachAllEntities()
            {
                var changedEntriesCopy = ChangeTracker.Entries()
                    .ToList();

                foreach (var entry in changedEntriesCopy)
                    entry.State = EntityState.Detached;
            }
        }

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
			var existingGuids = new List<Guid>(); 

			await foreach (var item in Context.QueuedImageDownloads.AsNoTracking().AsAsyncEnumerable())
			{
				if (!imageDownloads.Contains(item))
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

            Program.Log("Shrinking state database...", true);

			await Context.Database.ExecuteSqlRawAsync("VACUUM;");
		}

		/// <inheritdoc/>
		public async Task InsertToDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			Context.AddRange(imageDownloads);

			await Context.SaveChangesAsync();
            Context.DetachAllEntities();
        }

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return await Context.QueuedImageDownloads.AsNoTracking().ToListAsync();
		}

        public void Dispose()
        {
            Context.Dispose();
			Connection?.Dispose();
        }
    }
}