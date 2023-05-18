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
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Nest;
using Newtonsoft.Json;

namespace Hayden.WebServer.Data
{
	public class AsagiDataProvider : IDataProvider
	{
		private ElasticClient esClient { get; set; }
		private AsagiDbContext dbContext { get; }
		private IOptions<ServerConfig> ServerConfig { get; }

		// TODO: this should not be static
		private static string[] Boards { get; set; }

		public AsagiDataProvider(AsagiDbContext context, ElasticClient elasticClient, IOptions<ServerConfig> serverConfig)
		{
			esClient = elasticClient;
			dbContext = context;
			ServerConfig = serverConfig;
		}

		public bool SupportsWriting => false;

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
			string prefix = !string.IsNullOrWhiteSpace(ServerConfig.Value.Data.ImagePrefix) ? ServerConfig.Value.Data.ImagePrefix : "/image";

			return string.Join('/', prefix, board, "image", radixString, image.media); 
		}

		private string GetMediaThumbnail(string board, AsagiDbContext.AsagiDbImage image)
		{
			string radixString = Path.Combine(image.media.Substring(0, 4), image.media.Substring(4, 2));
			string prefix = !string.IsNullOrWhiteSpace(ServerConfig.Value.Data.ImagePrefix) ? ServerConfig.Value.Data.ImagePrefix : "/image";

			return string.Join('/', prefix, board, "thumb", radixString, image.preview_op ?? image.preview_reply); 
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
				lastModified = Utility.ConvertNewYorkTimestamp(threadInfo.time_bump).UtcDateTime,
				posts = posts.Select(post => new ApiController.JsonPostModel
				{
					postId = post.p.num,
					author = post.p.name,
					contentHtml = null,
					contentRaw = post.p.comment,
					deleted = post.p.deleted,
					dateTime = Utility.ConvertNewYorkTimestamp(post.p.timestamp).UtcDateTime,
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

		public async Task<ApiController.JsonBoardPageModel> PerformSearch(SearchRequest searchRequest)
		{
			if (esClient == null || !ServerConfig.Value.Elasticsearch.Enabled)
				return null;

			var searchTerm = searchRequest.TextQuery.ToLowerInvariant()
				.Replace("\\", "\\\\")
				.Replace("*", "\\*")
				.Replace("?", "\\?");


			Func<QueryContainerDescriptor<PostIndex>, QueryContainer> searchDescriptor = x =>
			{
				var allQueries = new List<Func<QueryContainerDescriptor<PostIndex>, QueryContainer>>();
				
				//if (!string.IsNullOrWhiteSpace(searchRequest.Subject))
				//	allQueries.Add(y => y.Match(z => z.Field(a => a.)));

				if (searchRequest.IsOp.HasValue)
					allQueries.Add(y => y.Term(z => z.Field(f => f.IsOp).Value(searchRequest.IsOp.Value)));

				if (searchRequest.Boards != null && searchRequest.Boards.Length > 0)
					allQueries.Add(y => y.Bool(z => z.Should(searchRequest.Boards.Select<string, Func<QueryContainerDescriptor<PostIndex>, QueryContainer>>(board =>
					{
						return a => a.Term(b => b.Field(f => f.BoardId).Value(Boards.FirstIndexOf(j => j == board)));
					}))));

				if (!searchTerm.Contains(" "))
				{
					allQueries.Add(y => y.Match(z => z.Field(o => o.PostRawText).Query(searchTerm)));
				}
				else
				{
					allQueries.Add(y => y.MatchPhrase(z => z.Field(o => o.PostRawText).Query(searchTerm)));
				}

				return x.Bool(y => y.Must(allQueries));
			};


			if (!searchTerm.Contains(" "))
			{
				//searchDescriptor = x => /* x.Bool(b => b.Must(bc => bc.Term(y => y.IsOp, true))) && */
				//	x.Bool(b => b.Must(bc => bc.Bool(bcd => bcd.Should(
				//	//x.Wildcard(y => y.PostHtmlText, searchTerm),
				//	//x.Wildcard(y => y.Subject, searchTerm),
				//	x.Wildcard(y => y.PostRawText, searchTerm)
				//	))));

				
			}
			else
			{
				// .Query(x => x.Match(y => y.Field(z => z.FullName).Query(searchTerm))));
				//searchDescriptor = x => x.MatchPhrase(y => y.Field(z => z).Query(searchTerm));
				//searchDescriptor = x => x.QueryString(y => y.Fields(z => z.Field(a => a.FullName)).Query(searchTerm));

				//searchDescriptor = x => x.Term(y => y.IsOp, true) && (
				//	x.MatchPhrase(y => y.Field(z => z.PostRawText).Query(searchTerm))
				//	//||x.MatchPhrase(y => y.Field(z => z.PostHtmlText).Query(searchTerm))
				//	//|| x.MatchPhrase(y => y.Field(z => z.Subject).Query(searchTerm))
				//	);
			}

			var searchResult = await esClient.SearchAsync<PostIndex>(x => x
				.Index(ServerConfig.Value.Elasticsearch.IndexName)
				.Size(20)
				//.Fields(f => f.Fields("threadId", "boardId", "postId")) // Fields(x => x.BoardId, x => x.ThreadId, x => x.PostId)
				.DocValueFields(f => f.Fields(p => p.BoardId, p => p.ThreadId, p => p.PostId))
				//.Fields(f => f.Field("*"))
				.Sort(y => y.Descending(z => z.PostDateUtc))
				.Query(searchDescriptor));

			if (ServerConfig.Value.Elasticsearch.Debug)
				Console.WriteLine(searchResult.ApiCall.DebugInformation);

			if (!searchResult.IsValid)
				return null;

			var threadIdArray = searchResult.Hits.Select(x =>
					(BoardId: x.Fields.ValueOf<PostIndex, ushort>(y => y.BoardId),
					ThreadId: x.Fields.ValueOf<PostIndex, ulong>(y => y.ThreadId),
					PostId: x.Fields.ValueOf<PostIndex, ulong>(y => y.PostId)
						))
					.ToArray();

			if (threadIdArray.Length == 0)
				return new ApiController.JsonBoardPageModel
				{
					totalThreadCount = searchResult.Hits.Count,
					threads = Array.Empty<ApiController.JsonThreadModel>(),
					boardInfo = null
				};
			

			IQueryable<AsagiDbContext.AsagiDbPost> postQuery = null;

			foreach (var post in threadIdArray)
			{
				var (posts, _, _) = dbContext.GetSets(Boards[post.BoardId]);

				var newQuery = posts.Where(x => x.num == (uint)post.PostId);

				postQuery = postQuery == null ? newQuery : postQuery.Concat(newQuery);
			}
			
			var result = await postQuery!.AsNoTracking().ToArrayAsync();

			ApiController.JsonThreadModel[] threadModels = new ApiController.JsonThreadModel[threadIdArray.Length];
			int i = 0;

			foreach (var post in result)
			{
				threadModels[i] = new ApiController.JsonThreadModel
				{
					board = CreateBoardInfo(Boards[threadIdArray[i].BoardId]), // this might not work
					archived = post.locked,
					deleted = post.deleted,
					lastModified = Utility.ConvertNewYorkTimestamp(post.timestamp).UtcDateTime,
					threadId = post.thread_num,
					posts = new[]
					{
						new ApiController.JsonPostModel
						{
							postId = post.num,
							author = post.name,
							contentRaw = post.comment,
							dateTime = Utility.ConvertNewYorkTimestamp(post.timestamp).UtcDateTime,
							deleted = post.deleted,
							files = Array.Empty<ApiController.JsonFileModel>()
						}
					}
				};

				i++;
			}

			if (threadModels.Any(x => x == null))
				threadModels = threadModels.Where(x => x != null).ToArray();

			if (ServerConfig.Value.Elasticsearch.Debug)
				Console.WriteLine(JsonConvert.SerializeObject(threadModels));

			return new ApiController.JsonBoardPageModel
			{
				totalThreadCount = searchResult.Hits.Count,
				threads = threadModels,
				boardInfo = null
			};
		}
		
		public async Task<IEnumerable<PostIndex>> GetIndexEntities(string board, ulong minPostNo)
		{
			if (!Boards.Contains(board))
				return null;

			var (posts, images, threads) = dbContext.GetSets(board);

			var boardId = (ushort)Boards.FirstIndexOf(x => x == board);

			return posts.AsNoTracking()
				.Where(x => x.num > minPostNo)
				.Select(x => new PostIndex
				{
					BoardId = boardId,
					PostId = x.num,
					ThreadId = x.thread_num,
					IsOp = x.op,
					PostDateUtc = Utility.ConvertNewYorkTimestamp(x.timestamp).UtcDateTime,
					PostRawText = x.comment,
				});
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

			services.AddHostedService<ESSyncService>();

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

			return tableNames.OrderBy(x => x).ToArray();
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
