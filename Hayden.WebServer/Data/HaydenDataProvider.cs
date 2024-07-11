using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static Hayden.WebServer.Controllers.Api.ApiController;

namespace Hayden.WebServer.Data
{
	public class HaydenDataProvider : IDataProvider
	{
		private HaydenDbContext dbContext { get; }
		private IOptions<ServerConfig> config { get; }

		public HaydenDataProvider(HaydenDbContext context, IOptions<ServerConfig> config)
		{
			dbContext = context;
			this.config = config;
		}

		public bool SupportsWriting => true;

		public async Task<bool> PerformInitialization(IServiceProvider services)
		{
			await using var dbContext = services.GetRequiredService<HaydenDbContext>();

			try
			{
				await dbContext.Database.OpenConnectionAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Database cannot be connected to, or is not ready");
				return false;
			}

			await dbContext.UpgradeOrCreateAsync();

			if (dbContext.Moderators.All(x => x.Role != ModeratorRole.Admin))
			{
				var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

				ApiController.RegisterCodes.Add(code, ModeratorRole.Admin);

				Console.WriteLine("No admin account detected. Use this code to register an admin account:");
				Console.WriteLine(code);
			}

			return true;
		}

		public async Task<IList<DBBoard>> GetBoardInfo()
		{
			return await dbContext.Boards.AsNoTracking().ToListAsync();
		}

		private JsonThreadModel CreateThreadModel(DBBoard boardObj, DBThread thread, IEnumerable<DBPost> posts,
			(DBFileMapping, DBFile)[] mappings)
		{
			return new JsonThreadModel(boardObj, thread, posts.Select(x =>
					new JsonPostModel(x,
						mappings.Where(y => y.Item1.PostId == x.PostId)
							.Select(y =>
							{
								if (y.Item2 == null)
									return new JsonFileModel(null, y.Item1, null, null);

								var (imageUrl, thumbUrl) = GenerateUrls(y.Item2, boardObj.ShortName, config.Value);

								return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
							}).ToArray()))
				.ToArray());
		}

		private JsonPostModel CreatePostModel(DBBoard boardObj, DBPost post,
			(DBFileMapping, DBFile)[] mappings)
		{
			return new JsonPostModel(post,
						mappings
							.Select(y =>
							{
								if (y.Item2 == null)
									return new JsonFileModel(null, y.Item1, null, null);

								var (imageUrl, thumbUrl) = GenerateUrls(y.Item2, boardObj.ShortName, config.Value);

								return new JsonFileModel(y.Item2, y.Item1, imageUrl, thumbUrl);
							}).ToArray());
		}

		public async Task<JsonThreadModel> GetThread(string board, ulong threadid)
		{
			var (boardObj, thread, posts, mappings) = await dbContext.GetThreadInfo(threadid, board);

			if (thread == null)
				return null;

			return CreateThreadModel(boardObj, thread, posts, mappings);
		}

		public async Task<JsonPostModel> GetPost(string board, ulong postid)
		{
			var boardObj = await dbContext.Boards.FirstOrDefaultAsync(x => x.ShortName == board);

			if (boardObj == null)
				return null;

			var postInfo = await dbContext.GetPostInfo(postid, boardObj.Id);

			if (postInfo.Item1 == null)
				return null;

			return CreatePostModel(boardObj, postInfo.Item1, postInfo.Item2);
		}

		private static readonly Dictionary<ushort, long> ThreadCountCache = new();

		public async Task<JsonBoardPageModel> GetBoardPage(string board, int? page)
		{
			var boardInfo = await dbContext.Boards.AsNoTracking().Where(x => x.ShortName == board).FirstOrDefaultAsync();

			if (boardInfo == null)
				return null;

			var query = dbContext.Threads.AsNoTracking()
				.Where(x => x.BoardId == boardInfo.Id);

			if (!ThreadCountCache.TryGetValue(boardInfo.Id, out var totalCount))
			{
				totalCount = await query.LongCountAsync();
				ThreadCountCache[boardInfo.Id] = totalCount;
			}
			
			DBThread[] topThreads;

			

			topThreads = await query
				.OrderByDescending(x => x.LastModified)
				.Skip(page.HasValue ? ((page.Value - 1) * 10) : 0)
				.Take(10)
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

				threadModels[i] = CreateThreadModel(boardObj, threadObj, limitedPosts, mappings);
			}

