using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using System.ComponentModel.DataAnnotations;
using Hayden.Consumers;

namespace Hayden.ImportExport;

/// <summary>
/// Importer for the Jason Scott archive
/// </summary>
public class JSArchiveImporter : IImporter
{
	private SourceConfig sourceConfig;
	private ConsumerConfig consumerConfig;
	private DbContextOptions<JSArchiveDbContext> dbContextOptions;

	public JSArchiveImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig)
	{
		this.sourceConfig = sourceConfig;
		this.consumerConfig = consumerConfig;

		dbContextOptions = new DbContextOptionsBuilder<JSArchiveDbContext>()
			.UseMySql(sourceConfig.DbConnectionString,
				ServerVersion.AutoDetect(sourceConfig.DbConnectionString), o =>
				{
					o.EnableIndexOptimizedBooleanColumns();
				})
			//.LogTo(s => Program.Log(s))
			.Options;
	}

	private JSArchiveDbContext GetDbContext() => new JSArchiveDbContext(dbContextOptions);

	public async Task<string[]> GetBoardList()
	{
		await using var dbContext = GetDbContext();

		return await dbContext.Threads.Where(x => x.board != null).Select(x => x.board).Distinct().ToArrayAsync();
	}

	public async IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null)
	{
		await using var dbContext = GetDbContext();
		
		var query = dbContext.Threads.AsNoTracking()
			.Where(x => x.board == board);

		if (minId.HasValue)
			query = query.Where(x => x.number >= minId);

		if (maxId.HasValue)
			query = query.Where(x => x.number <= maxId);

		await foreach (var threadId in query.Select(x => x.number).Distinct().AsAsyncEnumerable())
		{
			yield return new ThreadPointer(board, (ulong)threadId);
		}
	}

	public async Task<Thread> RetrieveThread(ThreadPointer pointer)
	{
		await using var dbContext = GetDbContext();
		
		var threadPosts = await dbContext.Posts.AsNoTracking()
			.Join(dbContext.Threads, post => post.threadid, thread => thread.threadid, (post, thread) => new { thread, post })
			.Where(p => p.thread.number == (int)pointer.ThreadId && p.thread.board == pointer.Board)
			.OrderBy(p => p.post.number)
			.Select(p => p.post).ToArrayAsync();

		//var threadPosts = await posts
		//	.Where(x => (x.parent == (uint)pointer.ThreadId || x.num == (uint)pointer.ThreadId) && x.subnum == 0)
		//	.OrderBy(x => x.num)
		//	.AsNoTracking()
		//	.ToArrayAsync();

		//string radix = $"{pointer.ThreadId / 100000 % 1000:0000}/{pointer.ThreadId / 1000 % 100:00}";

		return new Thread
		{
			ThreadId = pointer.ThreadId,
			IsArchived = false,
			Title = threadPosts[0].subject,
			Posts = threadPosts.Select(x => new Post
			{
				PostNumber = (ulong)x.number,
				TimePosted = Utility.ConvertNewYorkTimestamp(x.postdate),
				Author = x.name,
				Tripcode = x.tripcode,
				Email = x.email,
				ContentRaw = AsagiThreadConsumer.CleanComment(x.body),
				ContentRendered = null,
				ContentType = ContentType.Yotsuba,
				OriginalObject = x,
				Media = !x.image
					? Array.Empty<Media>()
					: new[]
					{
						new Media
						{
							Filename = "<missing>",
							FileExtension = "",
							Index = 0,
						}
					}
			}).ToArray()
		};
	}

	private class JSArchiveDbContext : DbContext
	{
		public virtual DbSet<DbPost> Posts { get; set; }
		public virtual DbSet<DbThread> Threads { get; set; }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<DbPost>(x => x.ToTable("posts"));
			modelBuilder.Entity<DbThread>(x => x.ToTable("threads"));
		}

		public JSArchiveDbContext(DbContextOptions<JSArchiveDbContext> options) : base(options) { }
		
		public class DbPost
		{
			[Key]
			public int commentid { get; set; }
			public int threadid { get; set; }
			public int number { get; set; }
			public string name { get; set; }
			public string tripcode { get; set; }
			public string subject { get; set; }
			public string email { get; set; }
			public DateTime postdate { get; set; }

			public string body { get; set; }
			public bool image { get; set; }
		}

		public class DbThread
		{
			[Key]
			public int threadid { get; set; }
			public string threadurl { get; set; }
			public int number { get; set; }

			// this field isn't actually in the dump's data. i made it because doing a REGEXP_REPLACE in a select several times is going to be extremely fucking slow
			// so I created this column with the following query to cache it in advance:
			// UPDATE threads SET board = REGEXP_REPLACE(threadurl, '.+\/(.+)\/res\/.+', '$1') WHERE threadurl LIKE '%4chan%'
			public string board { get; set; }
		}
	}
}