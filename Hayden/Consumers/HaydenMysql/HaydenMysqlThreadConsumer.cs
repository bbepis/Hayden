using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	/// <summary>
	/// A thread consumer for the HaydenMysql MySQL backend.
	/// </summary>
	public class HaydenMysqlThreadConsumer : IThreadConsumer
	{
		private HaydenMysqlConfig Config { get; }

		private MySqlConnectionPool ConnectionPool { get; }

		/// <param name="config">The object to load configuration values from.</param>
		public HaydenMysqlThreadConsumer(HaydenMysqlConfig config)
		{
			Config = config;
			ConnectionPool = new MySqlConnectionPool(config.ConnectionString, config.SqlConnectionPoolSize);
		}

		/// <inheritdoc/>
		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			string board = threadUpdateInfo.ThreadPointer.Board;

			{ // delete this block when not testing
				string threadDirectory = Path.Combine(Config.DownloadLocation, board, "thread");
				string threadFileName = Path.Combine(threadDirectory, $"{threadUpdateInfo.ThreadPointer.ThreadId}.json");

				Directory.CreateDirectory(threadDirectory);

				FilesystemThreadConsumer.PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);
			}

			void ProcessImages(Post post)
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
				await InsertThread(threadUpdateInfo.Thread, board);
			}

			foreach (var post in threadUpdateInfo.NewPosts)
			{
				ProcessImages(post);
			}

			await InsertPosts(threadUpdateInfo.NewPosts, board);

			foreach (var post in threadUpdateInfo.UpdatedPosts)
			{
				Program.Log($"[DB] Post /{board}/{post.PostNumber} has been modified", true);

				await UpdatePost(post, board, false);
			}

			foreach (var postNumber in threadUpdateInfo.DeletedPosts)
			{
				Program.Log($"[DB] Post /{board}/{postNumber} has been deleted", true);

				await UpdatePost(postNumber, board, true);
			}

			await UpdateThread(threadUpdateInfo.ThreadPointer.ThreadId, board, false, threadUpdateInfo.Thread.OriginalPost.Archived == true);

			return imageDownloads;
		}

		/// <inheritdoc/>
		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			await UpdateThread(threadId, board, deleted, !deleted);
		}
		
		#region Sql

		/// <summary>
		/// Inserts a collection of posts into their relevant table on the database.
		/// </summary>
		/// <param name="posts">The posts to insert.</param>
		/// <param name="board">The board of the posts.</param>
		public async Task InsertPosts(ICollection<Post> posts, string board)
		{
			string insertQuerySql = "INSERT INTO posts"
									+ "         (board, postid, threadid, html, author, mediahash, mediafilename, datetime, isspoiler, isdeleted, isimagedeleted)"
									+ "  VALUES (@board, @postid, @threadid, @html, @author, @mediahash, @mediafilename, @datetime, @isspoiler, 0, @isimagedeleted);";

			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			using var chainedQuery = rentedConnection.Object.CreateQuery(insertQuerySql, true);

			foreach (var post in posts)
			{
				await chainedQuery
					  .SetParam("@board", board)
					  .SetParam("@postid", post.PostNumber)
					  .SetParam("@threadid", post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber)
					  .SetParam("@html", post.Comment)
					  .SetParam("@author", post.Name == "Anonymous" ? null : post.Name + post.Trip)
					  .SetParam("@mediahash", post.FileMd5 == null ? null : Convert.FromBase64String(post.FileMd5))
					  .SetParam("@mediafilename", post.OriginalFilenameFull)
					  .SetParam("@datetime", Utility.ConvertGMTTimestamp(post.UnixTimestamp).UtcDateTime)
					  .SetParam("@isspoiler", post.SpoilerImage == true ? 1 : 0)
					  .SetParam("@isimagedeleted", post.FileDeleted == true ? 1 : 0)
					  .ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Inserts a new thread into the 'threads' metadata table.
		/// </summary>
		/// <param name="thread">The thread to insert.</param>
		/// <param name="board">The board of the thread.</param>
		public async Task InsertThread(Thread thread, string board)
		{
			string insertQuerySql = "INSERT INTO threads"
									+ "         (board, threadid, title, lastmodified, isarchived, isdeleted)"
									+ "  VALUES (@board, @threadid, @title, '1337-01-01', 0, 0);";

			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			using var chainedQuery = rentedConnection.Object.CreateQuery(insertQuerySql, true);

			await chainedQuery
				.SetParam("@board", board)
				.SetParam("@threadid", thread.OriginalPost.PostNumber)
				.SetParam("@title", string.IsNullOrWhiteSpace(thread.OriginalPost.Subject) ? null : thread.OriginalPost.Subject)
				.ExecuteNonQueryAsync();
		}
		
		/// <summary>
		/// Updates an existing post in the database.
		/// </summary>
		/// <param name="post">The post to update.</param>
		/// <param name="board">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if it was not.</param>
		public async Task UpdatePost(Post post, string board, bool deleted)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string sql = $"UPDATE posts SET "
						 + "Html = @html, "
						 + "IsDeleted = @deleted, "
						 + "IsImageDeleted = @imagedeleted "
						 + "WHERE PostId = @postid "
						 + "AND board = @board";

			await rentedConnection.Object.CreateQuery(sql)
								  .SetParam("@html", post.Comment)
								  .SetParam("@deleted", deleted ? 1 : 0)
								  .SetParam("@imagedeleted", post.FileDeleted == true ? 1 : 0)
								  .SetParam("@postid", post.PostNumber)
								  .SetParam("@board", board)
								  .ExecuteNonQueryAsync();
		}

		/// <summary>
		/// Updates an existing post in the database, but only using the post ID (i.e. for use when a post gets deleted).
		/// </summary>
		/// <param name="postId">The ID of the post to update.</param>
		/// <param name="board">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if it was not.</param>
		public async Task UpdatePost(ulong postId, string board, bool deleted)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string sql = $"UPDATE posts SET "
						 + "IsDeleted = @deleted "
						 + "WHERE PostId = @postid "
						 + "AND board = @board";

			await rentedConnection.Object.CreateQuery(sql)
								  .SetParam("@deleted", deleted ? 1 : 0)
								  .SetParam("@board", board)
								  .SetParam("@postid", postId)
								  .ExecuteNonQueryAsync();
		}

		/// <summary>
		/// Sets a post tracking status in the database.
		/// </summary>
		/// <param name="threadId">The number of the post.</param>
		/// <param name="board">The board that the post belongs to.</param>
		/// <param name="deleted">True if the post was explicitly deleted, false if not.</param>
		public async Task UpdateThread(ulong threadId, string board, bool deleted, bool archived, DateTimeOffset? lastModified = null)
		{
			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			await rentedConnection.Object.CreateQuery("UPDATE threads SET " +
			                                          "isdeleted = @deleted, " +
			                                          "isarchived = @archived, " +
													  "lastmodified = COALESCE(@lastmodified, (SELECT MAX(DateTime) FROM posts WHERE board = @board and threadid = @threadid GROUP BY threadid), '1000-01-01') " +
			                                          "WHERE threadid = @threadid AND board = @board")
								  .SetParam("@board", board)
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

			await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

			string query;

			if (getMetadata)
			{
				query = $@"SELECT threads.ThreadId, threads.LastModified
						   FROM threads
						   WHERE
							 	 (threads.IsArchived = {archivedInt} OR {archivedInt} = 0)
							 AND threads.Board = '{board}'
							 AND threads.ThreadId IN ({string.Join(',', threadIdsToCheck)})
						   GROUP BY threads.ThreadId";
				
				var threadTable = await rentedConnection.Object.CreateQuery(query).ExecuteTableAsync();
				
				var items = new List<ExistingThreadInfo>();

				foreach (DataRow threadRow in threadTable.Rows)
				{
					var hashes = new List<(ulong PostId, uint PostHash)>();

					var rows = rentedConnection.Object
						.CreateQuery("SELECT PostId, Html, IsSpoiler, IsImageDeleted, MediaFilename " +
						             "FROM posts " +
						             "WHERE threadid = @threadid AND board = @board AND IsDeleted = 0")
						.SetParam("@threadid", (ulong)threadRow[0])
						.SetParam("@board", board)
						.ExecuteRowsAsync();

					await foreach (var postRow in rows)
					{
						string filenameNoExt = postRow.GetValue<string>("MediaFilename");
						filenameNoExt = filenameNoExt?.Substring(0, filenameNoExt.IndexOf('.'));

						var hash = TrackedThread.CalculateYotsubaPostHash(postRow.GetValue<string>("Html"), postRow.GetValue<bool>("IsSpoiler"),
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
							 AND Board = ""{board}""
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

		/// <summary>
		/// Disposes the object.
		/// </summary>
		public void Dispose()
		{
			ConnectionPool.Dispose();
		}
	}
}