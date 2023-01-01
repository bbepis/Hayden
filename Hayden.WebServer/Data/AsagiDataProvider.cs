using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NodaTime;

namespace Hayden.WebServer.Data
{
	public class AsagiDataProvider : IDataProvider
	{
		private AsagiDbContext dbContext { get; }

		// TODO: this should not be static
		private static string[] Boards { get; set; }

		public AsagiDataProvider(AsagiDbContext context)
		{
			dbContext = context;
		}

		public bool SupportsWriting => true;

		private DBBoard CreateBoardInfo(string board)
		{
			return new DBBoard
			{
				ShortName = board,
				LongName = board,
				Category = "Asagi",
				IsReadOnly = true,
				MultiImageLimit = 1
			};
		}

		private string GetMediaFilename(string board, AsagiDbContext.AsagiDbImage image)
		{
			string radixString = Path.Combine(image.media.Substring(0, 4), image.media.Substring(4, 2));
			return string.Join('/', "/image", board, "image", radixString, image.media); 
		}

		private string GetMediaThumbnail(string board, AsagiDbContext.AsagiDbImage image)
		{
			string radixString = Path.Combine(image.media.Substring(0, 4), image.media.Substring(4, 2));
			return string.Join('/', "/image", board, "thumb", radixString, image.preview_op ?? image.preview_reply); 
		}

		private ApiController.JsonThreadModel CreateThreadModel(string board,
			AsagiDbContext.AsagiDbThread threadInfo,
			(AsagiDbContext.AsagiDbPost p, AsagiDbContext.AsagiDbImage i)[] posts)
		{
			var op = posts.First(x => x.p.op).p;

			return new ApiController.JsonThreadModel
			{
				board = CreateBoardInfo(board),
				threadId = op.thread_num,
				archived = op.locked,
				deleted = op.deleted,
				subject = op.title,
				lastModified = Instant.FromUnixTimeSeconds(threadInfo.time_bump).ToDateTimeUtc(),
				posts = posts.Select(post => new ApiController.JsonPostModel
				{
					postId = post.p.num,
					author = post.p.name,
					contentHtml = null,
					contentRaw = post.p.comment,
					deleted = post.p.deleted,
					dateTime = Instant.FromUnixTimeSeconds(post.p.timestamp).ToDateTimeUtc(),
					files = post.i?.media == null ? Array.Empty<ApiController.JsonFileModel>() : new[]
					{
						new ApiController.JsonFileModel
						{
							index = 1,
							fileId = post.i.media_id,
							extension = Path.GetExtension(post.i.media),
							filename = Path.GetFileNameWithoutExtension(post.p.media_filename),
							deleted = false, // might be wrong?
							fileSize = post.p.media_size,
							imageHeight = post.p.media_h,
							imageWidth = post.p.media_w,
							md5Hash = Convert.FromBase64String(post.p.media_hash),
							sha1Hash = null,
							sha256Hash = null,
							spoiler = post.p.spoiler,
							imageUrl = GetMediaFilename(board, post.i),
							thumbnailUrl = GetMediaThumbnail(board, post.i)
						}
					}
				}).ToArray()
			};
		}

		public async Task<bool> PerformInitialization(IServiceProvider services)
		{
			await using var tempContext = services.GetRequiredService<AsagiDbContext>();

			try
			{
				await tempContext.Database.OpenConnectionAsync();

				Boards = await tempContext.GetBoardTables();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Database cannot be connected to, or is not ready");
				return false;
			}

			return true;
		}

		public Task<IList<DBBoard>> GetBoardInfo()
		{
			var boardInfos = Boards.Select(CreateBoardInfo).ToArray();

			return Task.FromResult<IList<DBBoard>>(boardInfos);
		}

		public async Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid)
		{
			if (!Boards.Contains(board))
				return null;

			var (posts, images, threads) = dbContext.GetSets(board);

			var query = from p in posts.Where(x => x.thread_num == threadid)
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().ToArrayAsync();

			var threadInfo = await threads.AsNoTracking().FirstAsync(x => x.thread_num == threadid);

			return CreateThreadModel(board, threadInfo, result.Select(x => (x.p, x.i)).ToArray());
		}