			return new JsonBoardPageModel
			{
				totalThreadCount = totalCount,
				threads = threadModels,
				boardInfo = boardInfo
			};
		}

		public async Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, long hitCount)
		{
			var firstItem = threadIdArray.First();

			var unionizedPosts =
				dbContext.Posts.Where(x => x.BoardId == firstItem.BoardId && x.PostId == firstItem.PostId);

			unionizedPosts = threadIdArray.Skip(1)
				.Aggregate(unionizedPosts, (current, remainingItem) =>
					current.Concat(dbContext.Posts.Where(x => x.BoardId == remainingItem.BoardId && x.PostId == remainingItem.PostId)));
			
			var items = from p in unionizedPosts
				//join t in threadIdArray.Select(x => new { x.BoardId, x.PostId }) on new { p.BoardId, p.PostId } equals t
				join b in dbContext.Boards on p.BoardId equals b.Id
				from fm in dbContext.FileMappings.Where(x => x.BoardId == p.BoardId && x.PostId == p.PostId).DefaultIfEmpty()
				from f in dbContext.Files.Where(x => x.BoardId == fm.BoardId && x.Id == fm.FileId).DefaultIfEmpty()
				select new { p, b, fm, f };

			var result = await items.AsNoTracking().ToArrayAsync();

			JsonThreadModel[] threadModels = new JsonThreadModel[threadIdArray.Length];
			int i = 0;

			foreach (var group in result.GroupBy(x => new { x.p.BoardId, x.p.PostId }))
			{
				var item = group.First();

				threadModels[i] = new JsonThreadModel
				{
					board = item.b,
					archived = false,
					deleted = item.p.IsDeleted,
					lastModified = item.p.DateTime,
					threadId = item.p.ThreadId,
					posts = new[]
					{
						new JsonPostModel(item.p, group.Where(x => x.fm != null).Select(x =>
						{
							if (x.f == null)
								return new JsonFileModel(null, x.fm, null, null);

							var (imageUrl, thumbUrl) = GenerateUrls(x.f, item.b.ShortName, config.Value);
							return new JsonFileModel(x.f, x.fm, imageUrl, thumbUrl);
						}).ToArray())
					}
				};

				i++;
			}

			return new JsonBoardPageModel
			{
				totalThreadCount = hitCount,
				threads = threadModels,
				boardInfo = null
			};
		}

		public async Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions()
		{
			if (!System.IO.File.Exists("index-positions.json"))
				return Array.Empty<(ushort, ulong)>();

			return JsonConvert.DeserializeObject<Dictionary<ushort, ulong>>(
					await System.IO.File.ReadAllTextAsync("index-positions.json"))
				.Select(x => (x.Key, x.Value))
				.ToArray();
		}

		public async Task SetIndexPosition(ushort boardId, ulong indexPosition)
		{
			Dictionary<ushort, ulong> dictionary;

			if (!System.IO.File.Exists("index-positions.json"))
				dictionary = new Dictionary<ushort, ulong>();
			else
				dictionary =
					JsonConvert.DeserializeObject<Dictionary<ushort, ulong>>(
						await System.IO.File.ReadAllTextAsync("index-positions.json"));

			dictionary[boardId] = indexPosition;

			await System.IO.File.WriteAllTextAsync("index-positions.json", JsonConvert.SerializeObject(dictionary));
		}

		public async IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo)
		{
			var boardInfo = await dbContext.Boards.AsNoTracking().Where(x => x.ShortName == board).FirstAsync();

			// .OrderBy(x => x.BoardId).ThenBy(x => x.PostId).Where(x => 1 == 1)
			var query = dbContext.Posts.AsNoTracking()
				.Join(dbContext.Threads, post => new { post.BoardId, post.ThreadId }, thread => new { thread.BoardId, thread.ThreadId }, (post, thread) => new { post, thread })
				//.SelectMany(x => dbContext.FileMappings.Where(y => y.PostId == x.post.PostId && y.BoardId == x.post.BoardId).Take(1).DefaultIfEmpty(), (post, mapping) => new { post.post, post.thread, mapping })
				.SelectMany(x => dbContext.FileMappings.Where(y => y.PostId == x.post.PostId && y.BoardId == x.post.BoardId && y.Index == 0).DefaultIfEmpty(), (post, mapping) => new { post.post, post.thread, mapping })
				.SelectMany(x => dbContext.Files.Where(y => y.Id == x.mapping.FileId).DefaultIfEmpty(), (x, file) => new { x.post, x.thread, x.mapping, file })
				.Where(x => x.post.BoardId == boardInfo.Id && x.post.PostId > minPostNo);

			await foreach (var x in query.AsAsyncEnumerable())
			{
				//var additionalMetadata = new Post.PostAdditionalMetadata x.post.AdditionalMetadata;

				var additionalMetadata = !string.IsNullOrWhiteSpace(x.post.AdditionalMetadata)
					? JsonConvert.DeserializeObject<Post.PostAdditionalMetadata>(x.post.AdditionalMetadata)
					: null;

				var isOp = x.post.PostId == x.post.ThreadId;

				yield return new PostIndex
				{
					BoardId = x.post.BoardId,
					PostId = x.post.PostId,
					ThreadId = x.post.ThreadId,
					IsOp = isOp,
					PostDateUtc = x.post.DateTime,
					PostRawText = x.post.ContentRaw ?? x.post.ContentHtml,
					PosterID = additionalMetadata?.PosterID,
					IsDeleted = x.post.IsDeleted,
					Subject = isOp ? x.thread.Title : null,
					PosterName = x.post.Author,
					Tripcode = x.post.Tripcode,
					MediaFilename = x.mapping != null ? (x.mapping.Filename + "." + x.file?.Extension) : null, // TODO: this needs to check AdditionalMetadata for extensions
					MediaMd5HashBase64 = x.file != null ? Convert.ToBase64String(x.file.Md5Hash) : null // TODO: this needs to check AdditionalMetadata for hashes

					//PosterID = x.post
				};
			}
		}

		public async Task<bool> DeletePost(ushort boardId, ulong postId, bool banImages)
		{
			var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.BoardId == boardId && x.PostId == postId);

			if (post == null)
				return false;

			var board = await dbContext.Boards.FirstAsync(x => x.Id == boardId);

			var mappings = await dbContext.FileMappings
				.Where(x => x.BoardId == boardId && x.PostId == postId)
				.ToArrayAsync();

			foreach (var mapping in mappings)
				dbContext.Remove(mapping);

			if (banImages && mappings.Length > 0)
			{
				var fileIds = mappings.Select(x => x.FileId).ToArray();

				var files = await dbContext.Files
					.Where(x => fileIds.Contains(x.Id))
					.ToArrayAsync();

				foreach (var file in files)
				{
					file.FileBanned = true;

					var fullFilename = Common.CalculateFilename(config.Value.Data.FileLocation, board.ShortName, Common.MediaType.Image,
						file.Sha256Hash, file.Extension);
					var thumbFilename = Common.CalculateFilename(config.Value.Data.FileLocation, board.ShortName, Common.MediaType.Thumbnail,
						file.Sha256Hash, "jpg");

					System.IO.File.Delete(fullFilename);
					System.IO.File.Delete(thumbFilename);
				}
			}

			// actually delete the post from the db?
			// flag on board object "PreserveDeleted"
			post.IsDeleted = true;

			await dbContext.SaveChangesAsync();

			return true;
		}

		public async Task<DBModerator> GetModerator(ushort userId) => await dbContext.Moderators.FirstOrDefaultAsync(x => x.Id == userId);

		public async Task<DBModerator> GetModerator(string username) => await dbContext.Moderators.FirstOrDefaultAsync(x => x.Username == username);

		public async Task<bool> RegisterModerator(DBModerator moderator)
		{
			if (await dbContext.Moderators.AnyAsync(x => x.Username == moderator.Username))
				return false;

			dbContext.Add(moderator);
			await dbContext.SaveChangesAsync();
			return true;
		}

		public static (string imageUrl, string thumbnailUrl) GenerateUrls(DBFile file, string board, ServerConfig config)
		{
			string b36Name = Utility.ConvertToBase(file.Sha256Hash);

			// https://github.com/dotnet/runtime/issues/36510
			var prefix = !string.IsNullOrWhiteSpace(config.Data.ImagePrefix)
				? config.Data.ImagePrefix
				: "/image";

			var imageUrl = $"{prefix}/{board}/image/{b36Name}.{file.Extension}";
			var thumbUrl = $"{prefix}/{board}/thumb/{b36Name}.jpg";

			return (imageUrl, thumbUrl);
		}
	}
}
