using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Consumers.Asagi;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;

namespace Hayden.WebServer.Data
{
	public class AsagiDataProvider : IDataProvider
	{
		private AsagiDbContext dbContext { get; }
		private AuxiliaryDbContext AuxiliaryDbContext { get; set; }
		private IOptions<ServerConfig> ServerConfig { get; }

		// TODO: this should not be static
		private static Dictionary<ushort, string> Boards { get; set; } = new();

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Asagi");

		public AsagiDataProvider(AsagiDbContext context, IOptions<ServerConfig> serverConfig, IServiceProvider serviceProvider)
		{
			dbContext = context;
			ServerConfig = serverConfig;
			AuxiliaryDbContext = serviceProvider.GetService<AuxiliaryDbContext>();
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
			string prefix = !string.IsNullOrWhiteSpace(ServerConfig.Value.Data.ImagePrefix) ? ServerConfig.Value.Data.ImagePrefix : "/image";

			return string.Join('/', prefix, GetMediaInternalPath(board, asagiFilename, thumbnail, '/')); 
		}

		private string GetMediaFilename(string board, string asagiFilename, bool thumbnail)
		{
			return Path.Join(ServerConfig.Value.Data.FileLocation, GetMediaInternalPath(board, asagiFilename, thumbnail, Path.DirectorySeparatorChar)); 
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
				deleted = post.deleted,
				dateTime = Utility.ConvertNewYorkTimestamp(post.timestamp).UtcDateTime,
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
							spoiler = post.spoiler,
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

			return new ApiController.JsonThreadModel
			{
				board = CreateBoardInfo(board),
				threadId = threadInfo.thread_num,
				archived = op?.locked ?? false,
				deleted = op?.deleted ?? false,
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
				if (AuxiliaryDbContext != null)
				{
					await AuxiliaryDbContext.Database.EnsureCreatedAsync();

					if (AuxiliaryDbContext.Moderators.All(x => x.Role != ModeratorRole.Admin))
					{
						var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

						ApiController.RegisterCodes.Add(code, ModeratorRole.Admin);

						Console.WriteLine("No admin account detected. Use this code to register an admin account:");
						Console.WriteLine(code);
					}
				}

				await tempContext.Database.OpenConnectionAsync();

				var indexes = AuxiliaryDbContext != null
					? AuxiliaryDbContext.BoardIndexes.AsEnumerable().Select(x => (x.Id, x.ShortName)).ToArray()
					: Array.Empty<(ushort, string)>();

				var boardList = await tempContext.GetBoardTables();

				foreach (var existingTable in indexes)
				{
					Boards[existingTable.Item1] = existingTable.Item2;
				}

				foreach (var newBoard in boardList.Except(Boards.Values.ToArray()).OrderBy(x => x))
				{
					ushort newIndex = 1;

					while (Boards.ContainsKey(newIndex))
						newIndex++;

					Boards[newIndex] = newBoard;

					if (AuxiliaryDbContext != null)
						await SetIndexPosition(newIndex, 0);
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

			if (ServerConfig.Value.Search.Debug)
				Console.WriteLine(JsonConvert.SerializeObject(threadModels));

			return new ApiController.JsonBoardPageModel
			{
				totalThreadCount = hitCount,
				threads = threadModels,
				boardInfo = null
			};
		}

		public async Task<(ushort BoardId, ulong IndexPosition)[]> GetIndexPositions()
		{
			if (AuxiliaryDbContext == null)
				throw new InvalidOperationException("Auxiliary database does not exist");

			return await AuxiliaryDbContext.BoardIndexes.AsAsyncEnumerable().Select(x => (x.Id, x.IndexPosition)).ToArrayAsync();
		}

		public async Task SetIndexPosition(ushort boardId, ulong indexPosition)
		{
			if (AuxiliaryDbContext == null)
				throw new InvalidOperationException("Auxiliary database does not exist");

			var existingIndex = await AuxiliaryDbContext.BoardIndexes.FirstOrDefaultAsync(x => x.Id == boardId);

			if (existingIndex == null)
			{
				existingIndex = new AuxiliaryDbContext.BoardIndex
				{
					Id = boardId,
					ShortName = Boards[boardId],
					IndexPosition = indexPosition
				};

				AuxiliaryDbContext.Add(existingIndex);
			}
			else
			{
				existingIndex.IndexPosition = indexPosition;
				AuxiliaryDbContext.Update(existingIndex);
			}

			await AuxiliaryDbContext.SaveChangesAsync();
			AuxiliaryDbContext.ChangeTracker.Clear();
		}

		public async IAsyncEnumerable<PostIndex> GetIndexEntities(string board, ulong minPostNo)
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
				yield return new PostIndex
				{
					BoardId = boardId,
					PostId = post.num,
					ThreadId = post.thread_num,
					IsOp = post.op,
					PostDateUtc = Utility.ConvertNewYorkTimestamp(post.timestamp).UtcDateTime,
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

						var command = ServerConfig.Value.Extensions.ImageDeleteCommand.Replace("{I}", filename);

						if (!string.IsNullOrWhiteSpace(command))
						{
							if (OperatingSystem.IsLinux())
							{
								Process.Start("bash", new [] { "-c", command });
							}
							else if (OperatingSystem.IsWindows())
							{
								Process.Start("cmd.exe", new[] { "/C", command });
							}
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

		public async Task<DBModerator> GetModerator(ushort userId)
		{
			return await AuxiliaryDbContext.Moderators.FirstOrDefaultAsync(x => x.Id == userId);
		}

		public async Task<DBModerator> GetModerator(string username)
		{
			return await AuxiliaryDbContext.Moderators.FirstOrDefaultAsync(x => x.Username == username);
		}

		public async Task<bool> RegisterModerator(DBModerator moderator)
		{
			if (await AuxiliaryDbContext.Moderators.AnyAsync(x => x.Username == moderator.Username))
				return false;

			dbContext.Add(moderator);
			await dbContext.SaveChangesAsync();
			return true;
		}
	}
}
