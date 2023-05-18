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
	Task<ApiController.JsonBoardPageModel> PerformSearch(SearchRequest searchRequest);

	Task<IEnumerable<PostIndex>> GetIndexEntities(string board, ulong minPostNo);
}

public class SearchRequest
{
	public string TextQuery { get; set; }

	public string[] Boards { get; set; }

	public string Subject { get; set; }
	public string PosterName { get; set; }
	public string PosterID { get; set; }
	public bool? IsOp { get; set; }

	public int? Page { get; set; }
}