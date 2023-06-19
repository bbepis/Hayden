using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Hayden.WebServer.DB.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nest;

namespace Hayden.WebServer.Services
{
	public class ESSyncService : BackgroundService
	{
		private IDataProvider DataProvider { get; }
		private ElasticClient EsClient { get; }
		private ServerElasticSearchConfig Config { get; }

		public ESSyncService(IServiceProvider services, IOptions<ServerConfig> config)
		{
			var scope = services.CreateScope();

			DataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
			EsClient = scope.ServiceProvider.GetService<ElasticClient>();
			Config = config.Value.Elasticsearch;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (EsClient == null || Config == null || !Config.Enabled)
			{
				Console.WriteLine("Disabling elasticsearch");
				return;
			}

			if (string.IsNullOrWhiteSpace(Config.IndexName))
			{
				throw new Exception("Elasticsearch.IndexName must be a valid index name");
			}

			var indexName = Config.IndexName;

			bool alreadyExists = (await EsClient.Indices.ExistsAsync(indexName)).Exists;
			
			if (!alreadyExists)
			{
				await EsClient.Indices.DeleteAsync(indexName);

				var createResult = await EsClient.Indices.CreateAsync(indexName, i =>
					i.Settings(s => s.Setting("codec", "best_compression")
						.SoftDeletes(sd =>
							sd.Retention(r => r.Operations(0)))
						.NumberOfShards(1)
						.NumberOfReplicas(0)
					));
				
#pragma warning disable CS0618 // Type or member is obsolete
				var mapResult = await EsClient.MapAsync<PostIndex>(c =>
					c.AutoMap()
						.SourceField(s => s.Enabled(false))
						.AllField(a => a.Enabled(false))
						.Dynamic(false)
						.Index(indexName));
#pragma warning restore CS0618 // Type or member is obsolete

				if (!mapResult.IsValid)
				{
					Console.WriteLine(mapResult.ServerError?.ToString());
					Console.WriteLine(mapResult.DebugInformation);
					return;
				}
			}

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
						var minPostNo = indexPositions.First(x => x.BoardId == board.Id).IndexPosition;

						int i = 0;
						const int batchSize = 20000;

						await foreach (var batch in DataProvider.GetIndexEntities(board.ShortName, minPostNo).Batch(batchSize))
						{
							await EsClient.IndexManyAsync(batch, indexName, stoppingToken);

							i += batch.Count;

							var maxPostId = batch.Max(x => x.PostId);

							await DataProvider.SetIndexPosition(board.Id, maxPostId);

							Console.WriteLine($"[{board.ShortName}]: Indexed {i} ({maxPostId})");

							if (stoppingToken.IsCancellationRequested)
								return;
						}
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
