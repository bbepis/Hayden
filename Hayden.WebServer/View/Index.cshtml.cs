using System.Collections.Generic;
using Hayden.Consumers.HaydenMysql.DB;

namespace Hayden.WebServer.View
{
	public class IndexModel
	{
		public IList<(DBThread, DBPost[])> PostCollections { get; set; }

		public IndexModel(IList<(DBThread, DBPost[])> postCollections)
		{
			PostCollections = postCollections;
		}
	}
}