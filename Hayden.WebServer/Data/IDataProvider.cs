using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;

namespace Hayden.WebServer.Data;

public interface IDataProvider
{
	bool SupportsWriting { get; }

	Task<bool> PerformInitialization(IServiceProvider services);

	// Post info
	Task<IList<DBBoard>> GetBoardInfo();
	Task<IDictionary<ushort, BoardStats>> GetBoardStats();
	Task<ApiController.JsonPostModel> GetPost(string board, ulong postid);
	Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid);
	Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page);
	Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, long hitCount);

	// Search indexing
	IAsyncEnumerable<PostDocument> GetIndexEntities(string board, ulong minPostNo);
}

public class BoardStats
{
	public long ThreadCount { get; set; }
	public long PostCount { get; set; }
	public long ImageCount { get; set; }
}