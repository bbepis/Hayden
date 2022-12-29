using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;

namespace Hayden.WebServer.Data
{
	public interface IDataProvider
	{
		bool SupportsWriting { get; }

		Task<bool> PerformInitialization(IServiceProvider services);

		Task<IList<DBBoard>> GetBoardInfo();
		Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid);
		Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page);
	}
}
