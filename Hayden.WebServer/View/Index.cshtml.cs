using System.Collections.Generic;
using Hayden.WebServer.DB;

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