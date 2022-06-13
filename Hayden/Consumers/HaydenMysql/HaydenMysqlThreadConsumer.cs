using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenMysqlThreadConsumer : IThreadConsumer<YotsubaThread, YotsubaPost>
	{
		private HaydenMysqlConfig Config { get; }

		protected Dictionary<string, ushort> BoardIdMappings { get; } = new();

		/// <param name="config">The object to load configuration values from.</param>
		public HaydenMysqlThreadConsumer(HaydenMysqlConfig config)
		{
			Config = config;
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

		private HaydenDbContext GetDBContext()
		{
			var contextBuilder = new DbContextOptionsBuilder();

			contextBuilder.UseMySql(Config.ConnectionString, ServerVersion.AutoDetect(Config.ConnectionString), x =>
			{
				x.EnableIndexOptimizedBooleanColumns();
			});

			return new HaydenDbContext(contextBuilder.Options);
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo<YotsubaThread, YotsubaPost> threadUpdateInfo)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			await using var dbContext = GetDBContext();

			string board = threadUpdateInfo.ThreadPointer.Board;
			ushort boardId = BoardIdMappings[board];

			{ // delete this block when not testing
				string threadDirectory = Path.Combine(Config.DownloadLocation, board, "thread");
				string threadFileName = Path.Combine(threadDirectory, $"{threadUpdateInfo.ThreadPointer.ThreadId}.json");

				Directory.CreateDirectory(threadDirectory);

				YotsubaFilesystemThreadConsumer.PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
			}

			async Task ProcessImages(YotsubaPost post)
			{
				if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
					return; // skip the DB check since we're not even bothering with images

				if (post.FileMd5 != null)
				{
					if (!Config.DoNotUseMd5HashForComparison)
					{
						var existingFile = await dbContext.Files.FirstOrDefaultAsync(x =>
							x.Md5Hash == Convert.FromBase64String(post.FileMd5)
							&& x.Size == post.FileSize.Value
							&& x.BoardId == boardId);

						if (existingFile != null)
						{
							// We know we have the file. Just attach it

							var fileMapping = new DBFileMapping
							{
								BoardId = boardId,
								PostId = post.PostNumber,
								FileId = existingFile.Id,
								Filename = Path.GetFileNameWithoutExtension(post.OriginalFilename),
								Index = 0,
								IsDeleted = post.FileDeleted.GetValueOrDefault(),
								IsSpoiler = post.SpoilerImage.GetValueOrDefault()
							};

							dbContext.Add(fileMapping);

							return;
						}
					}

					Uri imageUrl = null;
					Uri thumbUrl = null;

					if (Config.FullImagesEnabled)
					{
						string imageDirectory = Path.Combine(Config.DownloadLocation, board, "image");
						Directory.CreateDirectory(imageDirectory);

						imageUrl = new Uri($"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}");
					}

					if (Config.ThumbnailsEnabled)
					{
						string thumbnailDirectory = Path.Combine(Config.DownloadLocation, board, "thumb");
						Directory.CreateDirectory(thumbnailDirectory);

						thumbUrl = new Uri($"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg");
					}
					
					imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
					{
						["board"] = board,
						["boardId"] = boardId,
						["post"] = post
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
					Title = threadUpdateInfo.Thread.OriginalPost.Subject.TrimAndNullify()
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
					ThreadId = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber,
					ContentHtml = post.Comment,
					Author = post.Name == "Anonymous" ? null : post.Name,
					Tripcode = post.Trip,
					DateTime = Utility.ConvertGMTTimestamp(post.UnixTimestamp).UtcDateTime
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

				dbPost.ContentHtml = post.Comment;
				dbPost.IsDeleted = false;

				foreach (var dbPostMapping in dbPostMappings)
				{
					dbPostMapping.IsDeleted = post.FileDeleted ?? false;
				}
			}

			foreach (var postNumber in threadUpdateInfo.DeletedPosts)
			{
				Program.Log($"[DB] Post /{board}/{postNumber} has been deleted", true);

				var dbPost = await dbContext.Posts.FirstAsync(x => x.BoardId == boardId && x.PostId == postNumber);

				dbPost.IsDeleted = true;
			}

			await dbContext.SaveChangesAsync();

			await UpdateThread(board, threadUpdateInfo.ThreadPointer.ThreadId, false, threadUpdateInfo.Thread.OriginalPost.Archived == true);
			
			return imageDownloads;
		}

		/// <inheritdoc/>
		public async Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, Memory<byte>? imageData, Memory<byte>? thumbnailData)
		{
			if (!queuedImageDownload.TryGetProperty("board", out string board)
				|| !queuedImageDownload.TryGetProperty("boardId", out ushort boardId)
				|| !queuedImageDownload.TryGetProperty("post", out YotsubaPost post))
			{
				throw new InvalidOperationException("Queued image download did not have the required properties");
			}

			if (imageData == null)
				throw new InvalidOperationException("Full image required for hash calculation");

			using var stream = new MemorySpanStream(imageData.Value, true);

			var (md5Hash, sha1Hash, sha256Hash) = Utility.CalculateHashes(stream);
			
			var imageFilename = Common.CalculateFilename(Config.DownloadLocation, board, Common.MediaType.Image, sha256Hash, post.FileExtension);

			await Utility.WriteAllBytesAsync(imageFilename + ".tmp", imageData.Value);
			File.Move(imageFilename + ".tmp", imageFilename, true);

			if (thumbnailData.HasValue)
			{
				var thumbFilename = Common.CalculateFilename(Config.DownloadLocation, board, Common.MediaType.Thumbnail, sha256Hash, "jpg");
				
				await Utility.WriteAllBytesAsync(thumbFilename + ".tmp", thumbnailData.Value);
				File.Move(thumbFilename + ".tmp", thumbFilename, true);
			}

			await using var dbContext = GetDBContext();

			uint fileId;

			var existingFile = await dbContext.Files
				.Where(x => x.Sha256Hash == sha256Hash
				            && x.BoardId == boardId)
				.Select(x => new { x.Id })
				.FirstOrDefaultAsync();

			if (existingFile == null)
			{
				var dbFile = new DBFile
				{
					BoardId = (ushort)boardId,
					Extension = post.FileExtension.TrimStart('.'),
					FileExists = true,
					FileBanned = false,
					ThumbnailExtension = thumbnailData.HasValue ? "jpg" : null,
					Md5Hash = md5Hash,
					Sha1Hash = sha1Hash,
					Sha256Hash = sha256Hash,
					Size = (uint)imageData.Value.Length
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
			
			var fileMapping = new DBFileMapping
			{
				BoardId = (ushort)boardId,
				PostId = post.PostNumber,
				FileId = fileId,
				Filename = Path.GetFileNameWithoutExtension(post.OriginalFilename),
				Index = 0,
				IsDeleted = post.FileDeleted.GetValueOrDefault(),
				IsSpoiler = post.SpoilerImage.GetValueOrDefault()
			};

			dbContext.Add(fileMapping);
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
						var fileMapping = postGroup.FirstOrDefault();

						var hash = CalculatePostHash(postGroup.Key.ContentHtml, fileMapping?.IsSpoiler,
							fileMapping?.IsDeleted, fileMapping?.Filename, null, null, null, null, null, null, null);

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

		public static uint CalculatePostHash(string postHtml, bool? spoilerImage, bool? fileDeleted, string originalFilenameNoExt,
			bool? archived, bool? closed, bool? bumpLimit, bool? imageLimit, uint? replyCount, ushort? imageCount, int? uniqueIpAddresses)
		{
			// Null bool? values should evaluate to false everywhere
			static int EvaluateNullableBool(bool? value)
			{
				return value.HasValue && value.Value
					? 1
					: 2;
			}

			// The HTML content of a post can change due to public warnings and bans.
			uint hashCode = Utility.FNV1aHash32(postHtml);

			// Attached files can be removed, and have their spoiler status changed
			Utility.FNV1aHash32(EvaluateNullableBool(spoilerImage), ref hashCode);
			Utility.FNV1aHash32(EvaluateNullableBool(fileDeleted), ref hashCode);
			Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

			// The OP of a thread can have numerous properties change.
			// As such, these properties are only considered mutable for OPs (because that's the only place they can exist) and immutable for replies.
			Utility.FNV1aHash32(EvaluateNullableBool(archived), ref hashCode);
			Utility.FNV1aHash32(EvaluateNullableBool(closed), ref hashCode);
			Utility.FNV1aHash32(EvaluateNullableBool(bumpLimit), ref hashCode);
			Utility.FNV1aHash32(EvaluateNullableBool(imageLimit), ref hashCode);
			Utility.FNV1aHash32((int?)replyCount ?? -1, ref hashCode);
			Utility.FNV1aHash32(imageCount ?? -1, ref hashCode);
			Utility.FNV1aHash32(uniqueIpAddresses ?? -1, ref hashCode);

			return hashCode;
		}

		/// <inheritdoc />
		public uint CalculateHash(YotsubaPost post)
			=> CalculatePostHash(post.Comment, post.SpoilerImage, post.FileDeleted, post.OriginalFilename,
				post.Archived, post.Closed, post.BumpLimit, post.ImageLimit, post.TotalReplies, post.TotalImages, post.UniqueIps);

		/// <summary>
		/// Disposes the object.
		/// </summary>
		public void Dispose() { }
	}
}