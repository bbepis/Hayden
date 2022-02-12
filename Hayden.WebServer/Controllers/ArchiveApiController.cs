using System;
using System.Linq;
using System.Threading.Tasks;
using Hayden.WebServer.DB;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Routing;
using Hayden.WebServer.View;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nest;

namespace Hayden.WebServer.Controllers
{
	[ApiModeActionFilter(true)]
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
				//.Join(dbContext.Posts.AsNoTracking(), t => new { t.ThreadId, t.BoardId }, p => new { p.ThreadId, p.BoardId }, (t, p) => new { t, p })
				//.Take(10)
				.ToArrayAsync();

			// This is incredibly inefficient and slow, but it's prototype code so who cares

			//var array = topThreads.GroupBy(x => x.t.ThreadId)
			//	.Select(x => x.OrderBy(y => y.p.DateTime).Take(1)
			//		.Concat(x.Where(y => y.p.PostId != y.p.ThreadId).OrderByDescending(y => y.p.DateTime).Take(3).Reverse()).ToArray())
			//	.Select(x => new ThreadModel(x.First().t, x.Select(y => new PostPartialViewModel(y.p, Config.Value)).ToArray()))
			//	.OrderByDescending(x => x.thread.LastModified)
			//	.ToArray();

			JsonThreadModel[] threadModels = new JsonThreadModel[topThreads.Length];

			for (var i = 0; i < topThreads.Length; i++)
			{
				var thread = topThreads[i];

				var (boardObj, threadObj, posts, mappings) = await dbContext.GetThreadInfo(thread.ThreadId, thread.BoardId);

				var limitedPosts = posts.Take(1).Concat(posts.TakeLast(3)).Distinct();

				threadModels[i] = new JsonThreadModel(boardObj, threadObj, limitedPosts.Select(x =>
						new JsonPostModel(x,
							mappings.Where(y => y.Item1.PostId == x.PostId)
								.Select(y =>
								{
									var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(y.Item2, boardObj.ShortName, Config.Value);

									return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
								}).ToArray()))
					.ToArray());
			}

