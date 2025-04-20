using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Hayden.WebServer.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Hayden.WebServer.WebDb;
using Serilog;
using Hayden.WebServer.Config;

namespace Hayden.WebServer.Services;

public class SearchSyncService : BackgroundService
{
	private IDataProvider DataProvider { get; }
	private ISearchService SearchService { get; }
	private WebDbContext WebDbContext { get; }
	private IOptions<ServerSearchConfig> Config { get; }

	private static ILogger Logger { get; } = SerilogManager.CreateSubLogger("SearchSync");

	public SearchSyncService(IServiceProvider services, IOptions<ServerSearchConfig> config)
	{
		var scope = services.CreateScope();

		DataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
		SearchService = scope.ServiceProvider.GetService<ISearchService>();
		Config = config;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (SearchService == null || Config == null || !Config.Value.Enabled)
		{
			Logger.Warning("Search service has been disabled");
			return;
		}

		if (string.IsNullOrWhiteSpace(Config.Value.IndexName))
		{
			throw new Exception("Elasticsearch.IndexName must be a valid index name");
		}

		if (!await SearchService.CheckIfIndexExists())
		{
			Logger.Information("Creating search index");

			await SearchService.CreateIndex();
		}

		Logger.Information("Starting post indexing");

		while (true)
		{
			try
			{
				// We can't use ES aggregations for getting max post no. because of float conversion issues
				// https://github.com/elastic/elasticsearch/issues/43258
				// https://github.com/elastic/elasticsearch/issues/60149
					
				var boardList = await DataProvider.GetBoardInfo();
				var indexPositions = await WebDbContext.IndexPositions.ToArrayAsync();

				// Console.WriteLine("! " + string.Join(", ", indexPositions.Select(x => $"[{x.BoardId}] = {x.IndexPosition:N0}")));

				foreach (var board in boardList)
				{
					var indexPosition = await WebDbContext.IndexPositions.FirstOrDefaultAsync(x => x.BoardId == board.Id);

					if (indexPosition == null)
					{
						indexPosition = new DBIndexPosition { BoardId = board.Id, PostPosition = 0 };
						WebDbContext.IndexPositions.Add(indexPosition);
						await WebDbContext.SaveChangesAsync();
					}

					int i = 0;
					const int batchSize = 20000;

					Logger.Verbose($"[{board.ShortName}]: Retrieving index entities > {indexPosition.PostPosition}");

					await foreach (var batch in DataProvider.GetIndexEntities(board.ShortName, indexPosition.PostPosition).Batch(batchSize))
					{
						await SearchService.IndexBatch(batch, stoppingToken);

						i += batch.Count;

						var maxPostId = batch.Max(x => x.PostId);

						indexPosition.PostPosition = maxPostId;

						WebDbContext.Update(indexPosition);
						await WebDbContext.SaveChangesAsync();

						Logger.Verbose($"[{board.ShortName}]: Indexed {i} ({maxPostId})");

						if (stoppingToken.IsCancellationRequested)
							return;
					}

					await SearchService.Commit();
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failure during post indexing");
			}

			await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
		}
	}
}