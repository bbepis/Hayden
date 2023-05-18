using System;
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

			if (alreadyExists)
				return;

			if (!alreadyExists)
			{
				await EsClient.Indices.DeleteAsync(indexName);

				var createResult = await EsClient.Indices.CreateAsync(indexName, i =>
					i.Settings(s => s.Setting("codec", "best_compression")
						.SoftDeletes(sd =>
							sd
								//.Enabled(false)
								.Retention(r => r.Operations(0)))
						//.RecoveryInitialShards(0)
						.NumberOfShards(1)
						.NumberOfReplicas(0)
					));

				//if (!mapResult.IsValid)
				//{
				//	Console.WriteLine(mapResult.ServerError.ToString());
				//	Console.WriteLine(mapResult.DebugInformation);
				//	return;
				//}

				//var updateResult = await EsClient.Indices.UpdateSettingsAsync(indexName,
				//	u => u.IndexSettings(s => s.Setting("codec", "best_compression")), stoppingToken);

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
				// We can't use ES aggregations for getting max post no. because of float conversion issues
				// https://github.com/elastic/elasticsearch/issues/43258
				// https://github.com/elastic/elasticsearch/issues/60149

				//var scanResult = await EsClient.SearchAsync<PostIndex>(s => s
				//	.Index(indexName)
				//	.Size(0)
				//	.Aggregations(a => a
				//		.Terms("per_board", t => t
				//			.Field(f => f.BoardId)
				//			.Aggregations(a2 =>
				//				a2.Max("max_postid", m => m.Field(f => f.PostId))
				//			))));

				//var agg = ((BucketAggregate)scanResult.Aggregations["per_board"]).Items
				//	.Cast<KeyedBucket<object>>().ToDictionary(
				//		x => (ushort)(long)x.Key,
				//		x => (ulong)((ValueAggregate)x.Values.First()).Value!);

				var boardList = await DataProvider.GetBoardInfo();

				foreach (var board in boardList)
				{
					//var minPostNo = agg.TryGetValue(board.Id, out var value)
					//	? value
					//	: 0;

					var minPostNo = (ulong)0;

					var query = await DataProvider.GetIndexEntities(board.ShortName, minPostNo);

					int i = 0;
					const int batchSize = 3000;

					foreach (var batch in query.Batch(batchSize))
					{
						await EsClient.IndexManyAsync(batch, indexName);

						i += batch.Count;

						Console.WriteLine(i);

						if (stoppingToken.IsCancellationRequested)
							return;
					}
				}

				break;

				await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
			}

			//await EsClient.Indices.ForceMergeAsync(indexName, s => s.MaxNumSegments(1));
		}
	}
}