			return Json(threadModels);
		}

		[HttpGet("search")]
		public async Task<IActionResult> Search([FromServices] HaydenDbContext dbContext, [FromServices] ElasticClient elasticClient, [FromQuery] string query)
		{
			Response.Headers.Add("Access-Control-Allow-Origin", "*");

			//var topThreads = await (dbContext.Threads.AsNoTracking()
			//	.Join(dbContext.Posts.AsNoTracking(), t => new { PostId = t.ThreadId, t.BoardId }, p => new { p.PostId, p.BoardId }, (t, p) => new { t, p })
			//	.Where(x => x.p.ContentHtml.Contains(query))
			//	.Select(x => x.t))
			//	.OrderByDescending(x => x.LastModified)
			//	.Take(20)
			//	//.Join(dbContext.Posts.AsNoTracking(), t => new { t.ThreadId, t.BoardId }, p => new { p.ThreadId, p.BoardId }, (t, p) => new { t, p })
			//	.ToArrayAsync();

			var searchTerm = "*" + query.ToLowerInvariant().Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?") + "*";

			Func<QueryContainerDescriptor<PostIndex>, QueryContainer> searchDescriptor;

			if (!searchTerm.Contains(" "))
			{
				searchDescriptor = x => x.Bool(b => b.Must(bc => bc.Term(y => y.IsOp, true))) &&
					x.Bool(b => b.Must(bc => bc.Bool(bcd => bcd.Should(
					x.Wildcard(y => y.PostHtmlText, searchTerm),
					x.Wildcard(y => y.PostRawText, searchTerm),
					x.Wildcard(y => y.Subject, searchTerm)))));

				
			}
			else
			{
				// .Query(x => x.Match(y => y.Field(z => z.FullName).Query(searchTerm))));
				//searchDescriptor = x => x.MatchPhrase(y => y.Field(z => z).Query(searchTerm));
				//searchDescriptor = x => x.QueryString(y => y.Fields(z => z.Field(a => a.FullName)).Query(searchTerm));

				searchDescriptor = x => x.Term(y => y.IsOp, true) && (
					x.MatchPhrase(y => y.Field(z => z.PostHtmlText).Query(searchTerm))
					|| x.MatchPhrase(y => y.Field(z => z.PostRawText).Query(searchTerm))
					|| x.MatchPhrase(y => y.Field(z => z.Subject).Query(searchTerm)));
			}

			var searchResult = await elasticClient.SearchAsync<PostIndex>(x => x
				.Size(20)
				.Source(source => source.IncludeAll())
				.Sort(y => y.Descending(z => z.PostDateUtc))
				.Query(searchDescriptor));

			var threadIdArray = searchResult.IsValid
				? searchResult.Hits.Select(x => (x.Source.BoardId, x.Source.ThreadId)).ToArray()
				: null;

			if (threadIdArray == null)
				return StatusCode(StatusCodes.Status500InternalServerError);

			// This is incredibly inefficient and slow, but it's prototype code so who cares

			//var array = topThreads.GroupBy(x => x.t.ThreadId)
			//	.Select(x => x.OrderBy(y => y.p.DateTime).Take(1)
			//		.Concat(x.Where(y => y.p.PostId != y.p.ThreadId).OrderByDescending(y => y.p.DateTime).Take(3).Reverse()).ToArray())
			//	.Select(x => new ThreadModel(x.First().t, x.Select(y => new PostPartialViewModel(y.p, Config.Value)).ToArray()))
			//	.OrderByDescending(x => x.thread.LastModified)
			//	.ToArray();

			// this is arguably worse

			JsonThreadModel[] threadModels = new JsonThreadModel[threadIdArray.Length];

			for (var i = 0; i < threadIdArray.Length; i++)
			{
				var thread = threadIdArray[i];

				var (boardObj, threadObj, posts, mappings) = await dbContext.GetThreadInfo(thread.ThreadId, thread.BoardId);

				var limitedPosts = posts.Take(1).Concat(posts.TakeLast(3)).Distinct();

				threadModels[i] = new JsonThreadModel(boardObj, threadObj, limitedPosts.Select(x =>
						new JsonPostModel(x,
							mappings.Where(y => y.Item1.PostId == x.PostId)
								.Select(y =>
								{
									var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(y.Item2, boardObj.ShortName, Config.Value);

									return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
								}).ToArray()))
					.ToArray());
			}

			return Json(threadModels);
		}

		[HttpGet("{board}/thread/{threadid}")]
		public async Task<IActionResult> ThreadIndex(string board, ulong threadid, [FromServices] HaydenDbContext dbContext)
		{
			Response.Headers.Add("Access-Control-Allow-Origin", "*");

			var (boardObj, thread, posts, mappings) = await dbContext.GetThreadInfo(threadid, board);

			if (thread == null)
				return NotFound();

			return Json(new JsonThreadModel(boardObj, thread, posts.Select(x => 
				new JsonPostModel(x, 
					mappings.Where(y => y.Item1.PostId == x.PostId)
						.Select(y =>
						{
							var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(y.Item2, board, Config.Value);

							return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
						}).ToArray()))
				.ToArray()));
		}

		public class JsonThreadModel
		{
			public ulong threadId { get; set; }

			public DBBoard board { get; set; }
			
			public string subject { get; set; }
			public DateTime lastModified { get; set; }

			public bool archived { get; set; }
			public bool deleted { get; set; }
			
			public JsonPostModel[] posts { get; set; }

			public JsonThreadModel(DBBoard board, DBThread thread, JsonPostModel[] posts)
			{
				this.board = board;

				threadId = thread.ThreadId;
				subject = thread.Title;
				lastModified = thread.LastModified;
				archived = thread.IsArchived;
				deleted = thread.IsDeleted;
				
				this.posts = posts;
			}
		}

		public class JsonPostModel
		{
			public ulong postId { get; set; }

			public string contentHtml { get; set; }
			public string contentRaw { get; set; }
			
			public string author { get; set; }

			public DateTime dateTime { get; set; }

			public bool deleted { get; set; }

			public JsonFileModel[] files { get; set; }

			public JsonPostModel(DBPost post, JsonFileModel[] files)
			{
				postId = post.PostId;
				contentHtml = post.ContentHtml;
				contentRaw = post.ContentRaw;
				author = post.Author;
				dateTime = post.DateTime;
				deleted = post.IsDeleted;

				this.files = files;
			}
		}

		public class JsonFileModel
		{
			public uint fileId { get; set; }
			
			public byte[] md5Hash { get; set; }
			public byte[] sha1Hash { get; set; }
			public byte[] sha256Hash { get; set; }
			
			public string extension { get; set; }

			public ushort? imageWidth { get; set; }
			public ushort? imageHeight { get; set; }

			public uint fileSize { get; set; }

			public byte index { get; set; }
			
			public string filename { get; set; }

			public bool spoiler { get; set; }
			public bool deleted { get; set; }

			public string imageUrl { get; set; }
			public string thumbnailUrl { get; set; }

			public JsonFileModel(DBFile file, DBFileMapping fileMapping, string imageUrl, string thumbnailUrl)
			{
				fileId = file.Id;
				md5Hash = file.Md5Hash;
				sha1Hash = file.Sha1Hash;
				sha256Hash = file.Sha256Hash;
				extension = file.Extension;
				imageWidth = file.ImageWidth;
				imageHeight = file.ImageHeight;
				fileSize = file.Size;

				index = fileMapping.Index;
				filename = fileMapping.Filename;
				spoiler = fileMapping.IsSpoiler;
				deleted = fileMapping.IsDeleted;

				this.imageUrl = imageUrl;
				this.thumbnailUrl = thumbnailUrl;
			}
		}
	}
}