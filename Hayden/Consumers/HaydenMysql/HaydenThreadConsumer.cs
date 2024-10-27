using System;
using System.Collections.Generic;
using System.IO;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using static Hayden.Common;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenThreadConsumer : IThreadConsumer
	{
		protected ConsumerConfig ConsumerConfig { get; }
		protected SourceConfig SourceConfig { get; }
		protected DbContextOptions<HaydenDbContext> DbContextOptions { get; set; }
		protected PooledDbContextFactory<HaydenDbContext> DbContextPool { get; set; }
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
			DbContextOptions = new DbContextOptionsBuilder<HaydenDbContext>()
				.SetupHaydenDb(consumerConfig: ConsumerConfig)
				.Options;

			DbContextPool = new PooledDbContextFactory<HaydenDbContext>(DbContextOptions);
		}

		public Task CommitAsync() => Task.CompletedTask;

		protected virtual HaydenDbContext GetDBContext() => DbContextPool.CreateDbContext(); // new(DbContextOptions); 

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			await using var dbContext = GetDBContext();

			// A lot of this will look a bit verbose and unclear, but it's to do with how EF Core handles entity tracking
			// Basically, to maximise performance and minimise wasted cycles on change tracking, I follow a lot of practices here:
			// https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics
			// Which is very far away from how you'd expect normal EF Core code to be written. However this gives a +25% performance boost

			try
			{
				dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

				List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

				string board = GetTranslatedBoardName(threadUpdateInfo.ThreadPointer.Board);
				ushort boardId = BoardIdMappings[board];

				async Task ProcessImages()
				{
					// NOTE: There's an edge case I'm not handling here. If someone changes the config settings
					//   for if full images or thumbnails should be downloaded, those changes will only be applied to posts detected as new.
					// This could be handled by doing DB checks for unchanged posts, but it'll tank performance

					// Handle new image downloads
					// Going to be doing this in a slightly weird way. We want to batch together as many inserts as we can

					var fileMappingDict = new List<(DBFileMapping, DBFile, Media)>();

					void QueueDownload(Media media, DBFile dbFile)
					{
						if (dbFile.FileBanned)
							return;

						Uri imageUrl = null, thumbUrl = null;

						if (ConsumerConfig.FullImagesEnabled && media.FileUrl != null && !dbFile.FileExists)
							imageUrl = new Uri(media.FileUrl);

						if (ConsumerConfig.ThumbnailsEnabled && media.ThumbnailUrl != null && !dbFile.ThumbnailExists)
							thumbUrl = new Uri(media.ThumbnailUrl);

						if (imageUrl != null || thumbUrl != null)
						{
							imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
							{
								["fileId"] = dbFile.Id,
								["media"] = media
							}));
						}
					}

				

					List<byte[]> Sha256Hashes = new List<byte[]>();
					List<byte[]> Sha1Hashes = new List<byte[]>();
					List<byte[]> Md5Hashes = new List<byte[]>();

				
					foreach (var post in threadUpdateInfo.NewPosts)
					{
						foreach (var media in post.Media)
						{
							if (media.Sha256Hash != null)
								Sha256Hashes.Add(media.Sha256Hash);
							if (media.Sha1Hash != null)
								Sha1Hashes.Add(media.Sha1Hash);
							if (media.Md5Hash != null)
								Md5Hashes.Add(media.Md5Hash);
						}
					}

					// do this in bulk so we're not doing thousands of sequential database calls
					DBFile[] allMatchingFiles;

					if (Sha256Hashes.Count > 0 || Sha1Hashes.Count > 0 || Md5Hashes.Count > 0)
					{
						allMatchingFiles = await dbContext.Files
							.AsNoTracking()
							.Where(file =>
								Sha256Hashes.Contains(file.Sha256Hash)
								|| Sha1Hashes.Contains(file.Sha256Hash)
								|| Md5Hashes.Contains(file.Md5Hash))
							.ToArrayAsync();
					}
					else
					{
						allMatchingFiles = Array.Empty<DBFile>();
					}

					foreach (var post in threadUpdateInfo.NewPosts)
					{
						foreach (var media in post.Media)
						{
							DBFile existingFile = null;

							// Order is important here. We want to rely on SHA256 first, then SHA1, then MD5
							if (media.Sha256Hash != null)
							{
								existingFile = allMatchingFiles.FirstOrDefault(x =>
									x.Sha256Hash.ByteArrayEquals(media.Sha256Hash));
							}

							if (existingFile == null && media.Sha1Hash != null && !ConsumerConfig.IgnoreSha1Hash)
							{
								existingFile = allMatchingFiles.FirstOrDefault(x =>
									x.Sha1Hash.ByteArrayEquals(media.Sha1Hash));
							}

							if (existingFile == null && media.Md5Hash != null && !ConsumerConfig.IgnoreMd5Hash)
							{
								existingFile = allMatchingFiles.FirstOrDefault(x =>
									x.Md5Hash.ByteArrayEquals(media.Md5Hash));
							}

							var fileMapping = new DBFileMapping
							{
								BoardId = boardId,
								PostId = post.PostNumber,
								FileId = null,
								Filename = media.Filename ?? "",
								Index = media.Index,
								IsDeleted = media.IsDeleted,
								IsSpoiler = media.IsSpoiler.GetValueOrDefault(),
								AdditionalMetadata = media.AdditionalMetadata?.Serialize()
							};

							if (existingFile != null)
							{
								// The file already exists in the DB.
								fileMapping.FileId = existingFile.Id;

								dbContext.Add(fileMapping);

								QueueDownload(media, existingFile);
							}
							else if (media.AdditionalMetadata?.ExternalMediaUrl != null)
							{
								// Dealing with an embed. Put it in the mapping, but don't create or assign a file for it
								dbContext.Add(fileMapping);
							}
							else
							{
								// Need to create a new file and attach the mapping. Can't attach it to the mapping until it gets saved
								//   to the database, so keep track of it in a dictionary

								var newFile = new DBFile
								{
									Sha256Hash = media.Sha256Hash,
									Sha1Hash = media.Sha1Hash,
									Md5Hash = media.Md5Hash,
									FileExists = false,
									ThumbnailExists = false,
									Size = media.FileSize ?? 0,
									Extension = media.FileExtension.TrimStart('.'),
									ThumbnailExtension = media.ThumbnailExtension?.TrimStart('.')
								};

								dbContext.Add(newFile);

								fileMappingDict.Add((fileMapping, newFile, media));
							}
						}
					}

					dbContext.ChangeTracker.DetectChanges();
					await dbContext.SaveChangesAsync();
					dbContext.ChangeTracker.Clear();

					// files will have assigned IDs now
					foreach (var (mapping, file, media) in fileMappingDict)
					{
						mapping.FileId = file.Id;
						dbContext.Add(mapping);

						QueueDownload(media, file);
					}

					// update deletion statuses for modified posts

					if (threadUpdateInfo.UpdatedPosts.Count > 0)
					{
						var postIds = threadUpdateInfo.UpdatedPosts.Select(x => x.PostNumber).ToArray();

						var existingFileMappings = await dbContext.FileMappings
							.AsNoTracking()
							.Where(x => x.BoardId == boardId && postIds.Contains(x.PostId))
							.ToArrayAsync();

						foreach (var post in threadUpdateInfo.UpdatedPosts)
						{
							var postMappings = existingFileMappings.Where(x => x.PostId == post.PostNumber).ToArray();

							if (post.Media.Length == 0)
							{
								foreach (var mapping in postMappings)
								{
									mapping.IsDeleted = true;
									dbContext.Update(mapping);
								}

								continue;
							}

							if (post.Media.Length < postMappings.Length)
							{
								// We can probably add some heuristics here, but it'd be too much of a pain in the ass
								Logger.Warning("Post /{postBoard}/{postNumber} has changed its file count from {prevNumber} to {postNumber}. A file deletion might have happened; it could not be determined which file",
									threadUpdateInfo.ThreadPointer.Board, post.PostNumber, postMappings.Length, post.Media.Length);
								continue;
							}

							foreach (var media in post.Media)
							{
								var existingFile = postMappings.First(x => x.Index == media.Index);

								if (media.IsDeleted && existingFile.IsDeleted != media.IsDeleted)
								{
									existingFile.IsDeleted = media.IsDeleted;
									dbContext.Update(existingFile);
								}
							}
						}
					}
				}

				void CreateThread()
				{
					var dbThread = new DBThread
					{
						BoardId = boardId,
						ThreadId = threadUpdateInfo.ThreadPointer.ThreadId,
						IsDeleted = threadUpdateInfo.Thread.AdditionalMetadata?.Deleted
						            ?? threadUpdateInfo.Thread.Posts.FirstOrDefault(x => x.PostNumber == threadUpdateInfo.ThreadPointer.ThreadId)?.IsDeleted
						            ?? false,
						IsArchived = threadUpdateInfo.Thread.IsArchived,
						LastModified = threadUpdateInfo.Thread.Posts.DefaultIfEmpty().Max(x => x.TimePosted).UtcDateTime,
						Title = threadUpdateInfo.Thread.Title.TrimAndNullify(),
						AdditionalMetadata = threadUpdateInfo.Thread.AdditionalMetadata?.Serialize()
					};

					dbContext.Add(dbThread);
				}

				if (threadUpdateInfo.IsNewThread)
				{
					CreateThread();
				}
				else if (threadUpdateInfo.NewPosts.Count > 0
					|| threadUpdateInfo.Thread.IsArchived
					|| threadUpdateInfo.Thread.Posts[0].IsDeleted == true)
				{
					var dbThread = await dbContext.Threads.FirstOrDefaultAsync(x =>
						x.BoardId == boardId && x.ThreadId == threadUpdateInfo.ThreadPointer.ThreadId);

					if (dbThread != null)
					{
						dbThread.IsDeleted = threadUpdateInfo.Thread.Posts[0].IsDeleted ?? false;
						dbThread.IsArchived = threadUpdateInfo.Thread.IsArchived;

						var newLastModified = threadUpdateInfo.Thread.Posts.Max(x => x.TimePosted).UtcDateTime;

						// it's possible a newer post exists in the DB that we don't have here
						if (newLastModified > dbThread.LastModified)
							dbThread.LastModified = newLastModified;

						dbContext.Update(dbThread);
					}
					else
					{
						CreateThread();
					}
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

				if (ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative)
					foreach (var post in threadUpdateInfo.UpdatedPosts)
					{
						Logger.Debug("Post /{board}/{postNumber} has been modified", board, post.PostNumber);

						var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == post.PostNumber);

						//var dbPostMappings = await dbContext.FileMappings
						//	.AsNoTracking()
						//	.Where(x => x.BoardId == boardId && x.PostId == post.PostNumber).ToArrayAsync();
					
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
						dbContext.Update(dbPost);

						//foreach (var dbPostMapping in dbPostMappings)
						//{
						//	if (post.Media == null
						//		|| post.Media.Length == 0
						//		|| post.Media.Any(x => x.Filename == dbPostMapping.Filename && x.IsDeleted)
						//		|| post.Media.All(x => x.Filename != dbPostMapping.Filename))
						//	{
						//		dbPostMapping.IsDeleted = true;
						//		dbContext.Update(dbPostMapping);
						//	}
						//}
					}

				if (ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative)
					foreach (var postNumber in threadUpdateInfo.DeletedPosts)
					{
						Logger.Debug("Post /{board}/{postNumber} has been deleted", board, postNumber);

						var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == postNumber);

						dbPost.IsDeleted = true;
						dbContext.Update(dbPost);
					}
				
				dbContext.ChangeTracker.DetectChanges();
				await dbContext.SaveChangesAsync();
				dbContext.ChangeTracker.Clear();
			
				await ProcessImages();
			
				dbContext.ChangeTracker.DetectChanges();
				await dbContext.SaveChangesAsync();
				dbContext.ChangeTracker.Clear();
			
				return imageDownloads;

			}
			finally
			{
				dbContext.ChangeTracker.AutoDetectChangesEnabled = true;
			}
		}

		/// <inheritdoc/>
		public async Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
		{
			if (!queuedImageDownload.TryGetProperty("fileId", out uint fileId)
				|| !queuedImageDownload.TryGetProperty("media", out Media media))
			{
				Logger.Error("Queued image download did not have the required properties. URL: {url}", queuedImageDownload.FullImageUri);
				return;
			}

			await using var dbContext = GetDBContext();

			var file = dbContext.Files.FirstOrDefault(x => x.Id == fileId);

			if (file == null)
			{
				Logger.Error("Could not find relevant file in database for download. URL: {url}", queuedImageDownload.FullImageUri);
				return;
			}

			if (file.FileBanned)
				return;

			if (imageTempFilename != null)
			{
				await using var stream = FileSystem.File.OpenRead(imageTempFilename);

				var fileSize = stream.Length;
				var (md5Hash, sha1Hash, sha256Hash) = Utility.CalculateHashes(stream);

				stream.Close();

				// check if the file already exists. use SHA256, the others have collisions
				var existingFile = await dbContext.Files.FirstOrDefaultAsync(x => x.Sha256Hash == sha256Hash && x.Id != file.Id);

				if (existingFile != null)
				{
					// this file already exists unfortunately. try to merge it with the existing one, assuming it's not banned

					await dbContext.FileMappings
						.Where(x => x.FileId == fileId)
						.ExecuteUpdateAsync(x => x.SetProperty(y => y.FileId, existingFile.Id));

					dbContext.Remove(file);
					await dbContext.SaveChangesAsync();

					if (existingFile.FileBanned)
						return;

					file = existingFile;
					fileId = existingFile.Id;
				}

				if (!file.FileExists)
				{
					var imageFilename = CalculateFilename(ConsumerConfig.DownloadLocation, MediaType.FullImage, fileId, media.FileExtension);

					FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(imageFilename));
					
					if (!FileSystem.File.Exists(imageFilename))
						FileSystem.File.Move(imageTempFilename, imageFilename);
					
					await MediaInspector.DetermineMediaInfoAsync(imageFilename, file);

					if (file.Size == 0)
						file.Size = (uint)FileSystem.FileInfo.New(imageFilename).Length;

					file.FileExists = true;
					dbContext.Update(file);
				}
			}

			if (thumbTempFilename != null && !file.ThumbnailExists)
			{
				var thumbFilename = CalculateFilename(ConsumerConfig.DownloadLocation, MediaType.Thumbnail, fileId, media.ThumbnailExtension);

				FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(thumbFilename));

				if (!FileSystem.File.Exists(thumbFilename))
					FileSystem.File.Move(thumbTempFilename, thumbFilename);

				file.ThumbnailExists = true;
				dbContext.Update(file);
			}

			await dbContext.SaveChangesAsync();
		}

		/// <inheritdoc/>
		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			if (!deleted)
				return;

			ushort boardId = BoardIdMappings[GetTranslatedBoardName(board)];

			await using var dbContext = GetDBContext();

			var thread = await dbContext.Threads.FirstOrDefaultAsync(x => x.ThreadId == threadId && x.BoardId == boardId);

			if (thread == null)
			{
				// tried to mark a non-existent thread as deleted
				return;
			}

			thread.IsDeleted = true;
			dbContext.Update(thread);

			await dbContext.SaveChangesAsync();
		}

		/// <inheritdoc/>
		public async Task<IList<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
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
						.SelectMany(x =>
						dbContext.FileMappings.Where(y => y.BoardId == boardId && y.PostId == x.PostId).DefaultIfEmpty(),
						(post, mapping) => new { post, mapping });

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

		public async Task<ExistingThreadInfo> CheckExistingThread(ulong threadId, string board, MetadataMode metadataMode = MetadataMode.FullHashMetadata,
			bool excludeDeletedPosts = true)
		{
			ushort boardId = BoardIdMappings[GetTranslatedBoardName(board)];

			await using var dbContext = GetDBContext();

			if (metadataMode == MetadataMode.FullHashMetadata)
			{
				// Would be perfect but EF Core doesn't support translating GroupJoin

				//var posts = await dbContext.Posts
				//	.Where(x => x.BoardId == boardId && x.ThreadId == threadId && (!excludeDeletedPosts || !x.IsDeleted))
				//	.GroupJoin(dbContext.FileMappings,
				//		post => new { post.BoardId, post.PostId },
				//		mapping => new { mapping.BoardId, mapping.PostId },
				//		(post, mappings) => new {
				//			post.PostId,
				//			post.ContentHtml,
				//			post.ContentRaw,
				//			fileCount = mappings.Count(),
				//			deletedCount = mappings.Count(x => x.IsDeleted),
				//			spoilerCount = mappings.Count(x => x.IsSpoiler)
				//		}).ToArrayAsync();

				//var thread = await dbContext.Threads.AsNoTracking().FirstAsync(x => x.BoardId == boardId && x.ThreadId == threadId)

				var allPosts = await dbContext.Posts
					.AsNoTracking()
					.Where(x => x.BoardId == boardId && x.ThreadId == threadId && (!excludeDeletedPosts || !x.IsDeleted))
					.Select(x => new { x.PostId, x.ContentRaw, x.ContentHtml })
					.ToArrayAsync();

				var allPostNumbers = allPosts.Select(x => x.PostId).ToArray();

				var fileMappings = await dbContext.FileMappings
					.AsNoTracking()
					.Where(x => x.BoardId == boardId && allPostNumbers.Contains(x.PostId))
					.Select(x => new { x.PostId, x.IsSpoiler, x.IsDeleted })
					.ToArrayAsync();

				var hashes = new List<(ulong PostId, uint PostHash)>();

				foreach (var post in allPosts)
				{
					var postMappings = fileMappings.Where(x => x.PostId == post.PostId);

					var hash = CalculatePostHash(post.ContentHtml, post.ContentRaw,
						postMappings.Count(x => x.IsSpoiler), postMappings.Count(), postMappings.Count(x => x.IsDeleted));

					hashes.Add((post.PostId, hash));
				}

				// TODO: actually retrieve this thread data

				// new DateTimeOffset(threadInfo.LastModified, TimeSpan.Zero)
				return new ExistingThreadInfo(threadId, false, DateTimeOffset.MinValue, hashes);
			}

			if (metadataMode == MetadataMode.ThreadIdAndPostId)
			{
				var postIds = await dbContext.Posts.Where(x => x.BoardId == boardId && x.ThreadId == threadId)
					.Select(x => x.PostId)
					.ToArrayAsync();
				
				return new ExistingThreadInfo(threadId, false, DateTimeOffset.MinValue, postIds.Select(x => (x, (uint)0)).ToArray());
			}

			return new ExistingThreadInfo(threadId);
		}

		public static string CalculateFilename(string baseFolder, MediaType mediaType, uint fileId, string extension)
		{
			string mediaTypeString = mediaType switch
			{
				MediaType.FullImage => "image",
				MediaType.Thumbnail => "thumb",
				_                   => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null)
			};

			return Path.Combine(baseFolder, mediaTypeString, $"{fileId}.{extension.TrimStart('.').ToLower()}");
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