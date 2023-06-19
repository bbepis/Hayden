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

	Task<IList<DBBoard>> GetBoardInfo();
	Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid);
	Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page);
	Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, int hitCount);

	Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions();
	Task SetIndexPosition(ushort boardId, ulong indexPosition);

	IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo);
}

public class SearchRequest
{
	public string TextQuery { get; set; }

	public string[] Boards { get; set; }

	public string Subject { get; set; }
	public string PosterName { get; set; }
	public string PosterTrip { get; set; }
	public string PosterID { get; set; }

	public bool? IsOp { get; set; }

	public string DateStart { get; set; }
	public string DateEnd { get; set; }

	public string OrderType { get; set; }

	public int? Page { get; set; }
}