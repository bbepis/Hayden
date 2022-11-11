using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenMysqlThreadConsumer : IThreadConsumer
	{
		private ConsumerConfig Config { get; }
		private IFileSystem FileSystem { get; set; }

		protected Dictionary<string, ushort> BoardIdMappings { get; } = new();

		/// <param name="config">The object to load configuration values from.</param>
		public HaydenMysqlThreadConsumer(ConsumerConfig config, IFileSystem fileSystem)
		{
			Config = config;
			FileSystem = fileSystem;
		}

		public async Task InitializeAsync()
		{
			// TODO: logic to initialize unseen boards

			if (!Config.FullImagesEnabled && Config.ThumbnailsEnabled)
			{
				throw new InvalidOperationException(
					"Consumer cannot be used if thumbnails enabled and full images are not. Full images are required for proper hash calculation");
			}

			await using var context = GetDBContext();

			await foreach (var board in context.Boards)
			{
				BoardIdMappings[board.ShortName] = board.Id;
			}
		}

		protected virtual HaydenDbContext GetDBContext()
		{
			var contextBuilder = new DbContextOptionsBuilder();

			contextBuilder.UseMySql(Config.ConnectionString, ServerVersion.AutoDetect(Config.ConnectionString), x =>
			{
				x.EnableIndexOptimizedBooleanColumns();
			});

			return new HaydenDbContext(contextBuilder.Options);
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			await using var dbContext = GetDBContext();

			string board = threadUpdateInfo.ThreadPointer.Board;
			ushort boardId = BoardIdMappings[board];

			//{ // delete this block when not testing
			//	string threadDirectory = Path.Combine(Config.DownloadLocation, board, "thread");
			//	string threadFileName = Path.Combine(threadDirectory, $"{threadUpdateInfo.ThreadPointer.ThreadId}.json");

			//	Directory.CreateDirectory(threadDirectory);

			//	YotsubaFilesystemThreadConsumer.PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
			//}

			async Task ProcessImages(Post post)
			{
				if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
					return; // skip the DB check since we're not even bothering with images

				if (post.Media == null)
					return;

				foreach (var file in post.Media)
				{
					DBFile existingFile = null;

					if (file.Sha256Hash != null)
					{
						existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
							x.Sha256Hash == file.Sha256Hash
							&& x.BoardId == boardId);
					}
					
					if (existingFile == null && file.Sha1Hash != null && !Config.IgnoreSha1Hash)
					{
						existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
							x.Sha1Hash == file.Sha1Hash
							&& x.BoardId == boardId);
					}
					
					if (existingFile == null && file.Md5Hash != null && !Config.IgnoreMd5Hash)
					{
						existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
							x.Md5Hash == file.Md5Hash
							&& x.BoardId == boardId);
					}

					var fileMapping = new DBFileMapping
					{
						BoardId = boardId,
						PostId = post.PostNumber,
						FileId = null,
						Filename = file.Filename,
						Index = file.Index,
						IsDeleted = file.IsDeleted,
						IsSpoiler = file.IsSpoiler.GetValueOrDefault()
					};

					dbContext.Add(fileMapping);

					if (existingFile != null)
					{
						// We know we have the file. Just attach it
						fileMapping.FileId = existingFile.Id;

						return;
					}


					Uri imageUrl = null, thumbUrl = null;

					if (Config.FullImagesEnabled && file.FileUrl != null)
						imageUrl = new Uri(file.FileUrl);

					if (Config.ThumbnailsEnabled && file.ThumbnailUrl != null)
						thumbUrl = new Uri(file.ThumbnailUrl);

					imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
					{
						["board"] = board,
						["boardId"] = boardId,
						["postNumber"] = post.PostNumber,
						["media"] = file
					}));
				}
			}

			if (threadUpdateInfo.IsNewThread)
			{
				var dbThread = new DBThread
				{
					BoardId = boardId,
					ThreadId = threadUpdateInfo.ThreadPointer.ThreadId,
					IsDeleted = false,
					IsArchived = false,
					LastModified = DateTime.MinValue,
					Title = threadUpdateInfo.Thread.Title.TrimAndNullify()
				};

				dbContext.Add(dbThread);
				await dbContext.SaveChangesAsync();
			}

			foreach (var post in threadUpdateInfo.NewPosts)
			{
				dbContext.Add(new DBPost
				{
					BoardId = boardId,
					PostId = post.PostNumber,
					ThreadId = threadUpdateInfo.ThreadPointer.ThreadId,
					ContentHtml = post.ContentRendered.TrimAndNullify(),
					ContentRaw = post.ContentRaw.TrimAndNullify(),
					ContentType = post.ContentType,
					IsDeleted = post.IsDeleted ?? false,
					Author = post.Author == "Anonymous" ? null : post.Author.TrimAndNullify(),
					Tripcode = post.Tripcode.TrimAndNullify(),
					Email = post.Email.TrimAndNullify(),
					DateTime = post.TimePosted.UtcDateTime,
					AdditionalMetadata = (post.AdditionalMetadata?.Count ?? 0) == 0 ? null : post.AdditionalMetadata.ToString(Formatting.None)
				});
			}

			await dbContext.SaveChangesAsync();
			
			foreach (var post in threadUpdateInfo.NewPosts)
			{
				await ProcessImages(post);
			}

			await dbContext.SaveChangesAsync();

			foreach (var post in threadUpdateInfo.UpdatedPosts)
			{
				Program.Log($"[DB] Post /{board}/{post.PostNumber} has been modified", true);

				var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == post.PostNumber);
				var dbPostMappings = await dbContext.FileMappings.Where(x => x.BoardId == boardId && x.PostId == post.PostNumber).ToArrayAsync();
				
				//if (post.Comment != dbPost.ContentHtml)
				if ((dbPost.ContentRaw != null && post.ContentRaw != dbPost.ContentRaw) || (dbPost.ContentRaw == null && post.ContentRendered != dbPost.ContentHtml))
				{
					// this needs to be made more efficient
					// this also doesn't cooperate well with deadlinks (why the fuck is that passed through the api html render?)

					var jsonAdditionalMetadata = !string.IsNullOrWhiteSpace(dbPost.AdditionalMetadata)
						? JObject.Parse(dbPost.AdditionalMetadata)
						: new JObject();

					const string jsonKey = "content_modifications";

					var modificationsArray = jsonAdditionalMetadata.GetValue(jsonKey) as JArray ??
											 new JArray();

					modificationsArray.Add(JToken.FromObject(new
					{
						time = DateTimeOffset.UtcNow,
						old_content_raw = dbPost.ContentRaw,
						old_content_html = dbPost.ContentHtml,
						new_content_raw = post.ContentRaw,
						new_content_html = post.ContentRendered
					}));

					jsonAdditionalMetadata[jsonKey] = modificationsArray;
					dbPost.AdditionalMetadata = jsonAdditionalMetadata.ToString(Formatting.None);
					
					dbPost.ContentHtml = post.ContentRendered.TrimAndNullify();
					dbPost.ContentRaw = post.ContentRaw.TrimAndNullify();
				}

				dbPost.IsDeleted = false;

				foreach (var dbPostMapping in dbPostMappings)
				{
					if (post.Media == null
					    || post.Media.Length == 0
					    || post.Media.Any(x => x.Filename == dbPostMapping.Filename && x.IsDeleted)
					    || post.Media.All(x => x.Filename != dbPostMapping.Filename))
						dbPostMapping.IsDeleted = true;
				}
			}

			foreach (var postNumber in threadUpdateInfo.DeletedPosts)
			{
				Program.Log($"[DB] Post /{board}/{postNumber} has been deleted", true);

				var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == postNumber);

				dbPost.IsDeleted = true;
			}

			await dbContext.SaveChangesAsync();

			await UpdateThread(board, threadUpdateInfo.ThreadPointer.ThreadId, false, threadUpdateInfo.Thread.IsArchived);
			
			return imageDownloads;
		}

		/// <inheritdoc/>
		public async Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
		{
			if (!queuedImageDownload.TryGetProperty("board", out string board)
				|| !queuedImageDownload.TryGetProperty("boardId", out ushort boardId)
				|| !queuedImageDownload.TryGetProperty("postNumber", out ulong postNumber)
				|| !queuedImageDownload.TryGetProperty("media", out Media media))
			{
				throw new InvalidOperationException("Queued image download did not have the required properties");
			}
			
			if (imageTempFilename == null)
				throw new InvalidOperationException("Full image required for hash calculation");

			await using var stream = FileSystem.File.OpenRead(imageTempFilename);

			var fileSize = stream.Length;
			var (md5Hash, sha1Hash, sha256Hash) = Utility.CalculateHashes(stream);

			stream.Close();
			
			await using var dbContext = GetDBContext();

			uint fileId;

			var existingFile = await dbContext.Files
				.Where(x => x.Sha256Hash == sha256Hash
				            && x.BoardId == boardId)
				.Select(x => new { x.Id })
				.FirstOrDefaultAsync();

			var imageFilename = Common.CalculateFilename(Config.DownloadLocation, board, Common.MediaType.Image, sha256Hash, media.FileExtension);

			FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(imageFilename));

			if (!FileSystem.File.Exists(imageFilename))
				FileSystem.File.Move(imageTempFilename, imageFilename);

			if (thumbTempFilename != null)
			{
				var thumbFilename = Common.CalculateFilename(Config.DownloadLocation, board, Common.MediaType.Thumbnail, sha256Hash, media.ThumbnailExtension);

				FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(thumbFilename));

				if (!FileSystem.File.Exists(thumbFilename))
					FileSystem.File.Move(thumbTempFilename, thumbFilename);
			}

			if (existingFile == null)
			{
				var dbFile = new DBFile
				{
					BoardId = boardId,
					Extension = media.FileExtension.TrimStart('.'),
					FileExists = true,
					FileBanned = false,
					ThumbnailExtension = thumbTempFilename != null ? media.ThumbnailExtension.TrimStart('.') : null,
					Md5Hash = md5Hash,
					Sha1Hash = sha1Hash,
					Sha256Hash = sha256Hash,
					Size = (uint)fileSize
				};

				await Common.DetermineMediaInfoAsync(imageFilename, dbFile);

				dbContext.Add(dbFile);
				await dbContext.SaveChangesAsync();

				fileId = dbFile.Id;
			}
			else
			{
				fileId = existingFile.Id;
			}

			var existingFileMapping = await dbContext.FileMappings
				.FirstAsync(x => x.BoardId == boardId
					&& x.PostId == postNumber
					&& x.Index == media.Index);

			existingFileMapping.FileId = fileId;

			dbContext.Update(existingFileMapping);

			await dbContext.SaveChangesAsync();
		}

		/// <inheritdoc/>
		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			await UpdateThread(board, threadId, deleted, !deleted);
		}
		
		protected async Task UpdateThread(string board, ulong threadId, bool deleted, bool archived)
		{
			ushort boardId = BoardIdMappings[board];

			await using var dbContext = GetDBContext();

			var thread = await dbContext.Threads.FirstOrDefaultAsync(x => x.ThreadId == threadId && x.BoardId == boardId);

			if (thread == null)
			{
				// tried to mark a non-existent thread as deleted
				return;
			}

			thread.IsDeleted = deleted;
			thread.IsArchived = archived;

			thread.LastModified = await dbContext.Posts.Where(x => x.BoardId == boardId && x.ThreadId == threadId)
				.Select(x => x.DateTime)
				.DefaultIfEmpty()
				.MaxAsync();

			await dbContext.SaveChangesAsync();
		}

		/// <inheritdoc/>
		public async Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getMetadata = true)
		{
			ushort boardId = BoardIdMappings[board];

			await using var dbContext = GetDBContext();

			var query = dbContext.Threads.Where(x => x.BoardId == boardId && threadIdsToCheck.Contains(x.ThreadId));

			if (archivedOnly)
				query = query.Where(x => x.IsArchived);

			var items = new List<ExistingThreadInfo>();

			if (getMetadata)
			{
				foreach (var threadInfo in await query.Select(x => new { x.ThreadId, x.LastModified, x.IsArchived }).ToArrayAsync())
				{
					var hashes = new List<(ulong PostId, uint PostHash)>();

					var postQuery =
						dbContext.Posts.Where(x => x.BoardId == boardId && x.ThreadId == threadInfo.ThreadId)
							//.Join(dbContext.FileMappings, dbPost => new { dbPost.BoardId, dbPost.PostId }, dbFileMapping => new { dbFileMapping.BoardId, dbFileMapping.PostId }, (post, mapping) => new { post, mapping });
							.SelectMany(x => dbContext.FileMappings.Where(y => y.BoardId == boardId && y.PostId == x.PostId).DefaultIfEmpty(), (post, mapping) => new { post, mapping });

					var postGroupings =
						(await postQuery.ToArrayAsync()).GroupByCustomKey(x => x.post.PostId,
							x => x.post,
							x => x.mapping);

					foreach (var postGroup in postGroupings)
					{
						var hash = CalculatePostHash(postGroup.Key.ContentHtml, postGroup.Key.ContentRaw,
							postGroup.Count(x => x.IsSpoiler), postGroup.Count(), postGroup.Count(x => x.IsDeleted));

						hashes.Add((postGroup.Key.PostId, hash));
					}

					items.Add(new ExistingThreadInfo(threadInfo.ThreadId, threadInfo.IsArchived, new DateTimeOffset(threadInfo.LastModified, TimeSpan.Zero), hashes));
				}
			}
			else
			{
				await foreach (var threadId in query.Select(x => x.ThreadId).AsAsyncEnumerable())
				{
					items.Add(new ExistingThreadInfo(threadId));
				}
			}

			return items;
		}

		public static uint CalculatePostHash(string postHtml, string postRawContent, int spoilerCount, int fileCount, int deletedFileCount)
		{
			// The HTML content of a post can change due to public warnings and bans.
			uint hashCode = Utility.FNV1aHash32(postHtml);
			Utility.FNV1aHash32(postRawContent, ref hashCode);

			// Attached files can be removed, and have their spoiler status changed
			Utility.FNV1aHash32(spoilerCount, ref hashCode);
			Utility.FNV1aHash32(fileCount, ref hashCode);
			Utility.FNV1aHash32(deletedFileCount, ref hashCode);

			return hashCode;
		}

		/// <inheritdoc />
		public uint CalculateHash(Post post)
			=> CalculatePostHash(post.ContentRendered, post.ContentRaw,
				post.Media.Count(x => x.IsSpoiler ?? false),
				post.Media.Length,
				post.Media.Count(x => x.IsDeleted));

		/// <summary>
		/// Disposes the object.
		/// </summary>
		public void Dispose() { }
	}
}