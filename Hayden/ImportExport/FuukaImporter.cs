using System;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;

namespace Hayden.ImportExport
{
	public class FuukaImporter : IImporter
	{
		private string CdnUrl { get; }

		private SourceConfig sourceConfig;
		private DbContextOptions dbContextOptions;
		private string[] boardTables;

		public FuukaImporter(SourceConfig sourceConfig)
		{
			this.sourceConfig = sourceConfig;

			dbContextOptions = new DbContextOptionsBuilder()
				.UseMySql(sourceConfig.DbConnectionString,
					ServerVersion.AutoDetect(sourceConfig.DbConnectionString), o =>
					{
						o.EnableIndexOptimizedBooleanColumns();
					})
				//.LogTo(s => Program.Log(s))
				.Options;

			using var dbContext = GetDbContext();

			boardTables = dbContext.GetBoardTables().Result;

			CdnUrl = sourceConfig.ImageboardWebsite;

			if (!CdnUrl.EndsWith('/'))
				CdnUrl += "/";
		}

		private FuukaDbContext GetDbContext()
			=> boardTables != null
				? new FuukaDbContext(dbContextOptions, boardTables)
				: new FuukaDbContext(dbContextOptions, sourceConfig.DbConnectionString);


		public async Task<string[]> GetBoardList()
		{
			await using var dbContext = GetDbContext();

			return await dbContext.GetBoardTables();
		}

		public async IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null)
		{
			await using var dbContext = GetDbContext();

			var query = dbContext.GetSet(board).AsNoTracking();

			if (minId.HasValue)
				query = query.Where(x => x.num >= minId);

			if (maxId.HasValue)
				query = query.Where(x => x.num <= maxId);

			var threadIds = query
				.Where(x => x.parent == 0 && x.subnum == 0)
				.Select(x => x.num);

			await foreach (var threadId in threadIds.AsAsyncEnumerable())
			{
				yield return new ThreadPointer(board, threadId);
			}
		}

		public async Task<Thread> RetrieveThread(ThreadPointer pointer)
		{
			await using var dbContext = GetDbContext();

			var threadPosts = await dbContext.GetSet(pointer.Board).AsNoTracking()
				.Where(x => (x.parent == (uint)pointer.ThreadId || x.num == (uint)pointer.ThreadId) && x.subnum == 0)
				.OrderBy(x => x.num)
				.ToArrayAsync();

			string radix = $"{pointer.ThreadId / 100000 % 1000:0000}/{pointer.ThreadId / 1000 % 100:00}";

			return new Thread
			{
				ThreadId = pointer.ThreadId,
				IsArchived = false,
				Title = threadPosts[0].title,
				Posts = threadPosts.Select(x => new Post
				{
					PostNumber = x.num,
					TimePosted = DateTimeOffset.FromUnixTimeSeconds(x.timestamp),
					Author = x.name,
					Tripcode = x.trip,
					Email = x.email,
					Subject = x.title,
					ContentRaw = x.comment,
					ContentRendered = null,
					ContentType = ContentType.Yotsuba,
					IsDeleted = x.deleted,
					OriginalObject = x,
					Media = x.media_hash == null
						? Array.Empty<Media>()
						: new[]
						{
							new Media
							{
								Filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(x.media)),
								FileExtension = Path.GetExtension(x.media),
								Index = 1,
								FileSize = x.media_size,
								IsSpoiler = x.spoiler,
								ThumbnailExtension = Path.GetExtension(x.preview),
								Md5Hash = Convert.FromBase64String(x.media_hash),
								FileUrl = $"{CdnUrl}data/{pointer.Board}/img/{radix}/{x.media_filename}",
								ThumbnailUrl = $"{CdnUrl}data/{pointer.Board}/thumb/{radix}/{x.preview}"
							}
						},
					AdditionalMetadata = new()
					{
						Capcode = x.capcode != null && x.capcode != "N" ? x.capcode : null
					}
				}).ToArray()
			};
		}
	}

	public class FuukaDbContext : DbContext
	{
		private string connectionString { get; }
		private string[] boardTables { get; set; }

		public FuukaDbContext(DbContextOptions options, string connectionString) : base(options)
		{
			this.connectionString = connectionString;
		}

		public FuukaDbContext(DbContextOptions options, string[] boardTables) : base(options)
		{
			this.boardTables = boardTables;
		}

		public DbSet<FuukaDbPost> GetSet(string board)
		{
			return Set<FuukaDbPost>(board);
		}

		public async Task<string[]> GetBoardTables()
		{
			await using var dbConnection = new MySqlConnection(connectionString);
			await dbConnection.OpenAsync();

			await using var dbCommand = dbConnection.CreateCommand();

			dbCommand.CommandText = "SHOW TABLES;";

			await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.Default);

			var tableNames = new List<string>();
			while (await reader.ReadAsync())
			{
				string tableName = (string)reader[0];

				if (!tableName.Contains('_'))
					tableNames.Add(tableName);
			}

			return tableNames.ToArray();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			boardTables ??= GetBoardTables().Result;

			foreach (var board in boardTables)
			{
				modelBuilder.SharedTypeEntity<FuukaDbPost>(board);
			}
		}

		public class FuukaDbPost
		{
			[Key]
			public uint doc_id { get; set; }
			public uint num { get; set; }
			public uint subnum { get; set; }
			public uint parent { get; set; }
			public uint timestamp { get; set; }

			public string preview { get; set; }
			public string media { get; set; }
			public ushort media_w { get; set; }
			public ushort media_h { get; set; }
			public uint media_size { get; set; }
			public string media_hash { get; set; }
			public string media_filename { get; set; }

			public bool spoiler { get; set; }
			public bool deleted { get; set; }
			public string capcode { get; set; }

			public string email { get; set; }
			public string name { get; set; }
			public string trip { get; set; }
			public string title { get; set; }
			public string comment { get; set; }

			public bool sticky { get; set; }
		}
	}
}
