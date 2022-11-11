using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Hayden.Consumers.HaydenMysql.DB
{
	public class HaydenDbContext : DbContext
	{
		public virtual DbSet<DBBoard> Boards { get; set; }
		public virtual DbSet<DBThread> Threads { get; set; }
		public virtual DbSet<DBPost> Posts { get; set; }
		public virtual DbSet<DBFileMapping> FileMappings { get; set; }
		public virtual DbSet<DBFile> Files { get; set; }

		// live imageboard only
		public virtual DbSet<DBBannedPoster> BannedPosters { get; set; }
		public virtual DbSet<DBModerator> Moderators { get; set; }

		public HaydenDbContext(DbContextOptions options) : base(options) { }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<DBThread>(x => x.HasKey(nameof(DBThread.BoardId), nameof(DBThread.ThreadId)));
			modelBuilder.Entity<DBPost>(x => x.HasKey(nameof(DBPost.BoardId), nameof(DBPost.PostId)));
			modelBuilder.Entity<DBFileMapping>(x => x.HasKey(nameof(DBFileMapping.BoardId), nameof(DBFileMapping.PostId), nameof(DBFileMapping.Index)));

			modelBuilder.Entity<DBFile>(x => HasJsonConversion(x.Property<JObject>(nameof(DBFile.AdditionalMetadata))));

			modelBuilder.HasCharSet(CharSet.Utf8Mb4.Name, DelegationModes.ApplyToColumns);
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, DBBoard boardObj, bool skipPostInfo = false)
		{
			var thread = await Threads.AsNoTracking().FirstOrDefaultAsync(x => x.BoardId == boardObj.Id && x.ThreadId == threadId);

			if (thread == null)
				return (boardObj, null, null, null);

			if (skipPostInfo)
				return (boardObj, thread, null, null);

			var posts = await Posts.AsNoTracking()
				.Where(x => x.BoardId == boardObj.Id && x.ThreadId == threadId)
				.OrderBy(x => x.DateTime)
				.ToArrayAsync();

			var fileMappings = await FileMappings.AsNoTracking()
				.Join(Posts.AsNoTracking(),
					mapping => new { mapping.BoardId, mapping.PostId },
					post => new { post.BoardId, post.PostId },
					(mapping, post) => new { mapping, post.ThreadId })
				.Join(Files.AsNoTracking(),
					mapping => mapping.mapping.FileId,
					file => file.Id,
					(mapping, file) => new { mapping.mapping, mapping.ThreadId, file })
				.Where(x => x.mapping.BoardId == boardObj.Id && x.ThreadId == threadId)
				.Select(x => new { x.mapping, x.file })
				.ToArrayAsync();

			return (boardObj, thread, posts, fileMappings.Select(x => (x.mapping, x.file)).ToArray());
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, string board, bool skipPostInfo = false)
		{
			var boardObj = await Boards.FirstOrDefaultAsync(x => x.ShortName == board);

			if (boardObj == null)
				return default;

			return await GetThreadInfo(threadId, boardObj, skipPostInfo);
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, ushort boardId, bool skipPostInfo = false)
		{
			var boardObj = await Boards.FirstOrDefaultAsync(x => x.Id == boardId);

			if (boardObj == null)
				return default;

			return await GetThreadInfo(threadId, boardObj, skipPostInfo);
		}

		public void DetachAllEntities()
		{
			var changedEntriesCopy = ChangeTracker.Entries()
				//.Where(e => e.State == EntityState.Unchanged)
				.ToList();

			foreach (var entry in changedEntriesCopy)
				entry.State = EntityState.Detached;
		}

		private static readonly ValueConverter<JObject, string> JsonValueConverter = new(
			v => v == null ? null : v.ToString(Formatting.None),
			v => v == null ? null : JObject.Parse(v)
		);

		private static readonly ValueComparer<JObject> JsonValueComparer = new(
			(l, r) => JToken.DeepEquals(l, r),
			v => v == null ? 0 : v.GetHashCode(),
			v => v == null ? null : (JObject)v.DeepClone()
		);

		protected static PropertyBuilder HasJsonConversion(PropertyBuilder<JObject> propertyBuilder)
		{
			propertyBuilder.HasConversion(JsonValueConverter);
			propertyBuilder.Metadata.SetValueConverter(JsonValueConverter);
			propertyBuilder.Metadata.SetValueComparer(JsonValueComparer);
			propertyBuilder.HasColumnType("json");

			return propertyBuilder;
		}
	}
}