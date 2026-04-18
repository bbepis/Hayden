using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Hayden.Consumers.Asagi;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Config;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Hayden.WebServer.Data
{
	public class AsagiDataProvider : IDataProvider
	{
		private AsagiDbContext dbContext { get; }
		private ConfigOption<ServerDataConfig> DataConfig { get; }

		// TODO: this should not be static
		private static Dictionary<ushort, string> Boards { get; set; } = new();

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Asagi");

		public AsagiDataProvider(AsagiDbContext context, ConfigOption<ServerDataConfig> dataConfig, IServiceProvider serviceProvider)
		{
			dbContext = context;
			DataConfig = dataConfig;
		}

		public bool SupportsWriting => false;

		private DBBoard CreateBoardInfo(string board)
		{
			return new DBBoard
			{
				Id = Boards.FirstOrDefault(x => x.Value == board).Key,
				ShortName = board,
				LongName = board,
				Category = "Asagi",
				IsReadOnly = true,
				MultiImageLimit = 1
			};
		}

		private string GetMediaInternalPath(string board, string asagiFilename, bool thumbnail, char separator)
		{
			string radixString = Path.Combine(asagiFilename.Substring(0, 4), asagiFilename.Substring(4, 2));

			return string.Join(separator, board, thumbnail ? "thumb" : "image", radixString, asagiFilename);
		}

		private string GetMediaUrl(string board, string asagiFilename, bool thumbnail)
		{
			string prefix = !string.IsNullOrWhiteSpace(DataConfig.Value.ImagePrefix) ? DataConfig.Value.ImagePrefix : "/image";

			return string.Join('/', prefix, GetMediaInternalPath(board, asagiFilename, thumbnail, '/')); 
		}

		private string GetMediaFilename(string board, string asagiFilename, bool thumbnail)
		{
			return Path.Join(DataConfig.Value.FileLocation, GetMediaInternalPath(board, asagiFilename, thumbnail, Path.DirectorySeparatorChar)); 
		}

		private ApiController.JsonPostModel CreatePostModel(string board,
			AsagiDbContext.AsagiDbPost post, AsagiDbContext.AsagiDbImage image)
		{
			return new ApiController.JsonPostModel()
			{
				postId = post.num,
				author = post.name,
				contentHtml = null,
				contentRaw = post.comment,
				deleted = (post.deleted || post.timestamp_expired.HasValue) ? Utility.ConvertNewYorkTimestamp(post.timestamp_expired.Value).UtcDateTime : null,
				dateTime = Utility.ConvertNewYorkTimestamp(post.timestamp.Value).UtcDateTime,
				files = image?.media == null
					? Array.Empty<ApiController.JsonFileModel>()
					: new[]
					{
						new ApiController.JsonFileModel
						{
							index = 1,
							fileId = image.media_id,
							extension = Path.GetExtension(image.media)?.TrimStart('.'),
							filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(post.media_filename)),
							deleted = false, // might be wrong?
							fileSize = post.media_size,
							imageHeight = post.media_h,
							imageWidth = post.media_w,
							md5Hash = Convert.FromBase64String(post.media_hash),
							sha1Hash = null,
							sha256Hash = null,
						spoiler = post.spoiler > 0,
							imageUrl = GetMediaUrl(board, image.media, false),
							thumbnailUrl = GetMediaUrl(board, image.preview_op ?? image.preview_reply, true)
						}
					}
			};
		}

		private ApiController.JsonThreadModel CreateThreadModel(string board,
			AsagiDbContext.AsagiDbThread threadInfo,
			(AsagiDbContext.AsagiDbPost p, AsagiDbContext.AsagiDbImage i)[] posts)
		{
			var op = posts.Select(x => x.p).FirstOrDefault(x => x.op);

			var isArchived = op?.locked ?? false;

			var expiredTime = op != null && op.timestamp_expired.GetValueOrDefault() != 0
				? Utility.ConvertNewYorkTimestamp(op.timestamp_expired.Value).UtcDateTime
				: (DateTime?)null;

			return new ApiController.JsonThreadModel
			{
				board = CreateBoardInfo(board),
				threadId = threadInfo.thread_num,
				archived = isArchived ? expiredTime : null,
				deleted = !isArchived ? expiredTime : null,
				subject = op?.title,
				lastModified = Utility.ConvertNewYorkTimestamp(threadInfo.time_bump).UtcDateTime,
				posts = posts.Select(post => CreatePostModel(board, post.p, post.i)).ToArray()
			};
		}

		public async Task<bool> PerformInitialization(IServiceProvider services)
		{
			await using var tempContext = services.GetRequiredService<AsagiDbContext>();

			try
			{
				await tempContext.Database.OpenConnectionAsync();

				var boardList = tempContext.GetBoardTables();

				foreach (var newBoard in boardList.OrderBy(x => x))
				{
					ushort newIndex = 1;

					Boards[newIndex++] = newBoard;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Database cannot be connected to, or is not ready");
				Console.WriteLine(ex);
				return false;
			}

			return true;
		}

		public Task<IList<DBBoard>> GetBoardInfo()
		{
			var boardInfos = Boards.Values.Select(CreateBoardInfo).ToArray();

			return Task.FromResult<IList<DBBoard>>(boardInfos);
		}

		public Task<IDictionary<ushort, BoardStats>> GetBoardStats()
		{
			var boardInfos = Boards.Values.Select(CreateBoardInfo).ToArray();

			return Task.FromResult<IDictionary<ushort, BoardStats>>(boardInfos.ToDictionary(x => x.Id, x => (BoardStats)null));
		}

		public async Task<ApiController.JsonPostModel> GetPost(string board, ulong postid)
		{
			if (!Boards.Values.Contains(board))
				return null;

			var (posts, images, threads, _) = dbContext.GetSets(board);

			var query = from p in posts.Where(x => x.num == (uint)postid && x.subnum == 0)
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().FirstOrDefaultAsync();

			if (result == null)
				return null;

			return CreatePostModel(board, result.p, result.i);
		}

		public async Task<ApiController.JsonThreadModel> GetThread(string board, ulong threadid)
		{
			if (!Boards.Values.Contains(board))
				return null;

			var (posts, images, threads, _) = dbContext.GetSets(board);

			var query = from p in posts.Where(x => x.thread_num == (uint)threadid)
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().ToArrayAsync();

			var threadInfo = await threads.AsNoTracking().FirstAsync(x => x.thread_num == (uint)threadid);

			return CreateThreadModel(board, threadInfo, result.Select(x => (x.p, x.i)).ToArray());
		}

		public async Task<ApiController.JsonBoardPageModel> GetBoardPage(string board, int? page)
		{
			if (!Boards.Values.Contains(board))
				return null;

			var (posts, images, threads, _) = dbContext.GetSets(board);

			var totalCount = await threads.CountAsync();

			var topThreads = await threads
				.AsNoTracking()
				.OrderByDescending(x => x.time_bump)
				.Skip(page.HasValue ? ((page.Value - 1) * 10) : 0)
				.Take(10)
				.ToArrayAsync();

			var threadNumbers = topThreads.Select(x => x.thread_num).ToArray();

			var query = from p in posts.Where(x => threadNumbers.Contains(x.thread_num))
				from i in images.Where(image => image.media_id == p.media_id).DefaultIfEmpty()
				select new { p, i };

			var result = await query.AsNoTracking().ToArrayAsync();

			var threadList = new ApiController.JsonThreadModel[topThreads.Length];
			var index = 0;

			foreach (var group in result.GroupBy(x => x.p.thread_num))
			{
				var previewPostList = group.Skip(1).TakeLast(3).Prepend(group.First());

				threadList[index] = CreateThreadModel(board, topThreads.First(x => x.thread_num == group.Key),
					previewPostList.Select(x => (x.p, x.i)).ToArray());

				index++;
			}

			return new ApiController.JsonBoardPageModel
			{
				boardInfo = CreateBoardInfo(board),
				threads = threadList,
				totalThreadCount = totalCount
			};
		}

		public async Task<ApiController.JsonBoardPageModel> ReadSearchResults((ushort BoardId, ulong ThreadId, ulong PostId)[] threadIdArray, long hitCount)
		{
			// while smart, this concatting different board sets doesn't work through EF

			//IQueryable<AsagiDbContext.AsagiDbPost> postQuery = null;

			//foreach (var post in threadIdArray)
			//{
			//	var (posts, _, _) = dbContext.GetSets(Boards[post.BoardId]);

			//	var newQuery = posts.Where(x => x.num == (uint)post.PostId);

			//	postQuery = postQuery == null ? newQuery : postQuery.Concat(newQuery);
			//}
			
			//var result = await postQuery!.AsNoTracking().ToArrayAsync();

			var allPosts = new List<(ushort boardId, AsagiDbContext.AsagiDbPost post, AsagiDbContext.AsagiDbImage image, AsagiDbContext.AsagiDbThread thread)>(threadIdArray.Length);

			foreach (var postGroup in threadIdArray.GroupBy(x => x.BoardId))
			{
				var (posts, images, threads, _) = dbContext.GetSets(Boards[postGroup.Key]);
				var postIds = postGroup.Select(y => (uint)y.PostId).ToArray();

				var retrievedPosts = await posts
					.SelectMany(x => images.Where(y => y.media_id == x.media_id).DefaultIfEmpty(), (post, image) => new { post, image })
					.Join(threads, obj => obj.post.thread_num, thread => thread.thread_num, (obj, thread) => new { obj.post, obj.image, thread })
					.Where(x => postIds.Contains(x.post.num) && x.post.subnum == 0)
					.AsNoTracking()
					.ToListAsync();

				foreach (var post in retrievedPosts)
					allPosts.Add((postGroup.Key, post.post, post.image, post.thread));
			}

			ApiController.JsonThreadModel[] threadModels = new ApiController.JsonThreadModel[threadIdArray.Length];
			int i = 0;

			foreach (var (boardId, post, image, thread) in allPosts)
			{
				var boardName = Boards[boardId];

				threadModels[i] = CreateThreadModel(boardName, thread, new[] { (post, image) });

				i++;
			}

			if (threadModels.Any(x => x == null))
				threadModels = threadModels.Where(x => x != null).ToArray();

			return new ApiController.JsonBoardPageModel
			{
				totalThreadCount = hitCount,
				threads = threadModels,
				boardInfo = null
			};
		}

		public async IAsyncEnumerable<PostDocument> GetIndexEntities(string board, ulong minPostNo)
		{
			if (!Boards.Values.Contains(board))
				yield break;

			var (posts, images, threads, _) = dbContext.GetSets(board);
			
			var boardId = Boards.First(x => x.Value == board).Key;

			var query = posts.AsNoTracking()
				.Where(x => x.num > (uint)minPostNo && x.subnum == (uint)0)
				.OrderBy(x => x.num);

			await foreach (var post in query.AsAsyncEnumerable())
			{
				yield return new PostDocument
				{
					BoardId = boardId,
					PostId = post.num,
					ThreadId = post.thread_num,
					IsOp = post.op,
					PostDateUtc = Utility.ConvertNewYorkTimestamp(post.timestamp.Value).UtcDateTime,
					PostRawText = post.comment,
					PosterID = post.poster_hash,
					PosterName = post.name,
					Subject = post.title,
					Tripcode = post.trip,
					IsDeleted = post.deleted,
					MediaFilename = post.media_filename,
					MediaMd5HashBase64 = post.media_hash
				};
			}
		}

		public async Task<bool> DeletePost(ushort boardId, ulong postId, bool banImages)
		{
			if (!Boards.ContainsKey(boardId))
				return false;

			var board = Boards[boardId];

			var (posts, images, threads, deleted) = dbContext.GetSets(board);

			var post = await posts.FirstOrDefaultAsync(x => x.num == (uint)postId);

			if (post == null)
				return false;

			posts.Remove(post);
			deleted.Add(post);

			if (banImages && post.media_id != 0)
			{
				var image = await images.FirstOrDefaultAsync(x => x.media_id == post.media_id);

				if (image != null)
				{
					void tryDeleteFile(string filename)
					{
						if (File.Exists(filename))
						{
							File.Delete(filename);
						}
						else
						{
							Logger.Warning("Banned file does not exist and cannot be deleted: {filename}", filename);
						}
					}

					tryDeleteFile(GetMediaFilename(board, image.media, false));

					if (!string.IsNullOrWhiteSpace(image.preview_op))
						tryDeleteFile(GetMediaFilename(board, image.preview_op, true));

					if (!string.IsNullOrWhiteSpace(image.preview_reply))
						tryDeleteFile(GetMediaFilename(board, image.preview_reply, true));
					
					image.banned = true;
					images.Update(image);
				}
			}

			await dbContext.SaveChangesAsync();
			return true;
		}
	}
}