using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Hayden.WebServer.DB
{
	public class HaydenDbContext : DbContext
	{
		public virtual DbSet<DBBoard> Boards { get; set; }
		public virtual DbSet<DBThread> Threads { get; set; }
		public virtual DbSet<DBPost> Posts { get; set; }
		public virtual DbSet<DBFileMapping> FileMappings { get; set; }
		public virtual DbSet<DBFile> Files { get; set; }

		public HaydenDbContext(DbContextOptions options) : base(options) { }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<DBThread>(x => x.HasKey(nameof(DBThread.BoardId), nameof(DBThread.ThreadId)));
			modelBuilder.Entity<DBPost>(x => x.HasKey(nameof(DBPost.BoardId), nameof(DBPost.PostId)));
			modelBuilder.Entity<DBFileMapping>(x => x.HasKey(nameof(DBFileMapping.BoardId), nameof(DBFileMapping.PostId), nameof(DBFileMapping.FileId)));

			modelBuilder.Entity<DBFile>(x =>
			{
				x.Property(e => e.Md5ConflictHistory)
					.HasColumnType("json");
			});
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
			var boardObj = await Boards.FirstAsync(x => x.ShortName == board);

			if (boardObj == null)
				return default;

			return await GetThreadInfo(threadId, boardObj, skipPostInfo);
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, ushort boardId, bool skipPostInfo = false)
		{
			var boardObj = await Boards.FirstAsync(x => x.Id == boardId);

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
	}
}