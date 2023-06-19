using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Services;
using Hayden.WebServer.View;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nest;
using static Hayden.WebServer.Controllers.Api.ApiController;

namespace Hayden.WebServer.Data
{
	public class HaydenDataProvider : IDataProvider
	{
		private HaydenDbContext dbContext { get; }
		private IOptions<ServerConfig> config { get; }
		private ElasticClient esClient { get; set; }

		public HaydenDataProvider(HaydenDbContext context, IOptions<ServerConfig> config)
		{
			dbContext = context;
			this.config = config;
		}

		public bool SupportsWriting => true;

		public async Task<bool> PerformInitialization(IServiceProvider services)
		{
			await using var dbContext = services.GetRequiredService<HaydenDbContext>();
			esClient = services.GetService<ElasticClient>();

			try
			{
				await dbContext.Database.OpenConnectionAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Database cannot be connected to, or is not ready");
				return false;
			}

			await dbContext.UpgradeOrCreateAsync();

			if (dbContext.Moderators.All(x => x.Role != ModeratorRole.Admin))
			{
				var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

				ApiController.RegisterCodes.Add(code, ModeratorRole.Admin);

				Console.WriteLine("No admin account detected. Use this code to register an admin account:");
				Console.WriteLine(code);
			}

			return true;
		}

		public async Task<IList<DBBoard>> GetBoardInfo()
		{
			return await dbContext.Boards.AsNoTracking().ToListAsync();
		}

		private JsonThreadModel CreateThreadModel(DBBoard boardObj, DBThread thread, IEnumerable<DBPost> posts,
			(DBFileMapping, DBFile)[] mappings)
		{
			return new JsonThreadModel(boardObj, thread, posts.Select(x =>
					new JsonPostModel(x,
						mappings.Where(y => y.Item1.PostId == x.PostId)
							.Select(y =>
							{
								if (y.Item2 == null)
									return new JsonFileModel(null, y.Item1, null, null);

								var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(y.Item2, boardObj.ShortName, config.Value);

								return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
							}).ToArray()))
				.ToArray());
		}

		public async Task<JsonThreadModel> GetThread(string board, ulong threadid)
		{
			var (boardObj, thread, posts, mappings) = await dbContext.GetThreadInfo(threadid, board);

			if (thread == null)
				return null;

			return CreateThreadModel(boardObj, thread, posts, mappings);
		}

		public async Task<JsonBoardPageModel> GetBoardPage(string board, int? page)
		{
			var boardInfo = await dbContext.Boards.AsNoTracking().Where(x => x.ShortName == board).FirstOrDefaultAsync();

			if (boardInfo == null)
				return null;

			var query = dbContext.Threads.AsNoTracking()
				.Where(x => x.BoardId == boardInfo.Id);

			var totalCount = await query.CountAsync();

			var topThreads = await query
				.OrderByDescending(x => x.LastModified)
				.Skip(page.HasValue ? ((page.Value - 1) * 10) : 0)
				.Take(10)
				.ToArrayAsync();

			// This is incredibly inefficient and slow, but it's prototype code so who cares

			//var array = topThreads.GroupBy(x => x.t.ThreadId)
			//	.Select(x => x.OrderBy(y => y.p.DateTime).Take(1)
			//		.Concat(x.Where(y => y.p.PostId != y.p.ThreadId).OrderByDescending(y => y.p.DateTime).Take(3).Reverse()).ToArray())
			//	.Select(x => new ThreadModel(x.First().t, x.Select(y => new PostPartialViewModel(y.p, Config.Value)).ToArray()))
			//	.OrderByDescending(x => x.thread.LastModified)
			//	.ToArray();

			JsonThreadModel[] threadModels = new JsonThreadModel[topThreads.Length];

			for (var i = 0; i < topThreads.Length; i++)
			{
				var thread = topThreads[i];

				var (boardObj, threadObj, posts, mappings) = await dbContext.GetThreadInfo(thread.ThreadId, thread.BoardId);

				var limitedPosts = posts.Take(1).Concat(posts.TakeLast(3)).Distinct();

				threadModels[i] = CreateThreadModel(boardObj, threadObj, limitedPosts, mappings);
			}

			return new JsonBoardPageModel
			{
				totalThreadCount = totalCount,
				threads = threadModels,
				boardInfo = boardInfo
			};
		}

