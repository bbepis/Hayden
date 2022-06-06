using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.DB;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Logic;
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


		[HttpGet("board/all/info")]
		public async Task<IActionResult> AllBoardInfo([FromServices] HaydenDbContext dbContext)
		{
			var boardInfos = await dbContext.Boards.AsNoTracking().ToListAsync();

			return Json(boardInfos);
		}

		[HttpGet("board/{board}/index")]
		public async Task<IActionResult> BoardIndex([FromServices] HaydenDbContext dbContext, string board)
		{
			var boardInfo = await dbContext.Boards.AsNoTracking().Where(x => x.ShortName == board).FirstOrDefaultAsync();

			if (boardInfo == null)
				return NotFound();

			var topThreads = await dbContext.Threads.AsNoTracking()
				.Where(x => x.BoardId == boardInfo.Id)
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

		private readonly SemaphoreSlim PostSemaphore = new SemaphoreSlim(1);

		[RequestSizeLimit((int)(4.1 * 1024 * 1024))]
		[HttpPost("makepost")]
		public async Task<IActionResult> MakePost([FromServices] HaydenDbContext dbContext, [FromForm] PostForm form)
		{
			if (form == null || form.board == null || form.threadId == 0)
				return BadRequest();

			if (string.IsNullOrWhiteSpace(form.text) && form.file == null)
				return BadRequest();

			var threadInfo = await dbContext.GetThreadInfo(form.threadId, form.board, true);

			if (threadInfo.Item2 == null)
				return NotFound();
			
			uint? fileId = null;

			if (form.file != null)
			{
				var extension = Path.GetExtension(form.file.FileName).TrimStart('.').ToLower();

				if (extension != "png" && extension != "jpg" && extension != "jpeg")
					return BadRequest();

				fileId = await ProcessUploadedFile(dbContext, form.file, threadInfo.Item1, extension);
			}

			await PostSemaphore.WaitAsync();

			try
			{
				var nextPostId = await dbContext.Posts.Where(x => x.BoardId == threadInfo.Item1.Id).MaxAsync(x => x.PostId) + 1;

				var newPost = new DBPost()
				{
					Author = form.name.TrimAndNullify(),
					BoardId = threadInfo.Item1.Id,
					ContentRaw = form.text.TrimAndNullify(),
					ContentHtml = null,
					DateTime = DateTime.UtcNow,
					Email = null,
					IsDeleted = false,
					PostId = nextPostId,
					ThreadId = form.threadId,
					Tripcode = null
				};

				dbContext.Add(newPost);

				threadInfo.Item2.LastModified = newPost.DateTime;
				dbContext.Update(threadInfo.Item2);

				await dbContext.SaveChangesAsync();

				if (fileId.HasValue)
				{
					var fileMapping = new DBFileMapping
					{
						BoardId = threadInfo.Item1.Id,
						PostId = nextPostId,
						FileId = fileId.Value,
						Filename = Path.GetFileNameWithoutExtension(form.file.FileName),
						Index = 0,
						IsDeleted = false,
						IsSpoiler = true
					};

					dbContext.Add(fileMapping);

					await dbContext.SaveChangesAsync();
				}
			}
			finally
			{
				PostSemaphore.Release();
			}

			return NoContent();
		}

		private async Task<uint> ProcessUploadedFile(HaydenDbContext dbContext, IFormFile file, DBBoard boardInfo, string extension)
		{
			byte[] sha256Hash;
			byte[] md5Hash;
			byte[] sha1Hash;
			byte[] fileData;

			await using (var readStream = file.OpenReadStream())
			{
				fileData = new byte[file.Length];

				await readStream.ReadAsync(fileData);
			}

			using (var sha256 = SHA256.Create())
				sha256Hash = sha256.ComputeHash(fileData);

			var fileId = (await dbContext.Files.FirstOrDefaultAsync(x => x.BoardId == boardInfo.Id && x.Sha256Hash == sha256Hash))?.Id;

			if (fileId.HasValue)
				return fileId.Value;

			using (var md5 = MD5.Create())
				md5Hash = md5.ComputeHash(fileData);

			using (var sha1 = SHA1.Create())
				sha1Hash = sha1.ComputeHash(fileData);

			var base64Name = Utility.ConvertToBase(md5Hash);

			var destinationFilename = Path.Combine(Config.Value.FileLocation, boardInfo.ShortName, "image",
				$"{base64Name}.{extension}");

			var thumbnailFilename = Path.Combine(Config.Value.FileLocation, boardInfo.ShortName, "thumb",
				$"{base64Name}.jpg");

			if (!System.IO.File.Exists(destinationFilename))
			{
				using var dataStream = new MemoryStream(fileData);
				using var thumbStream = new MemoryStream();

				await FileImporterTools.RunStreamCommandAsync("magick", $"convert - -resize 125x125 -background grey -flatten jpg:-", dataStream, thumbStream);
				
				await System.IO.File.WriteAllBytesAsync(destinationFilename, fileData);
				await System.IO.File.WriteAllBytesAsync(thumbnailFilename, thumbStream.ToArray());
			}

			var dbFile = new DBFile
			{
				BoardId = boardInfo.Id,
				Extension = extension,
				Md5Hash = md5Hash,
				Sha1Hash = sha1Hash,
				Sha256Hash = sha256Hash,
				Size = (uint)fileData.Length
			};

			try
			{
				var result = await FileImporterTools.RunJsonCommandAsync("ffprobe", $"-v quiet -hide_banner -show_streams -print_format json \"{destinationFilename}\"");

				dbFile.ImageWidth = result["streams"][0].Value<ushort>("width");
				dbFile.ImageHeight = result["streams"][0].Value<ushort>("height");
			}
			catch (Exception ex) when (ex.Message.Contains("magick"))
			{
				dbFile.ImageWidth = null;
				dbFile.ImageHeight = null;
			}

			dbContext.Files.Add(dbFile);

			await dbContext.SaveChangesAsync();

			return dbFile.Id;
		}

		public class PostForm
		{
			public string name { get; set; }
			public string text { get; set; }
			public IFormFile file { get; set; }
			public string board { get; set; }
			public ulong threadId { get; set; }
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