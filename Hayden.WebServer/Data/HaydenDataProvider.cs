using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.View;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Hayden.WebServer.Controllers.Api.ApiController;

namespace Hayden.WebServer.Data
{
	public class HaydenDataProvider : IDataProvider
	{
		private HaydenDbContext dbContext { get; }
		private IOptions<ServerConfig> config { get; }

		public HaydenDataProvider(HaydenDbContext context, IOptions<ServerConfig> config)
		{
			dbContext = context;
			this.config = config;
		}
		public bool SupportsWriting => true;

		public async Task<bool> PerformInitialization(IServiceProvider services)
		{
			await using var dbContext = services.GetRequiredService<HaydenDbContext>();

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

			//services.AddHostedService<ESSyncService>();

			return services;
		}
	}
}
