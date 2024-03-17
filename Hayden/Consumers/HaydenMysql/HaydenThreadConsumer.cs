using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.MediaInfo;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenThreadConsumer : IThreadConsumer
	{
		protected ConsumerConfig ConsumerConfig { get; }
		protected SourceConfig SourceConfig { get; }
		protected DbContextOptions DbContextOptions { get; set; }
		protected IFileSystem FileSystem { get; set; }
		protected IMediaInspector MediaInspector { get; set; }

		private ILogger Logger { get; } = SerilogManager.CreateSubLogger("HaydenDB");

		protected Dictionary<string, ushort> BoardIdMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

		/// <param name="consumerConfig">The object to load configuration values from.</param>
		public HaydenThreadConsumer(ConsumerConfig consumerConfig, SourceConfig sourceConfig, IFileSystem fileSystem, IMediaInspector mediaInspector)
		{
			ConsumerConfig = consumerConfig;
			SourceConfig = sourceConfig;
			FileSystem = fileSystem;
			MediaInspector = mediaInspector;

			SetUpDBContext();
		}

		private string GetTranslatedBoardName(string boardName)
		{
			if (!SourceConfig.Boards.TryGetValue(boardName, out var boardConfig) || string.IsNullOrWhiteSpace(boardConfig.StoredBoardName))
				return boardName;

			return boardConfig.StoredBoardName;
		}

		public async Task InitializeAsync()
		{
			if (!ConsumerConfig.FullImagesEnabled && ConsumerConfig.ThumbnailsEnabled)
			{
				throw new InvalidOperationException(
					"Consumer cannot be used if thumbnails enabled and full images are not. Full images are required for proper hash calculation");
			}

			await using var context = GetDBContext();

			if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
			{
				try
				{
					await context.Database.OpenConnectionAsync();
				}
				catch (Exception ex)
				{
					throw new Exception("Database cannot be connected to, or is not ready", ex);
				}

				await context.UpgradeOrCreateAsync();
			}

			await foreach (var board in context.Boards)
			{
				BoardIdMappings[board.ShortName] = board.Id;
			}

			foreach (var boardRule in SourceConfig.Boards)
			{
				var translatedBoardName = GetTranslatedBoardName(boardRule.Key);

				if (BoardIdMappings.ContainsKey(translatedBoardName))
					continue;

				var boardObject = new DBBoard
				{
					ShortName = translatedBoardName,
					LongName = translatedBoardName,
					Category = "Archive",
					IsNSFW = false,
					IsReadOnly = true,
					MultiImageLimit = 0,
					ShowsDeletedPosts = true
				};

				context.Add(boardObject);
				await context.SaveChangesAsync();

				BoardIdMappings[translatedBoardName] = boardObject.Id;
			}
		}

		protected virtual void SetUpDBContext()
		{
			var contextBuilder = new DbContextOptionsBuilder();

			if (ConsumerConfig.DatabaseType == DatabaseType.MySql)
			{
				contextBuilder.UseMySql(ConsumerConfig.ConnectionString, ServerVersion.AutoDetect(ConsumerConfig.ConnectionString), x =>
				{
					x.EnableIndexOptimizedBooleanColumns();
				});
			}
			else if (ConsumerConfig.DatabaseType == DatabaseType.Sqlite)
			{
				contextBuilder.UseSqlite(ConsumerConfig.ConnectionString);
			}
			else
			{
				throw new Exception("Unknown database type; not supported by HaydenConsumer");
			}

			DbContextOptions = contextBuilder.Options;
		}

		public Task CommitAsync() => Task.CompletedTask;

		protected virtual HaydenDbContext GetDBContext() => new(DbContextOptions);

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			await using var dbContext = GetDBContext();

			string board = GetTranslatedBoardName(threadUpdateInfo.ThreadPointer.Board);
			ushort boardId = BoardIdMappings[board];

			//{ // delete this block when not testing
			//	string threadDirectory = Path.Combine(Config.DownloadLocation, board, "thread");
			//	string threadFileName = Path.Combine(threadDirectory, $"{threadUpdateInfo.ThreadPointer.ThreadId}.json");

			//	Directory.CreateDirectory(threadDirectory);

			//	YotsubaFilesystemThreadConsumer.PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
			//}

			async Task ProcessImages()
			{
				foreach (var post in threadUpdateInfo.Thread.Posts)
				{
					if (post.Media == null)
						continue;

					foreach (var file in post.Media)
					{
						// TODO: performance could be improved here by batching these requests

						var fileMapping = await dbContext.FileMappings.FirstOrDefaultAsync(x =>
							x.BoardId == boardId && x.PostId == post.PostNumber && x.Index == file.Index);

						if (fileMapping != null && fileMapping.FileId != null)
						{
							var dbFile = await dbContext.Files.FirstAsync(x => x.Id == fileMapping.FileId);

							if (dbFile.FileExists)
								continue;
						}
						
						DBFile existingFile = null;

						if (file.Sha256Hash != null)
						{
							existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
								x.Sha256Hash == file.Sha256Hash
								&& x.BoardId == boardId);
						}

						if (existingFile == null && file.Sha1Hash != null && !ConsumerConfig.IgnoreSha1Hash)
						{
							existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
								x.Sha1Hash == file.Sha1Hash
								&& x.BoardId == boardId);
						}

						if (existingFile == null && file.Md5Hash != null && !ConsumerConfig.IgnoreMd5Hash)
						{
							existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
								x.Md5Hash == file.Md5Hash
								&& x.BoardId == boardId);
						}

						if (fileMapping == null)
						{
							fileMapping = new DBFileMapping
							{
								BoardId = boardId,
								PostId = post.PostNumber,
								FileId = null,
								Filename = file.Filename,
								Index = file.Index,
								IsDeleted = file.IsDeleted,
								IsSpoiler = file.IsSpoiler.GetValueOrDefault(),
								AdditionalMetadata = file.AdditionalMetadata?.Serialize()
							};

							dbContext.Add(fileMapping);
						}
						else
						{
							dbContext.Update(fileMapping);
						}

						if (existingFile != null)
						{
							// We know we have the file. Just attach it
							fileMapping.FileId = existingFile.Id;

							//if (existingFile.FileExists)
							continue;
						}


						Uri imageUrl = null, thumbUrl = null;

						if (ConsumerConfig.FullImagesEnabled && file.FileUrl != null)
							imageUrl = new Uri(file.FileUrl);

						if (ConsumerConfig.ThumbnailsEnabled && file.ThumbnailUrl != null)
							thumbUrl = new Uri(file.ThumbnailUrl);

						if (imageUrl != null || thumbUrl != null)
						{
							imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
							{
								["board"] = board,
								["boardId"] = boardId,
								["postNumber"] = post.PostNumber,
								["media"] = file
							}));
						}
						else
						{
							AddMissingMappingMetadata(fileMapping, file);
						}
					}
				}
			}

			if (threadUpdateInfo.IsNewThread)
			{
				var dbThread = new DBThread
				{
					BoardId = boardId,
					ThreadId = threadUpdateInfo.ThreadPointer.ThreadId,
					IsDeleted = threadUpdateInfo.Thread.AdditionalMetadata?.Deleted
						?? threadUpdateInfo.Thread.Posts.FirstOrDefault(x => x.PostNumber == threadUpdateInfo.ThreadPointer.ThreadId)?.IsDeleted
						?? false,
					IsArchived = threadUpdateInfo.Thread.IsArchived,
					LastModified = DateTime.MinValue,
					Title = threadUpdateInfo.Thread.Title.TrimAndNullify(),
					AdditionalMetadata = threadUpdateInfo.Thread.AdditionalMetadata?.Serialize()
				};

				dbContext.Add(dbThread);
				await dbContext.SaveChangesAsync();
			}

			HashSet<ulong> postNumbersToSkip = null;

			if (!threadUpdateInfo.IsNewThread && threadUpdateInfo.NewPosts.Any(x => x.IsDeleted == true))
			{
				var checkedPostIds = threadUpdateInfo.NewPosts.Where(x => x.IsDeleted == true)
					.Select(x => x.PostNumber)
					.ToArray();

				var skippablePostIds = await dbContext.Posts
					.Where(x => x.BoardId == boardId && checkedPostIds.Contains(x.PostId))
					.Select(x => x.PostId)
					.ToArrayAsync();

				postNumbersToSkip = new HashSet<ulong>(skippablePostIds);
			}
			
			foreach (var post in threadUpdateInfo.NewPosts)
			{
				if (postNumbersToSkip != null && postNumbersToSkip.Contains(post.PostNumber))
				{
					// due to limitations with the thread tracking method, deleted posts don't get processed correctly
					
					continue;
				}

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
					AdditionalMetadata = post.AdditionalMetadata?.Serialize()
				});
			}

			await dbContext.SaveChangesAsync();
			
			await ProcessImages();

			await dbContext.SaveChangesAsync();

			if (ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative)
				foreach (var post in threadUpdateInfo.UpdatedPosts)
				{
					Logger.Debug("Post /{board}/{postNumber} has been modified", board, post.PostNumber);

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

			if (ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative)
				foreach (var postNumber in threadUpdateInfo.DeletedPosts)
				{
					Logger.Debug("Post /{board}/{postNumber} has been deleted", board, postNumber);

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

			await using var dbContext = GetDBContext();

			var existingFileMapping = await dbContext.FileMappings
				.FirstAsync(x => x.BoardId == boardId
								 && x.PostId == postNumber
								 && x.Index == media.Index);

			if (imageTempFilename == null)
			{
				//throw new InvalidOperationException("Full image required for hash calculation");
				//Program.Log("Full image required for hash calculation");

				AddMissingMappingMetadata(existingFileMapping, media);

				dbContext.Update(existingFileMapping);
				await dbContext.SaveChangesAsync();

				// TODO: thumbnail-only handling
				return;
			}

			await using var stream = FileSystem.File.OpenRead(imageTempFilename);

			var fileSize = stream.Length;
			var (md5Hash, sha1Hash, sha256Hash) = Utility.CalculateHashes(stream);

			stream.Close();

			uint fileId;

			var existingFile = await dbContext.Files
				.Where(x => x.Sha256Hash == sha256Hash
							&& x.BoardId == boardId)
				.FirstOrDefaultAsync();

			if (existingFile?.FileBanned == true)
				return;

			var imageFilename = Common.CalculateFilename(ConsumerConfig.DownloadLocation, board, Common.MediaType.Image, sha256Hash, media.FileExtension);

			FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(imageFilename));

			if (!FileSystem.File.Exists(imageFilename))
				FileSystem.File.Move(imageTempFilename, imageFilename);

			if (thumbTempFilename != null)
			{
				var thumbFilename = Common.CalculateFilename(ConsumerConfig.DownloadLocation, board, Common.MediaType.Thumbnail, sha256Hash, media.ThumbnailExtension);

				FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(thumbFilename));

				if (!FileSystem.File.Exists(thumbFilename))
					FileSystem.File.Move(thumbTempFilename, thumbFilename);
			}

			string thumbnailExtension =
				thumbTempFilename != null ? media.ThumbnailExtension.TrimStart('.').ToLower() : null;

			if (existingFile == null)
			{
				var dbFile = new DBFile
				{
					BoardId = boardId,
					Extension = media.FileExtension.TrimStart('.').ToLower(),
					FileExists = true,
					FileBanned = false,
					ThumbnailExtension = thumbnailExtension,
					Md5Hash = md5Hash,
					Sha1Hash = sha1Hash,
					Sha256Hash = sha256Hash,
					Size = (uint)fileSize
				};

				await MediaInspector.DetermineMediaInfoAsync(imageFilename, dbFile);

				dbContext.Add(dbFile);
				await dbContext.SaveChangesAsync();

				fileId = dbFile.Id;
			}
			else
			{
				fileId = existingFile.Id;

				if (!existingFile.FileExists && imageTempFilename != null)
				{
					existingFile.FileExists = true;
					dbContext.Update(existingFile);
				}

				if (existingFile.ThumbnailExtension == null && thumbTempFilename != null)
				{
					existingFile.ThumbnailExtension = thumbnailExtension;
					dbContext.Update(existingFile);
				}
			}

			existingFileMapping.FileId = fileId;

			dbContext.Update(existingFileMapping);

			await dbContext.SaveChangesAsync();
		}

		private static void AddMissingMappingMetadata(DBFileMapping existingFileMapping, Media media)
		{
			var metadata = existingFileMapping.AdditionalMetadata != null
				? JObject.Parse(existingFileMapping.AdditionalMetadata)
				: new JObject();

			if (media.Md5Hash != null)
				metadata["missing_md5hash"] = Convert.ToBase64String(media.Md5Hash);

			if (media.Sha1Hash != null)
				metadata["missing_sha1hash"] = Convert.ToBase64String(media.Sha1Hash);

			if (media.Sha256Hash != null)
				metadata["missing_sha256hash"] = Convert.ToBase64String(media.Sha256Hash);

			if (!string.IsNullOrWhiteSpace(media.FileExtension))
				metadata["missing_extension"] = media.FileExtension.TrimStart('.');

			if (media.FileSize.HasValue)
				metadata["missing_size"] = media.FileSize;
			
			existingFileMapping.AdditionalMetadata = metadata.Count > 0 ? metadata.ToString(Formatting.None) : null;
		}

		/// <inheritdoc/>
		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			await UpdateThread(GetTranslatedBoardName(board), threadId, deleted, !deleted);
		}
		
		protected async Task UpdateThread(string board, ulong threadId, bool deleted, bool archived)
		{
			ushort boardId = BoardIdMappings[GetTranslatedBoardName(board)];

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
		public async Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
		{
			ushort boardId = BoardIdMappings[GetTranslatedBoardName(board)];

			await using var dbContext = GetDBContext();

			var query = dbContext.Threads.Where(x => x.BoardId == boardId && threadIdsToCheck.Contains(x.ThreadId));

			if (archivedOnly)
				query = query.Where(x => x.IsArchived);

			var items = new List<ExistingThreadInfo>();

			if (metadataMode == MetadataMode.FullHashMetadata)
			{
				var threadInfos = await query.Select(x => new { x.ThreadId, x.LastModified, x.IsArchived }).ToDictionaryAsync(x => x.ThreadId);
				
				var postQuery =
					dbContext.Posts.Where(x => x.BoardId == boardId && threadInfos.Keys.Contains(x.ThreadId) && (!excludeDeletedPosts || !x.IsDeleted))
						.SelectMany(x => dbContext.FileMappings.Where(y => y.BoardId == boardId && y.PostId == x.PostId).DefaultIfEmpty(), (post, mapping) => new { post, mapping });

				var allPosts = await postQuery.ToArrayAsync();

				foreach (var threadGrouping in allPosts.GroupBy(x => x.post.ThreadId))
				{
					var postGroupings =
						threadGrouping.GroupByCustomKey(x => x.post.PostId,
							x => x.post,
							x => x.mapping);

					var threadInfo = threadInfos[threadGrouping.Key];
					var hashes = new List<(ulong PostId, uint PostHash)>();

					foreach (var postGroup in postGroupings)
					{
						var hash = CalculatePostHash(postGroup.Key.ContentHtml, postGroup.Key.ContentRaw,
							postGroup.Count(x => x.IsSpoiler), postGroup.Count(), postGroup.Count(x => x.IsDeleted));

						hashes.Add((postGroup.Key.PostId, hash));
					}

					items.Add(new ExistingThreadInfo(threadInfo.ThreadId, threadInfo.IsArchived, new DateTimeOffset(threadInfo.LastModified, TimeSpan.Zero), hashes));
				}
			}
			else if (metadataMode == MetadataMode.ThreadIdAndPostId)
			{
				var postIds = await dbContext.Posts.Where(y => y.BoardId == boardId && query.Select(x => x.ThreadId).Contains(y.ThreadId))
					.Select(x => new { x.ThreadId, x.PostId })
					.ToArrayAsync();
				
				foreach (var group in postIds.GroupBy(x => x.ThreadId, x => x.PostId))
				{
					items.Add(new ExistingThreadInfo(group.Key, false, DateTimeOffset.MinValue, group.Select(x => (x, (uint)0)).ToArray()));
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