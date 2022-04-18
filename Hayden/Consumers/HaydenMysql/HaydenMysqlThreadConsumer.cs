using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenMysqlThreadConsumer : IThreadConsumer<YotsubaThread, YotsubaPost>
	{
		private HaydenMysqlConfig Config { get; }

		private MySqlConnectionPool ConnectionPool { get; }

		protected Dictionary<string, ushort> BoardIdMappings { get; } = new();

		/// <param name="config">The object to load configuration values from.</param>
		public HaydenMysqlThreadConsumer(HaydenMysqlConfig config)
		{
			Config = config;
			ConnectionPool = new MySqlConnectionPool(config.ConnectionString, config.SqlConnectionPoolSize);
		}

		public async Task InitializeAsync()
		{
			// TODO: logic to initialize unseen boards

			const string boardQuery = "SELECT ID, ShortName FROM boards;";

			await using var connection = await ConnectionPool.RentConnectionAsync();
			using var query = connection.Object.CreateQuery(boardQuery);

			await foreach (var boardRow in query.ExecuteRowsAsync())
			{
				BoardIdMappings[(string)boardRow[1]] = (ushort)boardRow[0];
			}
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo<YotsubaThread, YotsubaPost> threadUpdateInfo)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			string board = threadUpdateInfo.ThreadPointer.Board;
			ushort boardId = BoardIdMappings[board];

			{ // delete this block when not testing
				string threadDirectory = Path.Combine(Config.DownloadLocation, board, "thread");
				string threadFileName = Path.Combine(threadDirectory, $"{threadUpdateInfo.ThreadPointer.ThreadId}.json");

				Directory.CreateDirectory(threadDirectory);

				YotsubaFilesystemThreadConsumer.PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
			}

			void ProcessImages(YotsubaPost post)
			{
				if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
					return; // skip the DB check since we're not even bothering with images

				if (post.FileMd5 != null)
				{
					var md5Hash = Convert.FromBase64String(post.FileMd5);
					var base36Name = Utility.ConvertToBase(md5Hash);

					if (Config.FullImagesEnabled)
					{
						string imageDirectory = Path.Combine(Config.DownloadLocation, board, "image");
						Directory.CreateDirectory(imageDirectory);

						string fullImageFilename = Path.Combine(imageDirectory, base36Name + post.FileExtension);
						string fullImageUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}";

						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
					}

					if (Config.ThumbnailsEnabled)
					{
						string thumbnailDirectory = Path.Combine(Config.DownloadLocation, board, "thumb");
						Directory.CreateDirectory(thumbnailDirectory);

						string thumbFilename = Path.Combine(thumbnailDirectory, base36Name + ".jpg");
						string thumbUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg";

						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
					}
				}
			}

			if (threadUpdateInfo.IsNewThread)
			{
				await InsertThread(threadUpdateInfo.Thread, boardId);
			}

			foreach (var post in threadUpdateInfo.NewPosts)
			{
				ProcessImages(post);
			}

			await InsertPosts(threadUpdateInfo.NewPosts, boardId);

			foreach (var post in threadUpdateInfo.UpdatedPosts)
			{
				Program.Log($"[DB] Post /{board}/{post.PostNumber} has been modified", true);

				await UpdatePost(post, boardId, false);
			}

			foreach (var postNumber in threadUpdateInfo.DeletedPosts)
			{
				Program.Log($"[DB] Post /{board}/{postNumber} has been deleted", true);

				await UpdatePost(postNumber, boardId, true);
			}

			await UpdateThread(threadUpdateInfo.ThreadPointer.ThreadId, boardId, false, threadUpdateInfo.Thread.OriginalPost.Archived == true);

			return imageDownloads;
		}

		/// <inheritdoc/>
		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			ushort boardId = BoardIdMappings[board];

			await UpdateThread(threadId, boardId, deleted, !deleted);
		}
		
		#region Sql

		/// <summary>
		/// Inserts a collection of posts into their relevant table on the database.
		/// </summary>
		/// <param name="posts">The posts to insert.</param>
		/// <param name="boardId">The board of the posts.</param>
		public async Task InsertPosts(ICollection<YotsubaPost> posts, ushort boardId)
		{
			const string postInsertQuerySql = "INSERT INTO posts"
											+ "  (boardid, postid, threadid, contenthtml, author, tripcode, email, datetime, isdeleted)"
											+ "  VALUES (@boardid, @postid, @threadid, @contenthtml, @author, @tripcode, NULL, @datetime, 0);";

			const string fileInsertQuerySql = "INSERT INTO files"
											+ " (BoardId, Md5Hash, Sha1Hash, Sha256Hash, Extension, ImageWidth, ImageHeight, Size)"
											+ " VALUES (@boardid, @md5hash, @sha1hash, @sha256hash, @extension, @imagewidth, @imageheight, @filesize);"
											+ " SELECT LAST_INSERT_ID();";

			const string mappingInsertQuerySql = "INSERT INTO file_mappings"
												+ " (BoardId, PostId, FileId, `Index`, Filename, IsSpoiler, IsDeleted)"
												+ " VALUES (@boardid, @postid, @fileid, @index, @filename, @isspoiler, @isdeleted);";
			
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			Dictionary<string, uint> md5FileDictionary = null;

			if (posts.Any(x => x.FileMd5 != null))
			{
				var md5List = posts.Where(x => x.FileMd5 != null).Select(x => $"0x{Utility.ConvertToHex(Convert.FromBase64String(x.FileMd5))}");

				string query = $@"SELECT md5hash, CAST(id AS UNSIGNED)
								FROM files
								WHERE md5hash IN ({string.Join(',', md5List)});";

				var threadTable = await rentedConnection.Object.CreateQuery(query).ExecuteTableAsync();

				md5FileDictionary = new Dictionary<string, uint>();

				foreach (DataRow row in threadTable.Rows)
				{
					string md5Base64 = Convert.ToBase64String((byte[])row[0]);
					var fileId = (uint)(ulong)row[1];
					md5FileDictionary[md5Base64] = fileId;
				}
			}
			
			using var postInsertQuery = rentedConnection.Object.CreateQuery(postInsertQuerySql, true);
			using var fileInsertQuery = rentedConnection.Object.CreateQuery(fileInsertQuerySql, true);
			using var mappingInsertQuery = rentedConnection.Object.CreateQuery(mappingInsertQuerySql, true);

			foreach (var post in posts)
			{
				await postInsertQuery
					  .SetParam("@boardid", boardId)
					  .SetParam("@postid", post.PostNumber)
					  .SetParam("@threadid", post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber)
					  .SetParam("@contenthtml", post.Comment)
					  .SetParam("@author", post.Name == "Anonymous" ? null : post.Name)
					  .SetParam("@tripcode", post.Trip)
					  .SetParam("@datetime", Utility.ConvertGMTTimestamp(post.UnixTimestamp).UtcDateTime)
					  .ExecuteNonQueryAsync();

				if (post.FileMd5 != null)
				{
					if (!md5FileDictionary.TryGetValue(post.FileMd5, out var fileId))
					{
						var md5Hash = post.FileMd5 == null ? null : Convert.FromBase64String(post.FileMd5);

						// TODO: these additional fields need to be calculated at some point

						fileId = (uint)await fileInsertQuery
							.SetParam("@boardid", boardId)
							.SetParam("@md5hash", md5Hash)
							.SetParam("@sha1hash", md5Hash)
							.SetParam("@sha256hash", md5Hash)
							.SetParam("@extension", post.FileExtension.Substring(1))
							.SetParam("@imagewidth", null)
							.SetParam("@imageheight", null)
							.SetParam("@filesize", 0)
							.ExecuteScalarAsync<ulong>();
					}

					await mappingInsertQuery
						.SetParam("@boardid", boardId)
						.SetParam("@postid", post.PostNumber)
						.SetParam("@fileid", fileId)
						.SetParam("@index", 0)
						.SetParam("@filename", post.OriginalFilename)
						.SetParam("@isspoiler", post.SpoilerImage == true ? 1 : 0)
						.SetParam("@isdeleted", post.FileDeleted == true ? 1 : 0)
						.ExecuteNonQueryAsync();
				}
			}
		}

		/// <summary>
		/// Inserts a new thread into the 'threads' metadata table.
		/// </summary>
		/// <param name="thread">The thread to insert.</param>
		/// <param name="boardId">The board of the thread.</param>
		public async Task InsertThread(YotsubaThread thread, ushort boardId)
		{
			string insertQuerySql = "INSERT INTO threads"
									+ "         (boardid, threadid, title, lastmodified, isarchived, isdeleted)"
									+ "  VALUES (@boardid, @threadid, @title, '1337-01-01', 0, 0);";

			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			using var chainedQuery = rentedConnection.Object.CreateQuery(insertQuerySql, true);

			await chainedQuery
				.SetParam("@boardid", boardId)
				.SetParam("@threadid", thread.OriginalPost.PostNumber)
				.SetParam("@title", string.IsNullOrWhiteSpace(thread.OriginalPost.Subject) ? null : thread.OriginalPost.Subject)
				.ExecuteNonQueryAsync();
		}
		
		/// <summary>
		/// Updates an existing post in the database.
		/// </summary>
		/// <param name="post">The post to update.</param>
		/// <param name="boardId">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if it was not.</param>
		public async Task UpdatePost(YotsubaPost post, ushort boardId, bool deleted)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string sql = $"UPDATE posts SET "
						 + "ContentHtml = @html, "
						 + "IsDeleted = @deleted "
						 + "WHERE PostId = @postid "
						 + "AND boardid = @boardid; "

						 + "UPDATE file_mappings fm "
						 + "INNER JOIN files f ON fm.FileId = f.Id "
						 + "SET fm.IsDeleted = @imagedeleted "
						 + "WHERE fm.BoardId = @boardid AND fm.PostId = @postid AND f.Md5Hash = @md5hash;";

			var md5Hash = post.FileMd5 == null ? null : Convert.FromBase64String(post.FileMd5);

			await rentedConnection.Object.CreateQuery(sql)
								  .SetParam("@html", post.Comment)
								  .SetParam("@deleted", deleted ? 1 : 0)
								  .SetParam("@imagedeleted", post.FileDeleted == true ? 1 : 0)
								  .SetParam("@postid", post.PostNumber)
								  .SetParam("@boardid", boardId)
								  .SetParam("@md5hash", md5Hash)
								  .ExecuteNonQueryAsync();
		}

		/// <summary>
		/// Updates an existing post in the database, but only using the post ID (i.e. for use when a post gets deleted).
		/// </summary>
		/// <param name="postId">The ID of the post to update.</param>
		/// <param name="boardId">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if it was not.</param>
		public async Task UpdatePost(ulong postId, ushort boardId, bool deleted)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string sql = $"UPDATE posts SET "
						 + "IsDeleted = @deleted "
						 + "WHERE PostId = @postid "
						 + "AND boardid = @boardid";

			await rentedConnection.Object.CreateQuery(sql)
								  .SetParam("@deleted", deleted ? 1 : 0)
								  .SetParam("@boardid", boardId)
								  .SetParam("@postid", postId)
								  .ExecuteNonQueryAsync();
		}

		/// <summary>
		/// Sets a post tracking status in the database.
		/// </summary>
		/// <param name="threadId">The number of the post.</param>
		/// <param name="boardId">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if not.</param>
		public async Task UpdateThread(ulong threadId, ushort boardId, bool deleted, bool archived, DateTimeOffset? lastModified = null)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			await rentedConnection.Object.CreateQuery("UPDATE threads SET " +
			                                          "isdeleted = @deleted, " +
			                                          "isarchived = @archived, " +
													  "lastmodified = COALESCE(@lastmodified, (SELECT MAX(DateTime) FROM posts WHERE boardid = @boardid and threadid = @threadid GROUP BY threadid), '1000-01-01') " +
			                                          "WHERE threadid = @threadid AND boardid = @boardid")
								  .SetParam("@boardid", boardId)
								  .SetParam("@threadid", threadId)
								  .SetParam("@deleted", deleted ? 1 : 0)
								  .SetParam("@archived", archived ? 1 : 0)
								  .SetParam("@lastmodified", lastModified?.UtcDateTime)
								  .ExecuteNonQueryAsync();
		}

		/// <inheritdoc/>
		public async Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getMetadata = true)
		{
			int archivedInt = archivedOnly ? 1 : 0;

			ushort boardId = BoardIdMappings[board];

			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string query;

			if (getMetadata)
			{
				query = $@"SELECT ThreadId, LastModified
						   FROM threads
						   WHERE (IsArchived = {archivedInt} OR {archivedInt} = 0)
							 AND BoardId = {boardId}
							 AND ThreadId IN ({string.Join(',', threadIdsToCheck)})
						   GROUP BY ThreadId";

				Program.Log(query, true);
				
				var threadTable = await rentedConnection.Object.CreateQuery(query).ExecuteTableAsync();
				
				var items = new List<ExistingThreadInfo>();

				foreach (DataRow threadRow in threadTable.Rows)
				{
					var hashes = new List<(ulong PostId, uint PostHash)>();

					var rows = rentedConnection.Object
						.CreateQuery("SELECT p.PostId, p.ContentHtml AS `ContentHtml`, IFNULL(fm.IsSpoiler, 0) AS `IsSpoiler`, IFNULL(fm.IsDeleted, 0) AS `IsImageDeleted`, fm.Filename AS `MediaFilename` " +
						             "FROM posts p " +
						             "LEFT JOIN file_mappings fm ON p.BoardId = fm.BoardId AND p.PostId = fm.PostId AND fm.Index = 0 " +
						             "WHERE p.threadid = @threadid AND p.boardid = @boardid AND p.IsDeleted = 0")
						.SetParam("@threadid", (ulong)threadRow[0])
						.SetParam("@boardid", boardId)
						.ExecuteRowsAsync();

					await foreach (var postRow in rows)
					{
						string filenameNoExt = postRow.GetValue<string>("MediaFilename");

						var hash = CalculatePostHash(postRow.GetValue<string>("ContentHtml"), postRow.GetValue<bool>("IsSpoiler"),
							postRow.GetValue<bool>("IsImageDeleted"), filenameNoExt, null, null, null, null, null, null, null);

						hashes.Add(((ulong)postRow[0], hash));
					}

					items.Add(new ExistingThreadInfo((ulong)threadRow[0], (DateTime)threadRow[1], hashes));
				}

				return items;
			}
			else
			{
				query = $@"SELECT ThreadId
						   FROM threads
						   WHERE (threads.IsArchived = {archivedInt} OR {archivedInt} = 0)
							 AND BoardId = {boardId}
							 AND ThreadId IN ({string.Join(',', threadIdsToCheck)})";

				var chainedQuery = rentedConnection.Object.CreateQuery(query);

				var items = new List<ExistingThreadInfo>();

				await foreach (var row in chainedQuery.ExecuteRowsAsync())
				{
					items.Add(new ExistingThreadInfo((ulong)row[0]));
				}

				return items;
			}
		}

		#endregion

		public static uint CalculatePostHash(string postHtml, bool? spoilerImage, bool? fileDeleted, string originalFilenameNoExt,
			bool? archived, bool? closed, bool? bumpLimit, bool? imageLimit, uint? replyCount, ushort? imageCount, int? uniqueIpAddresses)
		{
			// Null bool? values should evaluate to false everywhere
			static int EvaluateNullableBool(bool? value)
			{
				return value.HasValue
					? (value.Value ? 1 : 2)
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
		public void Dispose()
		{
			ConnectionPool.Dispose();
		}
	}
}