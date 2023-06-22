using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Data;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.View;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nest;

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
									var (imageUrl, thumbUrl) = PostPartialViewModel.GenerateUrls(y.Item2, boardObj.ShortName, Config.Value);

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
			[FromQuery] string dateStart,
			[FromQuery] string dateEnd,
			[FromQuery] int? page)
		{
			var searchRequest = new Data.SearchRequest
            {
				TextQuery = query,
				Subject = subject,
				Boards = string.IsNullOrWhiteSpace(boards) ? null : boards.Split(','),
				IsOp = postType == "op" ? true : postType == "reply" ? false : null,
				PosterID = posterId,
				PosterName = name,
				PosterTrip = trip,
				DateStart = dateStart,
				DateEnd = dateEnd,
				OrderType = orderType,
				Page = page
			};

			if (ElasticClient == null || !Config.Value.Elasticsearch.Enabled)
				return null;

			var searchTerm = searchRequest.TextQuery?.ToLowerInvariant()
				.Replace("\\", "\\\\")
				.Replace("*", "\\*")
				.Replace("?", "\\?");

			var boardInfo = await dataProvider.GetBoardInfo();
			
			Func<QueryContainerDescriptor<PostIndex>, QueryContainer> searchDescriptor = x =>
			{
				var allQueries = new List<Func<QueryContainerDescriptor<PostIndex>, QueryContainer>>();

				if (!string.IsNullOrWhiteSpace(searchRequest.Subject))
					allQueries.Add(y => y.Match(z => z.Field(a => a.Subject).Query(searchRequest.Subject)));

				if (!string.IsNullOrWhiteSpace(searchRequest.PosterName))
					allQueries.Add(y => y.Match(z => z.Field(a => a.Subject).Query(searchRequest.PosterName)));

				if (!string.IsNullOrWhiteSpace(searchRequest.PosterTrip))
					allQueries.Add(y => y.Match(z => z.Field(a => a.Subject).Query(searchRequest.PosterTrip)));

				if (!string.IsNullOrWhiteSpace(searchRequest.PosterID))
					allQueries.Add(y => y.Match(z => z.Field(a => a.Subject).Query(searchRequest.PosterID)));

				var startDateBool = string.IsNullOrWhiteSpace(searchRequest.DateStart) && DateOnly.TryParse(searchRequest.DateStart, out var startDate);
				var endDateBool = string.IsNullOrWhiteSpace(searchRequest.DateEnd) && DateOnly.TryParse(searchRequest.DateEnd, out var endDate);

				if (startDateBool || endDateBool)
					allQueries.Add(y => y.DateRange(z =>
					{
						var query = z.Field(a => a.PostDateUtc);

						if (startDateBool)
							query = query.GreaterThanOrEquals(new DateMathExpression(startDate.ToDateTime(TimeOnly.MinValue)));

						if (endDateBool)
							query = query.LessThanOrEquals(new DateMathExpression(endDate.ToDateTime(TimeOnly.MaxValue)));

						return query;
					}));

				if (searchRequest.IsOp.HasValue)
					allQueries.Add(y => y.Term(z => z.Field(f => f.IsOp).Value(searchRequest.IsOp.Value)));

				if (searchRequest.Boards != null && searchRequest.Boards.Length > 0)
				{
					allQueries.Add(y => y.Bool(z => z.Should(searchRequest.Boards.Select<string, Func<QueryContainerDescriptor<PostIndex>, QueryContainer>>(board =>
					{
						return a => a.Term(b => b.Field(f => f.BoardId).Value(boardInfo.First(j => j.ShortName == board).Id));
					}))));
				}

				if (!string.IsNullOrWhiteSpace(searchTerm))
					if (!searchTerm.Contains(" "))
					{
						allQueries.Add(y => y.Match(z => z.Field(o => o.PostRawText).Query(searchTerm)));
					}
					else
					{
						allQueries.Add(y => y.MatchPhrase(z => z.Field(o => o.PostRawText).Query(searchTerm)));
					}

				return x.Bool(y => y.Must(allQueries));
			};

			var searchResult = await ElasticClient.SearchAsync<PostIndex>(x => x
				.Index(Config.Value.Elasticsearch.IndexName)
				.Size(20)
				.Skip(searchRequest.Page.HasValue ? (searchRequest.Page.Value - 1) * 20 : null)
				.DocValueFields(f => f.Fields(p => p.BoardId, p => p.ThreadId, p => p.PostId))
				.Query(searchDescriptor)
				.Sort(y => searchRequest.OrderType == "asc" ? y.Ascending(z => z.PostDateUtc)
					: y.Descending(z => z.PostDateUtc)));

			if (Config.Value.Elasticsearch.Debug)
				Console.WriteLine(searchResult.ApiCall.DebugInformation);

			if (!searchResult.IsValid)
				return null;

			var threadIdArray = searchResult.Hits.Select(x =>
					(BoardId: x.Fields.ValueOf<PostIndex, ushort>(y => y.BoardId),
					ThreadId: x.Fields.ValueOf<PostIndex, ulong>(y => y.ThreadId),
					PostId: x.Fields.ValueOf<PostIndex, ulong>(y => y.PostId)
						))
					.ToArray();

			if (threadIdArray.Length == 0)
				return Json(new ApiController.JsonBoardPageModel
				{
					totalThreadCount = searchResult.Hits.Count,
					threads = Array.Empty<ApiController.JsonThreadModel>(),
					boardInfo = null
				});
			
			return Json(await dataProvider.ReadSearchResults(threadIdArray, searchResult.Hits.Count));
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
			public int totalThreadCount { get; set; }
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
				fileId = file?.Id;
				md5Hash = file?.Md5Hash;
				sha1Hash = file?.Sha1Hash;
				sha256Hash = file?.Sha256Hash;
				extension = file?.Extension;
				imageWidth = file?.ImageWidth;
				imageHeight = file?.ImageHeight;
				fileSize = file?.Size;

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
