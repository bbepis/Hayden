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
	Task<ApiController.JsonPostModel> GetPost(string board, ulong postid);
	Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid);
	Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page);
	Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, long hitCount);

	// Search indexing
	Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions();
	Task SetIndexPosition(ushort boardId, ulong indexPosition);

	IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo);

	// Moderation
	Task<bool> DeletePost(ushort boardId, ulong postId, bool banImages);

	// User handling

	Task<DBModerator> GetModerator(ushort userId);
	Task<DBModerator> GetModerator(string username);

	Task<bool> RegisterModerator(DBModerator moderator);
}