		public async Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page)
		{
			if (!Boards.Contains(board))
				return null;

			var (posts, images, threads) = dbContext.GetSets(board);

			var totalCount = await threads.CountAsync();

			var topThreads = await threads
				.AsNoTracking()
				.OrderByDescending(x => x.time_bump)
				.Skip(page.HasValue ? ((page.Value - 1) * 10) : 0)
				.Take(10)
				.ToArrayAsync();

			var threadNumbers = topThreads.Select(x => x.thread_num).ToArray();

			var query = from p in posts.Where(x => threadNumbers.Contains(x.thread_num))
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().ToArrayAsync();

			var threadList = new ApiController.JsonThreadModel[topThreads.Length];
			var index = 0;

			foreach (var group in result.GroupBy(x => x.p.thread_num))
			{
				var previewPostList = group.Skip(1).TakeLast(3).Prepend(group.First());

				threadList[index] = CreateThreadModel(board, topThreads.First(x => x.thread_num == group.Key),
					previewPostList.Select(x => (x.p, x.i)).ToArray());

				index++;
			}

			return new ApiController.JsonBoardPageModel
			{
				boardInfo = CreateBoardInfo(board),
				threads = threadList,
				totalThreadCount = totalCount
			};
		}

		public Task<ApiController.JsonBoardPageModel> PerformSearch(string searchQuery, int? page)
		{
			throw new NotImplementedException("Not supported for asagi data provider");
		}
	}

	public static class AsagiDataProviderExtensions
	{
		public static IServiceCollection AddAsagiDataProvider(this IServiceCollection services, ServerConfig serverConfig)
		{
			services.AddScoped<IDataProvider, AsagiDataProvider>();

			if (serverConfig.Data.DBType == DatabaseType.MySql)
			{
				services.AddDbContext<AsagiDbContext>(x =>
					x.UseMySql(serverConfig.Data.DBConnectionString, ServerVersion.AutoDetect(serverConfig.Data.DBConnectionString),
						y =>
						{
							y.CommandTimeout(86400);
							y.EnableIndexOptimizedBooleanColumns();
						}));
			}
			else
			{
				throw new Exception("Unsupported database type");
			}

			return services;
		}
	}

	public class AsagiDbContext : DbContext
	{
		private string connectionString { get; set; }

		public AsagiDbContext(DbContextOptions options, IOptions<ServerConfig> serverConfig) : base(options)
		{
			connectionString = serverConfig.Value.Data.DBConnectionString;
		}

		public (DbSet<AsagiDbPost> posts, DbSet<AsagiDbImage> images, DbSet<AsagiDbThread> threads) GetSets(string board)
		{
			return (Set<AsagiDbPost>(board),
				Set<AsagiDbImage>($"{board}_images"),
				Set<AsagiDbThread>($"{board}_threads"));
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
			var boards = GetBoardTables().Result;

			foreach (var board in boards)
			{
				modelBuilder.SharedTypeEntity<AsagiDbPost>(board);
				modelBuilder.SharedTypeEntity<AsagiDbImage>($"{board}_images");
				modelBuilder.SharedTypeEntity<AsagiDbThread>($"{board}_threads");
			}
		}

		public class AsagiDbPost
		{
			[Key]
			public uint doc_id { get; set; }
			public uint media_id { get; set; }
			public uint num { get; set; }
			public uint subnum { get; set; }
			public uint thread_num { get; set; }
			public bool op { get; set; }
			public uint timestamp { get; set; }
			public uint timestamp_expired { get; set; }

			public string media_filename { get; set; }
			public ushort media_w { get; set; }
			public ushort media_h { get; set; }
			public uint media_size { get; set; }
			public string media_hash { get; set; }
			
			public bool spoiler { get; set; }
			public bool deleted { get; set; }
			public string capcode { get; set; }
			
			public string name { get; set; }
			public string trip { get; set; }
			public string title { get; set; }
			public string comment { get; set; }

			public bool sticky { get; set; }
			public bool locked { get; set; }
			public string poster_hash { get; set; }
			public string poster_country { get; set; }
		}

		public class AsagiDbImage
		{
			[Key]
			public uint media_id { get; set; }
			public string media_hash { get; set; }
			public string media { get; set; }
			public string preview_op { get; set; }
			public string preview_reply { get; set; }
		}

		public class AsagiDbThread
		{
			[Key]
			public uint thread_num { get; set; }
			public uint time_bump { get; set; }
		}
	}
}
