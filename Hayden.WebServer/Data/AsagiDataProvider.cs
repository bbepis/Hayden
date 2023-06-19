using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.Asagi;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nest;
using Newtonsoft.Json;

namespace Hayden.WebServer.Data
{
	public class AsagiDataProvider : IDataProvider
	{
		private ElasticClient esClient { get; set; }
		private AsagiDbContext dbContext { get; }
		private AuxiliaryDbContext AuxiliaryDbContext { get; set; }
		private IOptions<ServerConfig> ServerConfig { get; }

		// TODO: this should not be static
		private static Dictionary<ushort, string> Boards { get; set; } = new();

		public AsagiDataProvider(AsagiDbContext context, ElasticClient elasticClient, IOptions<ServerConfig> serverConfig, IServiceProvider serviceProvider)
		{
			esClient = elasticClient;
			dbContext = context;
			ServerConfig = serverConfig;
			AuxiliaryDbContext = serviceProvider.GetService<AuxiliaryDbContext>();
		}

		public bool SupportsWriting => false;

		private DBBoard CreateBoardInfo(string board)
		{
			return new DBBoard
			{
				Id = Boards.FirstOrDefault(x => x.Value == board).Key,
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
			var op = posts.Select(x => x.p).FirstOrDefault(x => x.op);

			return new ApiController.JsonThreadModel
			{
				board = CreateBoardInfo(board),
				threadId = threadInfo.thread_num,
				archived = op?.locked ?? false,
				deleted = op?.deleted ?? false,
				subject = op?.title,
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
							extension = Path.GetExtension(post.i.media)?.TrimStart('.'),
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
				if (AuxiliaryDbContext != null)
					await AuxiliaryDbContext.Database.EnsureCreatedAsync();

				await tempContext.Database.OpenConnectionAsync();

				var indexes = AuxiliaryDbContext != null
					? AuxiliaryDbContext.BoardIndexes.AsEnumerable().Select(x => (x.Id, x.ShortName)).ToArray()
					: Array.Empty<(ushort, string)>();

				var boardList = await tempContext.GetBoardTables();

				foreach (var existingTable in indexes)
				{
					Boards[existingTable.Item1] = existingTable.Item2;
				}

				foreach (var newBoard in boardList.Except(Boards.Values.ToArray()).OrderBy(x => x))
				{
					ushort newIndex = 1;

					while (Boards.ContainsKey(newIndex))
						newIndex++;

					Boards[newIndex] = newBoard;

					if (AuxiliaryDbContext != null)
						await SetIndexPosition(newIndex, 0);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Database cannot be connected to, or is not ready");
				Console.WriteLine(ex);
				return false;
			}

			return true;
		}

		public Task<IList<DBBoard>> GetBoardInfo()
		{
			var boardInfos = Boards.Values.Select(CreateBoardInfo).ToArray();

			return Task.FromResult<IList<DBBoard>>(boardInfos);
		}

		public async Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid)
		{
			if (!Boards.Values.Contains(board))
				return null;

			var (posts, images, threads) = dbContext.GetSets(board);

			var query = from p in posts.Where(x => x.thread_num == (uint)threadid)
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().ToArrayAsync();

			var threadInfo = await threads.AsNoTracking().FirstAsync(x => x.thread_num == (uint)threadid);

			return CreateThreadModel(board, threadInfo, result.Select(x => (x.p, x.i)).ToArray());
		}

		public async Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page)
		{
			if (!Boards.Values.Contains(board))
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

		public async Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, int hitCount)
		{
			// while smart, this concatting different board sets doesn't work through EF

			//IQueryable<AsagiDbContext.AsagiDbPost> postQuery = null;

			//foreach (var post in threadIdArray)
			//{
			//	var (posts, _, _) = dbContext.GetSets(Boards[post.BoardId]);

			//	var newQuery = posts.Where(x => x.num == (uint)post.PostId);

			//	postQuery = postQuery == null ? newQuery : postQuery.Concat(newQuery);
			//}
			
			//var result = await postQuery!.AsNoTracking().ToArrayAsync();

			var allPosts = new List<(ushort boardId, AsagiDbContext.AsagiDbPost post, AsagiDbContext.AsagiDbImage image, AsagiDbContext.AsagiDbThread thread)>(threadIdArray.Length);

			foreach (var postGroup in threadIdArray.GroupBy(x => x.BoardId))
			{
				var (posts, images, threads) = dbContext.GetSets(Boards[postGroup.Key]);
				var postIds = postGroup.Select(y => (uint)y.PostId).ToArray();

				var retrievedPosts = await posts
					.SelectMany(x => images.Where(y => y.media_id == x.media_id).DefaultIfEmpty(), (post, image) => new { post, image })
					.Join(threads, obj => obj.post.thread_num, thread => thread.thread_num, (obj, thread) => new { obj.post, obj.image, thread })
					.Where(x => postIds.Contains(x.post.num) && x.post.subnum == 0)
					.AsNoTracking()
					.ToListAsync();

				foreach (var post in retrievedPosts)
					allPosts.Add((postGroup.Key, post.post, post.image, post.thread));
			}

			ApiController.JsonThreadModel[] threadModels = new ApiController.JsonThreadModel[threadIdArray.Length];
			int i = 0;

			foreach (var (boardId, post, image, thread) in allPosts)
			{
				var boardName = Boards[boardId];

				threadModels[i] = CreateThreadModel(boardName, thread, new[] { (post, image) });

				i++;
			}

			if (threadModels.Any(x => x == null))
				threadModels = threadModels.Where(x => x != null).ToArray();

			if (ServerConfig.Value.Elasticsearch.Debug)
				Console.WriteLine(JsonConvert.SerializeObject(threadModels));

			return new ApiController.JsonBoardPageModel
			{
				totalThreadCount = hitCount,
				threads = threadModels,
				boardInfo = null
			};
		}

