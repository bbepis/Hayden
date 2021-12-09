using System.Linq;
using System.Threading.Tasks;
using Hayden.WebServer.DB;
using Hayden.WebServer.View;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Controllers
{
	[Route("api")]
	public class ArchiveApiController : Controller
	{
		protected IOptions<Config> Config { get; set; }

		public ArchiveApiController(IOptions<Config> config)
		{
			Config = config;
		}

		[HttpGet("index")]
		public async Task<IActionResult> Index([FromServices] HaydenDbContext dbContext)
		{
			Response.Headers.Add("Access-Control-Allow-Origin", "*");

			var topThreads = await dbContext.Threads.AsNoTracking()
				.OrderByDescending(x => x.LastModified)
				.Take(10)
				.Join(dbContext.Posts.AsNoTracking(), t => new { t.ThreadId, t.Board }, p => new { p.ThreadId, p.Board }, (t, p) => new { t, p })
				.ToArrayAsync();

			// This is incredibly inefficient and slow, but it's prototype code so who cares

			var array = topThreads.GroupBy(x => x.t.ThreadId)
				.Select(x => x.OrderBy(y => y.p.DateTime).Take(1)
					.Concat(x.Where(y => y.p.PostId != y.p.ThreadId).OrderByDescending(y => y.p.DateTime).Take(3).Reverse()).ToArray())
				.Select(x => new ThreadModel(x.First().t, x.Select(y => new PostPartialViewModel(y.p, Config.Value)).ToArray()))
				.OrderByDescending(x => x.thread.LastModified)
				.ToArray();

			return Json(array);
		}

		[HttpGet("search")]
		public async Task<IActionResult> Search([FromServices] HaydenDbContext dbContext, [FromQuery] string query)
		{
			Response.Headers.Add("Access-Control-Allow-Origin", "*");

			var topThreads = await (dbContext.Threads.AsNoTracking()
				.Join(dbContext.Posts.AsNoTracking(), t => new { PostId = t.ThreadId, t.Board }, p => new { p.PostId, p.Board }, (t, p) => new { t, p })
				.Where(x => x.p.Html.Contains(query))
				.Select(x => x.t))
				.OrderByDescending(x => x.LastModified)
				.Take(20)
				.Join(dbContext.Posts.AsNoTracking(), t => new { t.ThreadId, t.Board }, p => new { p.ThreadId, p.Board }, (t, p) => new { t, p })
				.ToArrayAsync();

			// This is incredibly inefficient and slow, but it's prototype code so who cares

			var array = topThreads.GroupBy(x => x.t.ThreadId)
				.Select(x => x.OrderBy(y => y.p.DateTime).Take(1)
					.Concat(x.Where(y => y.p.PostId != y.p.ThreadId).OrderByDescending(y => y.p.DateTime).Take(3).Reverse()).ToArray())
				.Select(x => new ThreadModel(x.First().t, x.Select(y => new PostPartialViewModel(y.p, Config.Value)).ToArray()))
				.OrderByDescending(x => x.thread.LastModified)
				.ToArray();

			return Json(array);
		}

		[HttpGet]
		[Route("{board}/thread/{threadid}")]
		public async Task<IActionResult> ThreadIndex(string board, ulong threadid, [FromServices] HaydenDbContext dbContext)
		{
			Response.Headers.Add("Access-Control-Allow-Origin", "*");

			var (thread, posts) = await dbContext.GetThreadInfo(threadid, board);

			if (thread == null)
				return NotFound();

			return Json(new ThreadModel(thread, posts.Select(x => new PostPartialViewModel(x, Config.Value)).ToArray()));
		}

		public class ThreadModel
		{
			public DBThread thread { get; set; }
			public PostPartialViewModel[] posts { get; set; }

			public ThreadModel() {}

			public ThreadModel(DBThread thread, PostPartialViewModel[] posts)
			{
				this.thread = thread;
				this.posts = posts;
			}
		}
	}
}