		public async Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, int hitCount)
		{
			var firstItem = threadIdArray.First();

			var unionizedPosts =
				dbContext.Posts.Where(x => x.BoardId == firstItem.BoardId && x.PostId == firstItem.PostId);

			unionizedPosts = threadIdArray.Skip(1)
				.Aggregate(unionizedPosts, (current, remainingItem) =>
					current.Concat(dbContext.Posts.Where(x => x.BoardId == remainingItem.BoardId && x.PostId == remainingItem.PostId)));
			
			var items = from p in unionizedPosts
				//join t in threadIdArray.Select(x => new { x.BoardId, x.PostId }) on new { p.BoardId, p.PostId } equals t
				join b in dbContext.Boards on p.BoardId equals b.Id
				from fm in dbContext.FileMappings.Where(x => x.BoardId == p.BoardId && x.PostId == p.PostId).DefaultIfEmpty()
				from f in dbContext.Files.Where(x => x.BoardId == fm.BoardId && x.Id == fm.FileId).DefaultIfEmpty()
				select new { p, b, fm, f };

			var result = await items.AsNoTracking().ToArrayAsync();

			JsonThreadModel[] threadModels = new JsonThreadModel[threadIdArray.Length];
			int i = 0;

			foreach (var group in result.GroupBy(x => new { x.p.BoardId, x.p.PostId }))
			{
				var item = group.First();

				threadModels[i] = new JsonThreadModel
				{
					board = item.b,
					archived = false,
					deleted = item.p.IsDeleted,
					lastModified = item.p.DateTime,
					threadId = item.p.ThreadId,
					posts = new[]
					{
						new JsonPostModel(item.p, group.Where(x => x.fm != null).Select(x =>
						{
							if (x.f == null)
								return new JsonFileModel(null, x.fm, null, null);

							var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(x.f, item.b.ShortName, config.Value);
							return new JsonFileModel(x.f, x.fm, imageUrl, thumbUrl);
						}).ToArray())
					}
				};

				i++;
			}

			return new JsonBoardPageModel
			{
				totalThreadCount = hitCount,
				threads = threadModels,
				boardInfo = null
			};
		}

		public Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions()
		{
			throw new NotImplementedException();
		}

		public Task SetIndexPosition(ushort boardId, ulong indexPosition)
		{
			throw new NotImplementedException();
		}

		public async IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo)
		{
			var boardInfo = await dbContext.Boards.AsNoTracking().Where(x => x.ShortName == board).FirstAsync();

			var query = dbContext.Posts.AsNoTracking()
				.Where(x => x.BoardId == boardInfo.Id && x.PostId > minPostNo);

			await foreach (var post in query.AsAsyncEnumerable())
			{
				yield return new PostIndex
				{
					//DocId = x.PostId * 1000 + x.BoardId,
					BoardId = post.BoardId,
					PostId = post.PostId,
					ThreadId = post.ThreadId,
					IsOp = post.PostId == post.ThreadId,
					PostDateUtc = post.DateTime,
					//PostHtmlText = x.ContentHtml,
					PostRawText = post.ContentRaw ?? post.ContentHtml,
					// TODO: index subject by using a join on threads
					//Subject = null
				};
			}
		}
	}

	public static class HaydenDataProviderExtensions
	{
		public static IServiceCollection AddHaydenDataProvider(this IServiceCollection services, ServerConfig serverConfig)
		{
			services.AddScoped<IDataProvider, HaydenDataProvider>();

			if (serverConfig.Data.DBType == DatabaseType.MySql)
			{
				services.AddDbContext<HaydenDbContext>(x =>
					x.UseMySql(serverConfig.Data.DBConnectionString, ServerVersion.AutoDetect(serverConfig.Data.DBConnectionString),
						y =>
						{
							y.CommandTimeout(86400);
							y.EnableIndexOptimizedBooleanColumns();
						}));
			}
			else if (serverConfig.Data.DBType == DatabaseType.Sqlite)
			{
				services.AddDbContext<HaydenDbContext>(x =>
					x.UseSqlite(serverConfig.Data.DBConnectionString));
			}
			else
			{
				throw new Exception("Unknown database type");
			}

			services.AddHostedService<ESSyncService>();

			return services;
		}
	}
}
