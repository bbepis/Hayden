using Hayden.Consumers.HaydenMysql.DB;

namespace Hayden.WebServer.View
{
	public class ThreadPartialViewModel
	{
		public DBThread Thread { get; set; }
		public DBPost[] Posts { get; set; }

		public bool BoardPage { get; set; }

		public ThreadPartialViewModel(DBThread thread, DBPost[] posts, bool boardPage = false)
		{
			Thread = thread;
			Posts = posts;
			BoardPage = boardPage;
		}
	}
}