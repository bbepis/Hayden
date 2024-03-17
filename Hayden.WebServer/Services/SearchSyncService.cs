using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Hayden.WebServer.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Services
{
	public class SearchSyncService : BackgroundService
	{
		private IDataProvider DataProvider { get; }
		private ISearchService SearchService { get; }
		private ServerSearchConfig Config { get; }

		public SearchSyncService(IServiceProvider services, IOptions<ServerConfig> config)
		{
			var scope = services.CreateScope();

			DataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
			SearchService = scope.ServiceProvider.GetService<ISearchService>();
			Config = config.Value.Search;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (SearchService == null || Config == null || !Config.Enabled)
			{
				Console.WriteLine("Disabling elasticsearch");
				return;
			}

			if (string.IsNullOrWhiteSpace(Config.IndexName))
			{
				throw new Exception("Elasticsearch.IndexName must be a valid index name");
			}
			
			Console.WriteLine("Creating search index");

			await SearchService.CreateIndex();
			
			Console.WriteLine("Indexing post searches");

			while (true)
			{
				try
				{
					// We can't use ES aggregations for getting max post no. because of float conversion issues
					// https://github.com/elastic/elasticsearch/issues/43258
					// https://github.com/elastic/elasticsearch/issues/60149
					
					var boardList = await DataProvider.GetBoardInfo();
					var indexPositions = await DataProvider.GetIndexPositions();

					// Console.WriteLine("! " + string.Join(", ", indexPositions.Select(x => $"[{x.BoardId}] = {x.IndexPosition:N0}")));

					foreach (var board in boardList)
					{
						ulong minPostNo;

						if (indexPositions.Any(x => x.BoardId == board.Id))
							minPostNo = indexPositions.First(x => x.BoardId == board.Id).IndexPosition;
						else
							minPostNo = 0;

						int i = 0;
						const int batchSize = 20000;

						Console.WriteLine($"[{board.ShortName}]: Retrieving index entities > {minPostNo}");

						await foreach (var batch in DataProvider.GetIndexEntities(board.ShortName, minPostNo).Batch(batchSize))
						{
							await SearchService.IndexBatch(batch, stoppingToken);

							i += batch.Count;

							var maxPostId = batch.Max(x => x.PostId);

							await DataProvider.SetIndexPosition(board.Id, maxPostId);

							Console.WriteLine($"[{board.ShortName}]: Indexed {i} ({maxPostId})");

							if (stoppingToken.IsCancellationRequested)
								return;
						}

						await SearchService.Commit();
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
				}

				await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
			}
		}
	}
}
