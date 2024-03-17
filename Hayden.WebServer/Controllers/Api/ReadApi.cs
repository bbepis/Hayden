using System;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Data;
using Hayden.WebServer.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Hayden.WebServer.Controllers.Api
{
	public partial class ApiController
	{
		[HttpGet("index")]
		public async Task<IActionResult> Index([FromServices] HaydenDbContext dbContext)
		{
			var topThreads = await dbContext.Threads.AsNoTracking()
				.OrderByDescending(x => x.LastModified)
				.Take(10)
				.ToArrayAsync();
			
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
									var (imageUrl, thumbUrl) = HaydenDataProvider.GenerateUrls(y.Item2, boardObj.ShortName, Config.Value);

									return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
								}).ToArray()))
					.ToArray());
			}

			return Json(threadModels);
		}

		[HttpGet("search")]
		public async Task<IActionResult> Search([FromServices] IDataProvider dataProvider,
			[FromQuery] string query,
			[FromQuery] string subject,
			[FromQuery] string boards,
			[FromQuery] string postType,
			[FromQuery] string orderType,
			[FromQuery] string posterId,
			[FromQuery] string name,
			[FromQuery] string trip,
			[FromQuery] string filename,
			[FromQuery] string md5Hash,
			[FromQuery] string dateStart,
			[FromQuery] string dateEnd,
			[FromQuery] int? page)
		{
			if (SearchService == null || !Config.Value.Search.Enabled)
				return BadRequest("Search is not enabled");
			
			var boardInfo = await dataProvider.GetBoardInfo();

			ushort[] boardIds = string.IsNullOrWhiteSpace(boards)
				? null
				: boards.Split(',')
					.Select(x =>
						boardInfo.FirstOrDefault(
							y => y.ShortName.Equals(x, StringComparison.InvariantCultureIgnoreCase))?.Id)
					.Where(x => x.HasValue)
					.Select(x => x.Value)
					.ToArray();

			const int pageSize = 40;

			var searchRequest = new SearchRequest
            {
				TextQuery = query,
				Subject = subject,
				Boards = boardIds,
				IsOp = postType == "op" ? true : postType == "reply" ? false : null,
				PosterID = posterId,
				PosterName = name,
				PosterTrip = trip,
				Filename = filename,
				FileMD5 = md5Hash,
				DateStart = dateStart,
				DateEnd = dateEnd,
				OrderType = orderType,
				Offset = page.HasValue ? (page.Value - 1) * pageSize : null,
				ResultSize = pageSize
			};

			var searchResult = await SearchService.PerformSearch(searchRequest);

			if (searchResult.PostNumbers.Length == 0)
				return Json(new JsonBoardPageModel
				{
					totalThreadCount = searchResult.SearchHitCount,
					threads = Array.Empty<JsonThreadModel>(),
					boardInfo = null
				});
			
			return Json(await dataProvider.ReadSearchResults(searchResult.PostNumbers, searchResult.SearchHitCount));
		}

		[HttpGet("{board}/post/{postid}")]
		public async Task<IActionResult> IndividualPost(string board, ulong postid, [FromServices] IDataProvider dataProvider)
		{
			var postData = await dataProvider.GetPost(board, postid);

			if (postData == null)
				return NotFound();

			return Json(postData);
		}

		[HttpGet("{board}/thread/{threadid}")]
		public async Task<IActionResult> ThreadIndex(string board, ulong threadid, [FromServices] IDataProvider dataProvider)
		{
			var threadData = await dataProvider.GetThread(board, threadid);

			if (threadData == null)
				return NotFound();

			return Json(threadData);
		}


		[HttpGet("board/all/info")]
		public async Task<IActionResult> AllBoardInfo([FromServices] IDataProvider dataProvider)
		{
			var boardInfos = await dataProvider.GetBoardInfo();

			return Json(boardInfos);
		}

		[HttpGet("board/{board}/index")]
		public async Task<IActionResult> BoardIndex([FromServices] IDataProvider dataProvider, string board, [FromQuery] int? page)
		{
			return Json(await dataProvider.GetBoardPage(board, page));
		}
		
		public class JsonBoardPageModel
		{
			public long totalThreadCount { get; set; }
			public DBBoard boardInfo { get; set; }
			public JsonThreadModel[] threads { get; set; }
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

			public JsonThreadModel() { }
		}

		public class JsonPostModel
		{
			public ulong postId { get; set; }

			public string contentHtml { get; set; }
			public string contentRaw { get; set; }

			public string author { get; set; }
			public string tripcode { get; set; }

			public DateTime dateTime { get; set; }

			public bool deleted { get; set; }

			public JsonFileModel[] files { get; set; }

			public JsonPostModel(DBPost post, JsonFileModel[] files)
			{
				postId = post.PostId;
				contentHtml = post.ContentHtml;
				contentRaw = post.ContentRaw;
				author = post.Author;
				tripcode = post.Tripcode;
				dateTime = post.DateTime;
				deleted = post.IsDeleted;

				this.files = files;
			}

			public JsonPostModel() { }
		}

		public class JsonFileModel
		{
			public uint? fileId { get; set; }

			public byte[] md5Hash { get; set; }
			public byte[] sha1Hash { get; set; }
			public byte[] sha256Hash { get; set; }

			public string extension { get; set; }

			public ushort? imageWidth { get; set; }
			public ushort? imageHeight { get; set; }

			public uint? fileSize { get; set; }

			public byte index { get; set; }

			public string filename { get; set; }

			public bool spoiler { get; set; }
			public bool deleted { get; set; }

			public string imageUrl { get; set; }
			public string thumbnailUrl { get; set; }

			public JsonFileModel(DBFile file, DBFileMapping fileMapping, string imageUrl, string thumbnailUrl)
			{
				var mappingMetadata = !string.IsNullOrWhiteSpace(fileMapping.AdditionalMetadata)
					? JObject.Parse(fileMapping.AdditionalMetadata)
					: null;

				fileId = file?.Id;

				md5Hash = file?.Md5Hash;
				if (md5Hash == null)
				{
					var md5HashB64 = mappingMetadata?.Value<string>("missing_md5hash");

					if (md5HashB64 != null)
						md5Hash = Convert.FromBase64String(md5HashB64);
				}

				sha1Hash = file?.Sha1Hash;
				sha256Hash = file?.Sha256Hash;
				extension = file?.Extension ?? mappingMetadata?.Value<string>("missing_extension")?.TrimStart('.');
				imageWidth = file?.ImageWidth;
				imageHeight = file?.ImageHeight;
				fileSize = file?.Size ?? mappingMetadata?.Value<uint?>("missing_size");

				index = fileMapping.Index;
				filename = fileMapping.Filename;
				spoiler = fileMapping.IsSpoiler;
				deleted = fileMapping.IsDeleted;

				this.imageUrl = imageUrl;
				this.thumbnailUrl = thumbnailUrl;
			}

			public JsonFileModel() { }
		}
	}
}
