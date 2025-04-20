using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.Config;
using Hayden.WebServer.DB.Elasticsearch;
using Nest;

namespace Hayden.WebServer.Search;

public class ElasticSearch : ISearchService
{
	protected ConfigOption<ServerSearchConfig> Config { get; set; }
	protected ElasticClient EsClient { get; set; }

	public ElasticSearch(ConfigOption<ServerSearchConfig> config, ElasticClient esClient)
	{
		Config = config;
		EsClient = esClient;
	}

	public async Task<bool> CheckIfIndexExists()
	{
		return (await EsClient.Indices.ExistsAsync(Config.Value.IndexName)).Exists;
	}

	public async Task CreateIndex()
	{
		// await EsClient.Indices.DeleteAsync(indexName);

		var createResult = await EsClient.Indices.CreateAsync(Config.Value.IndexName, i =>
			i.Settings(s => s.Setting("codec", "best_compression")
				.SoftDeletes(sd =>
					sd.Retention(r => r.Operations(0)))
				.NumberOfShards(2)
				.NumberOfReplicas(0)
			));

#pragma warning disable CS0618 // Type or member is obsolete
		var mapResult = await EsClient.MapAsync<PostDocument>(c =>
			c.AutoMap()
				.SourceField(s => s.Enabled(false))
				.AllField(a => a.Enabled(false))
				.Dynamic(false)
				.Index(Config.Value.IndexName));
#pragma warning restore CS0618 // Type or member is obsolete

		if (!mapResult.IsValid)
		{
			Console.WriteLine(mapResult.ServerError?.ToString());
			Console.WriteLine(mapResult.DebugInformation);
			return;
		}
	}

	public async Task<SearchResults> PerformSearch(SearchRequest searchRequest)
	{
		var searchTerm = searchRequest.TextQuery?.ToLowerInvariant()
			.Replace("\\", "\\\\")
			.Replace("*", "\\*")
			.Replace("?", "\\?");

		Func<QueryContainerDescriptor<PostDocument>, QueryContainer> searchDescriptor = x =>
		{
			var allQueries = new List<Func<QueryContainerDescriptor<PostDocument>, QueryContainer>>();

			if (!string.IsNullOrWhiteSpace(searchRequest.Subject))
				allQueries.Add(y => y.Match(z => z.Field(a => a.Subject).Query(searchRequest.Subject)));

			if (!string.IsNullOrWhiteSpace(searchRequest.PosterName))
				allQueries.Add(y => y.Match(z => z.Field(a => a.PosterName).Query(searchRequest.PosterName)));

			if (!string.IsNullOrWhiteSpace(searchRequest.PosterTrip))
				allQueries.Add(y => y.Match(z => z.Field(a => a.Tripcode).Query(searchRequest.PosterTrip)));

			if (!string.IsNullOrWhiteSpace(searchRequest.PosterID))
				allQueries.Add(y => y.Match(z => z.Field(a => a.PosterID).Query(searchRequest.PosterID)));

			if (!string.IsNullOrWhiteSpace(searchRequest.Filename))
				allQueries.Add(y => y.Match(z => z.Field(a => a.MediaFilename).Query(searchRequest.Filename)));

			if (!string.IsNullOrWhiteSpace(searchRequest.FileMD5))
				allQueries.Add(y => y.Match(z => z.Field(a => a.MediaMd5HashBase64).Query(searchRequest.FileMD5)));

			DateOnly startDate = default;
			DateOnly endDate = default;
			var startDateBool = string.IsNullOrWhiteSpace(searchRequest.DateStart) &&
			                    DateOnly.TryParse(searchRequest.DateStart, out startDate);
			var endDateBool = string.IsNullOrWhiteSpace(searchRequest.DateEnd) &&
			                  DateOnly.TryParse(searchRequest.DateEnd, out endDate);

			if (startDateBool || endDateBool)
				allQueries.Add(y => y.DateRange(z =>
				{
					var query = z.Field(a => a.PostDateUtc);

					if (startDateBool)
						query = query.GreaterThanOrEquals(
							new DateMathExpression(startDate.ToDateTime(TimeOnly.MinValue)));

					if (endDateBool)
						query = query.LessThanOrEquals(new DateMathExpression(endDate.ToDateTime(TimeOnly.MaxValue)));

					return query;
				}));

			if (searchRequest.IsOp.HasValue)
				allQueries.Add(y => y.Term(z => z.Field(f => f.IsOp).Value(searchRequest.IsOp.Value)));

			if (searchRequest.Boards != null && searchRequest.Boards.Length > 0)
			{
				allQueries.Add(y => y.Bool(z =>
					z.Should(searchRequest.Boards
						.Select<ushort, Func<QueryContainerDescriptor<PostDocument>, QueryContainer>>(boardId =>
						{
							return a => a.Term(b => b.Field(f => f.BoardId).Value(boardId));
						}))));
			}

			if (!string.IsNullOrWhiteSpace(searchTerm))
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

		var searchResult = await EsClient.SearchAsync<PostDocument>(x => x
			.Index(Config.Value.IndexName)
			.Size(searchRequest.ResultSize)
			.Skip(searchRequest.Offset)
			.DocValueFields(f => f.Fields(p => p.BoardId, p => p.ThreadId, p => p.PostId))
			.Query(searchDescriptor)
			.Sort(y => searchRequest.OrderType == "asc"
				? y.Ascending(z => z.PostDateUtc)
				: y.Descending(z => z.PostDateUtc)));

		if (Config.Value.Debug)
			Console.WriteLine(searchResult.ApiCall.DebugInformation);

		if (!searchResult.IsValid)
			return null;

		var threadIdArray = searchResult.Hits.Select(x =>
				(BoardId: x.Fields.ValueOf<PostDocument, ushort>(y => y.BoardId),
					ThreadId: x.Fields.ValueOf<PostDocument, ulong>(y => y.ThreadId),
					PostId: x.Fields.ValueOf<PostDocument, ulong>(y => y.PostId)
				))
			.ToArray();

		return new SearchResults(threadIdArray, searchResult.Total);
	}

	public async Task IndexBatch(IEnumerable<PostDocument> posts, CancellationToken token = default)
	{
		await EsClient.IndexManyAsync(posts, Config.Value.IndexName, token);
	}

	public Task Commit() => Task.CompletedTask;
}