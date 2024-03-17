using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.DB.Elasticsearch;

namespace Hayden.WebServer.Search;

public interface ISearchService
{
	Task CreateIndex();
	Task<SearchResults> PerformSearch(SearchRequest searchRequest);
	Task IndexBatch(IEnumerable<PostIndex> posts, CancellationToken token = default);
	Task Commit();
}

public class SearchRequest
{
	public string TextQuery { get; set; }

	public ushort[] Boards { get; set; }

	public string Subject { get; set; }
	public string PosterName { get; set; }
	public string PosterTrip { get; set; }
	public string PosterID { get; set; }
	public string FileMD5 { get; set; }
	public string Filename { get; set; }

	public bool? IsOp { get; set; }

	public string DateStart { get; set; }
	public string DateEnd { get; set; }

	public string OrderType { get; set; }

	public int? Offset { get; set; }
	public int ResultSize { get; set; } = 20;
}

public class SearchResults
{
	public (ushort BoardId, ulong ThreadId, ulong PostId)[] PostNumbers { get; set; }

	public long SearchHitCount { get; set; }

	public SearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] postNumbers, long searchHitCount)
	{
		PostNumbers = postNumbers;
		SearchHitCount = searchHitCount;
	}

	public SearchResults() { }
}