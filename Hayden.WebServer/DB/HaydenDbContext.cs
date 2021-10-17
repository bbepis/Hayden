using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Hayden.WebServer.DB
{
	public class HaydenDbContext : DbContext
	{
		public virtual DbSet<DBThread> Threads { get; set; }
		public virtual DbSet<DBPost> Posts { get; set; }

		public HaydenDbContext(DbContextOptions options) : base(options) { }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<DBThread>(x => x.HasKey(nameof(DBThread.Board), nameof(DBThread.ThreadId)));
			modelBuilder.Entity<DBPost>(x => x.HasKey(nameof(DBPost.Board), nameof(DBPost.PostId)));
		}

		public async Task<(DBThread, DBPost[])> GetThreadInfo(ulong threadId, string board)
		{
			var thread = await Threads.AsNoTracking().FirstOrDefaultAsync(x => x.Board == board && x.ThreadId == threadId);

			if (thread == null)
				return (null, null);

			var posts = await Posts.AsNoTracking()
				.Where(x => x.Board == board && x.ThreadId == threadId)
				.OrderBy(x => x.DateTime)
				.ToArrayAsync();

			return (thread, posts);
		}

		public void DetachAllEntities()
		{
			var changedEntriesCopy = ChangeTracker.Entries()
				.Where(e => e.State == EntityState.Unchanged)
				.ToList();

			foreach (var entry in changedEntriesCopy)
				entry.State = EntityState.Detached;
		}
	}
}