		public async Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions()
		{
			if (AuxiliaryDbContext == null)
				throw new InvalidOperationException("Auxiliary database does not exist");

			return await AuxiliaryDbContext.BoardIndexes.AsAsyncEnumerable().Select(x => (x.Id, x.IndexPosition)).ToArrayAsync();
		}

		public async Task SetIndexPosition(ushort boardId, ulong indexPosition)
		{
			if (AuxiliaryDbContext == null)
				throw new InvalidOperationException("Auxiliary database does not exist");

			var existingIndex = await AuxiliaryDbContext.BoardIndexes.FirstOrDefaultAsync(x => x.Id == boardId);

			if (existingIndex == null)
			{
				existingIndex = new AuxiliaryDbContext.BoardIndex
				{
					Id = boardId,
					ShortName = Boards[boardId],
					IndexPosition = indexPosition
				};

				AuxiliaryDbContext.Add(existingIndex);
			}
			else
			{
				existingIndex.IndexPosition = indexPosition;
				AuxiliaryDbContext.Update(existingIndex);
			}

			await AuxiliaryDbContext.SaveChangesAsync();
			AuxiliaryDbContext.ChangeTracker.Clear();
		}

		public async IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo)
		{
			if (!Boards.Values.Contains(board))
				yield break;

			var (posts, images, threads) = dbContext.GetSets(board);
			
			var boardId = Boards.First(x => x.Value == board).Key;

			var query = posts.AsNoTracking()
				.Where(x => x.num > (uint)minPostNo && x.subnum == (uint)0)
				.OrderBy(x => x.num);

			await foreach (var post in query.AsAsyncEnumerable())
			{
				yield return new PostIndex
				{
					BoardId = boardId,
					PostId = post.num,
					ThreadId = post.thread_num,
					IsOp = post.op,
					PostDateUtc = Utility.ConvertNewYorkTimestamp(post.timestamp).UtcDateTime,
					PostRawText = post.comment,
					PosterID = post.poster_hash,
					PosterName = post.name,
					Subject = post.title,
					Tripcode = post.trip
				};
			}
		}
	}

	public static class AsagiDataProviderExtensions
	{
		public static IServiceCollection AddAsagiDataProvider(this IServiceCollection services, ServerConfig serverConfig)
		{
			services.AddScoped<IDataProvider, AsagiDataProvider>();
			services.AddSingleton(new AsagiDbContext.AsagiDbContextOptions { ConnectionString = serverConfig.Data.DBConnectionString });

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

			if (!string.IsNullOrWhiteSpace(serverConfig.Data.AuxiliaryDbLocation))
			{
				services.AddDbContext<AuxiliaryDbContext>(x => x
					.UseSqlite("Data Source=" + serverConfig.Data.AuxiliaryDbLocation));
			}

			services.AddHostedService<ESSyncService>();

			return services;
		}
	}
}
