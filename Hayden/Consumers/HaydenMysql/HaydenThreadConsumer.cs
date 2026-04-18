using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.MediaInfo;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
			var config = SourceConfig.Boards.FirstOrDefault(x => x.Board == boardName);

			if (config == null || string.IsNullOrWhiteSpace(config.TranslatedBoardName))
				return boardName;

			return config.TranslatedBoardName;
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
				var translatedBoardName = boardRule.TranslatedBoardName ?? boardRule.Board;

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
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo, bool downloadFullImages, bool downloadThumbnails)
		{
			await using var dbContext = GetDBContext();

			// A lot of this will look a bit verbose and unclear, but it's to do with how EF Core handles entity tracking
			// Basically, to maximise performance and minimise wasted cycles on change tracking, I follow a lot of practices here:
			// https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics
			// Which is very far away from how you'd expect normal EF Core code to be written. However this gives a +25% performance boost

			try
			{
				dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
				dbContext.ChangeTracker.Clear();

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

						if (downloadFullImages && media.FileUrl != null && !dbFile.FileExists)
							imageUrl = new Uri(media.FileUrl);

						if (downloadThumbnails && media.ThumbnailUrl != null && !dbFile.ThumbnailExists)
							thumbUrl = new Uri(media.ThumbnailUrl);

						if (imageUrl != null || thumbUrl != null)
						{
							imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
							{
								["fileId"] = dbFile.Id,
								["media"] = media
							}));

							//Logger.Information("queued image");
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
								|| Sha1Hashes.Contains(file.Sha1Hash)
								|| Md5Hashes.Contains(file.Md5Hash))
							.ToArrayAsync();
					}
					else
					{
						allMatchingFiles = Array.Empty<DBFile>();
					}

					if (ConsumerConfig.ForceRescanImages)
					{
						var existingPosts = threadUpdateInfo.Thread.Posts.ExceptBy(
							threadUpdateInfo.NewPosts.Select(x => x.PostNumber), x => x.PostNumber);

						var postNumbers = existingPosts.Select(x => x.PostNumber).ToArray();

						var mappings = (await dbContext.FileMappings
							.AsNoTracking()
							.Where(x => x.BoardId == boardId && postNumbers.Contains(x.PostId))
							.GroupJoin(dbContext.Files, mapping => mapping.FileId, file => file.Id, (mapping, file) => new { mapping, file })
							.SelectMany(x => x.file.DefaultIfEmpty(), (x, file) => new { x.mapping, file })
							.ToArrayAsync())
							.ToLookup(x => x.mapping.PostId);

						foreach (var post in existingPosts)
						{
							var recordedCount = mappings[post.PostNumber].Count();
							if (recordedCount != post.Media.Length)
							{
								if (recordedCount == 0)
								{
									foreach (var media in post.Media)
									{
										var existingFile = await dbContext.Files
											.AsNoTracking()
											.Where(file =>
												(media.Sha256Hash != null && media.Sha256Hash == file.Sha256Hash)
												|| (media.Sha1Hash != null && media.Sha1Hash == file.Sha1Hash)
												|| (media.Md5Hash != null && media.Md5Hash == file.Md5Hash))
											.FirstOrDefaultAsync();

										if (existingFile == null)
										{
											existingFile = new DBFile
											{
												Sha256Hash = media.Sha256Hash,
												Sha1Hash = media.Sha1Hash,
												Md5Hash = media.Md5Hash,
												FileExists = false,
												ThumbnailExists = false,
												ImageHeight = media.ImageHeight.HasValue ? (ushort)media.ImageHeight.Value : null,
												ImageWidth = media.ImageWidth.HasValue ? (ushort)media.ImageWidth.Value : null,
												Size = media.FileSize ?? 0,
												Extension = media.FileExtension.TrimStart('.'),
												ThumbnailExtension = media.ThumbnailExtension?.TrimStart('.')
											};

											dbContext.Add(existingFile);
											await dbContext.SaveChangesAsync();
										}
										else
										{
											var entry = dbContext.Files.Local.FindEntry(existingFile.Id);
											if (entry != null)
												existingFile = entry.Entity;

											if (existingFile.ImageHeight == null)
											{
												existingFile.ImageHeight = media.ImageHeight.HasValue
													? (ushort)media.ImageHeight.Value
													: null;
												existingFile.ImageWidth = media.ImageWidth.HasValue
													? (ushort)media.ImageWidth.Value
													: null;

												if (entry == null)
													dbContext.Update(existingFile);
												else
													entry.State = EntityState.Modified;
											}
										}

										var fileMapping = new DBFileMapping
										{
											BoardId = boardId,
											PostId = post.PostNumber,
											FileId = existingFile.Id,
											Filename = media.Filename ?? "",
											TimestampedFilename = media.TimestampedFilename ?? (media.FileUrl != null ? FileSystem.Path.GetFileNameWithoutExtension(media.FileUrl) : null),
											Index = media.Index,
											IsDeleted = media.IsDeleted,
											IsSpoiler = media.IsSpoiler,
											AdditionalMetadata = SerializeAdditionalMetadata(media.AdditionalMetadata)
										};

										dbContext.Add(fileMapping);
									}

									continue;
								}

								Logger.Warning($"Post media count mismatch; incoming post has {post.Media.Length} files but we've only recorded {recordedCount}. Skipping checking post for missing images");
								continue;
							}

							foreach (var mapping in mappings[post.PostNumber])
							{
								var media = post.Media[mapping.mapping.Index];
								var file = mapping.file;

								if (file == null)
								{
									file = await dbContext.Files
										.FirstOrDefaultAsync(x =>
											(media.Md5Hash != null && !ConsumerConfig.IgnoreMd5Hash && x.Md5Hash == media.Md5Hash)
											|| (media.Sha1Hash != null && !ConsumerConfig.IgnoreSha1Hash && x.Sha1Hash == media.Sha1Hash)
											|| (media.Sha256Hash != null && x.Sha256Hash == media.Sha256Hash)
										);

									if (file == null)
									{
										file = new DBFile
										{
											Sha256Hash = media.Sha256Hash,
											Sha1Hash = media.Sha1Hash,
											Md5Hash = media.Md5Hash,
											FileExists = false,
											ThumbnailExists = false,
											ImageHeight = media.ImageHeight.HasValue ? (ushort)media.ImageHeight.Value : null,
											ImageWidth = media.ImageWidth.HasValue ? (ushort)media.ImageWidth.Value : null,
											Size = media.FileSize ?? 0,
											Extension = media.FileExtension?.TrimStart('.') ?? "",
											ThumbnailExtension = media.ThumbnailExtension?.TrimStart('.')
										};

										dbContext.Add(file);
										await dbContext.SaveChangesAsync();
									}

									mapping.mapping.FileId = file.Id;
									dbContext.Update(mapping.mapping);
								}

								if (mapping.file == null || !mapping.file.FileExists || !mapping.file.ThumbnailExists)
								{
									QueueDownload(media, file);
								}

								if (mapping.mapping.TimestampedFilename != media.TimestampedFilename)
								{
									mapping.mapping.TimestampedFilename = media.TimestampedFilename;
									dbContext.Update(mapping.mapping);
								}
							}
						}
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
								TimestampedFilename = media.TimestampedFilename ?? (media.FileUrl != null ? FileSystem.Path.GetFileNameWithoutExtension(media.FileUrl) : null),
								Index = media.Index,
								IsDeleted = media.IsDeleted,
								IsSpoiler = media.IsSpoiler,
								AdditionalMetadata = SerializeAdditionalMetadata(media.AdditionalMetadata)
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

							// todo: track post content diffs

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
								var existingFile = postMappings.FirstOrDefault(x => x.Index == media.Index);

								if (existingFile != null && media.IsDeleted && existingFile.IsDeleted != media.IsDeleted)
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
						TimeDeleted = threadUpdateInfo.Thread.DeletedTime?.UtcDateTime
						            ?? (threadUpdateInfo.Thread.Posts.FirstOrDefault(x => x.PostNumber == threadUpdateInfo.ThreadPointer.ThreadId)?.TimeDeleted?.UtcDateTime),
						TimeArchived = threadUpdateInfo.Thread.ArchivedTime?.UtcDateTime,
						LastModified = threadUpdateInfo.Thread.Posts.DefaultIfEmpty().Max(x => x.TimePosted).UtcDateTime,
						Title = threadUpdateInfo.Thread.Title.TrimAndNullify(),
						AdditionalMetadata = SerializeAdditionalMetadata(threadUpdateInfo.Thread.AdditionalMetadata),
						PostCount = (uint)threadUpdateInfo.NewPosts.Count,
						ImageCount = (uint)threadUpdateInfo.NewPosts.Sum(x => x.Media?.Length ?? 0),
					};

					dbContext.Add(dbThread);
				}

				if (threadUpdateInfo.IsNewThread)
				{
					CreateThread();
				}
				else if (threadUpdateInfo.NewPosts.Count > 0
					|| threadUpdateInfo.Thread.ArchivedTime != null
					|| threadUpdateInfo.Thread.Posts[0].TimeDeleted != null)
				{
					var dbThread = await dbContext.Threads.FirstOrDefaultAsync(x =>
						x.BoardId == boardId && x.ThreadId == threadUpdateInfo.ThreadPointer.ThreadId);

					if (dbThread != null)
					{
						dbThread.TimeDeleted = threadUpdateInfo.Thread.DeletedTime?.UtcDateTime;
						dbThread.TimeArchived = threadUpdateInfo.Thread.ArchivedTime?.UtcDateTime;

						dbThread.PostCount += (uint)threadUpdateInfo.NewPosts.Count;
						dbThread.ImageCount += (uint)threadUpdateInfo.NewPosts.Sum(x => x.Media?.Length ?? 0);

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

				if (!threadUpdateInfo.IsNewThread && threadUpdateInfo.NewPosts.Any(x => x.TimeDeleted != null))
				{
					var checkedPostIds = threadUpdateInfo.NewPosts.Where(x => x.TimeDeleted != null)
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

					byte? sourceValue = null;

					if (post.AdditionalMetadata?.Source != null)
					{
						if (SourceMappings.TryGetValue(post.AdditionalMetadata.Source, out var actualSourceValue))
							sourceValue = actualSourceValue;
						else
						{
							lock (SourceMappings)
							{
								if (!SourceMappings.TryGetValue(post.AdditionalMetadata.Source, out actualSourceValue))
								{
									actualSourceValue = (byte)(SourceMappings.Max(x => x.Value, 0) + 1);
									
									SourceMappings[post.AdditionalMetadata.Source] = actualSourceValue;

									var newDbSource = new DBSource { Id = actualSourceValue, Name = post.AdditionalMetadata.Source };
									dbContext.Add(newDbSource);
								}

								sourceValue = actualSourceValue;
							}
						}

						post.AdditionalMetadata.Source = null;
					}

					dbContext.Add(new DBPost
					{
						BoardId = boardId,
						PostId = post.PostNumber,
						ThreadId = threadUpdateInfo.ThreadPointer.ThreadId,
						ContentHtml = post.ContentRendered.TrimAndNullify(),
						ContentRaw = post.ContentRaw.TrimAndNullify(),
						ContentType = post.ContentType,
						TimeDeleted = post.TimeDeleted?.UtcDateTime,
						Author = post.Author == "Anonymous" ? null : post.Author.TrimAndNullify(),
						Tripcode = post.Tripcode.TrimAndNullify(),
						Email = post.Email.TrimAndNullify(),
						DateTime = post.TimePosted.UtcDateTime,
						Source = sourceValue,
						AdditionalMetadata = SerializeAdditionalMetadata(post.AdditionalMetadata)
					});
				}

				if (ConsumerConfig.ConsolidationMode == ConsolidationMode.Authoritative)
					foreach (var post in threadUpdateInfo.UpdatedPosts)
					{
						Logger.Information("Post /{board}/{postNumber} has been modified", board, post.PostNumber);

						var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == post.PostNumber);

						//var dbPostMappings = await dbContext.FileMappings
						//	.AsNoTracking()
						//	.Where(x => x.BoardId == boardId && x.PostId == post.PostNumber).ToArrayAsync();
					
						if ((dbPost.ContentRaw != null && post.ContentRaw != dbPost.ContentRaw) || (dbPost.ContentRaw == null && post.ContentRendered != dbPost.ContentHtml))
						{
							// this needs to be made more efficient
							// this also doesn't cooperate well with deadlinks (why the fuck is that passed through the api html render?)

							// TODO: check if the content has actually changed by removing deadlinks classes

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

						dbPost.TimeDeleted = null;
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

						dbPost.TimeDeleted = DateTime.UtcNow;
						dbContext.Update(dbPost);
					}
				
				//dbContext.ChangeTracker.DetectChanges();
				//await dbContext.SaveChangesAsync();
				//dbContext.ChangeTracker.Clear();
			
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

		public async Task<bool> NeedToDownloadImage(QueuedImageDownload queuedImageDownload)
		{
			if (!queuedImageDownload.TryGetProperty("fileId", out uint fileId)
				|| !queuedImageDownload.TryGetProperty("media", out Media media))
			{
				Logger.Error("Queued image download did not have the required properties. URL: {url}", queuedImageDownload.FullImageUri);
				return false;
			}

			await using var dbContext = GetDBContext();

			var file = dbContext.Files.FirstOrDefault(x => x.Id == fileId);

			if (file == null)
			{
				Logger.Error("Could not find relevant file in database for download. URL: {url}", queuedImageDownload.FullImageUri);
				return false;
			}

			return !file.FileExists || !file.ThumbnailExists;
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

					// TODO: update md5/sha1 with actual file hash, as it might not match

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
				else
				{
					file.Md5Hash = md5Hash;
					file.Sha1Hash = sha1Hash;
					file.Sha256Hash = sha256Hash;
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
		public async Task ThreadUntracked(ulong threadId, string board, DateTimeOffset? timeDeleted, DateTimeOffset? timeArchived)
		{
			if (timeDeleted == null && timeArchived == null)
				return;

			ushort boardId = BoardIdMappings[GetTranslatedBoardName(board)];

			await using var dbContext = GetDBContext();

			var thread = await dbContext.Threads.FirstOrDefaultAsync(x => x.ThreadId == threadId && x.BoardId == boardId);

			if (thread == null)
			{
				// tried to mark a non-existent thread as deleted
				return;
			}

			thread.TimeDeleted = timeDeleted?.UtcDateTime;

			if (thread.TimeArchived == null && timeArchived == DateTimeOffset.MinValue)
				thread.TimeArchived = DateTime.UtcNow;
			else
				thread.TimeArchived = timeArchived?.UtcDateTime;
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
				query = query.Where(x => x.TimeArchived != null);

			var items = new List<ExistingThreadInfo>();

			if (metadataMode == MetadataMode.FullHashMetadata)
			{
				var threadInfos = await query.Select(x => new { x.ThreadId, x.LastModified, x.TimeArchived }).ToDictionaryAsync(x => x.ThreadId);
				
				var postQuery =
					dbContext.Posts.Where(x => x.BoardId == boardId && threadInfos.Keys.Contains(x.ThreadId) && (!excludeDeletedPosts || x.TimeDeleted == null))
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

					items.Add(new ExistingThreadInfo(threadInfo.ThreadId, threadInfo.TimeArchived != null, new DateTimeOffset(threadInfo.LastModified, TimeSpan.Zero), hashes));
				}
			}
			else if (metadataMode == MetadataMode.ThreadIdAndPostId)
			{
				var postIds = await dbContext.Posts.Where(y => y.BoardId == boardId && query.Select(x => x.ThreadId).Contains(y.ThreadId))
					.OrderBy(x => x.ThreadId)
					.Select(x => new { x.ThreadId, x.PostId, x.DateTime })
					.ToArrayAsync();
				
				foreach (var group in postIds.EfficientGroupBy(x => x.ThreadId, x => x))
				{
					items.Add(new ExistingThreadInfo(group.Key, false, group.Values.Max(x => x.DateTime), group.Values.Select(x => (x.PostId, (uint)0)).ToArray()));
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
					.Where(x => x.BoardId == boardId && x.ThreadId == threadId && (!excludeDeletedPosts || x.TimeDeleted == null))
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

			return Path.Combine(baseFolder, mediaTypeString, $"{fileId}.{extension?.TrimStart('.')?.ToLower() ?? "null"}");
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
				post.Media.Count(x => x.IsSpoiler),
				post.Media.Length,
				post.Media.Count(x => x.IsDeleted));

		/// <summary>
		/// Disposes the object.
		/// </summary>
		public void Dispose() { }
	}
}