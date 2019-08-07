using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using MySql.Data.MySqlClient;
using Thread = Hayden.Models.Thread;

namespace Hayden.Consumers
{
	public class AsagiThreadConsumer : IThreadConsumer
	{
		private AsagiConfig Config { get; }
		private string ThumbDownloadLocation { get; }
		private string ImageDownloadLocation { get; }

		private SqlConnectionPool ConnectionPool { get; }

		private ConcurrentDictionary<string, DatabaseCommands> PreparedStatements { get; } = new ConcurrentDictionary<string, DatabaseCommands>();

		public AsagiThreadConsumer(AsagiConfig config, string[] boards)
		{
			Config = config;
			ConnectionPool = new SqlConnectionPool(config.ConnectionString, config.SqlConnectionPoolSize);

			foreach (string board in boards)
				ConnectionPool.ForEachConnection(async connection => GetPreparedStatements(board).PrepareConnection(connection)).Wait();

			ThumbDownloadLocation = Path.Combine(Config.DownloadLocation, "thumb");
			ImageDownloadLocation = Path.Combine(Config.DownloadLocation, "images");

			Directory.CreateDirectory(ThumbDownloadLocation);
			Directory.CreateDirectory(ImageDownloadLocation);
		}

		private ConcurrentDictionary<ThreadHashObject, SortedList<ulong, int>> ThreadHashes { get; } = new ConcurrentDictionary<ThreadHashObject, SortedList<ulong, int>>();

		private DatabaseCommands GetPreparedStatements(string board)
			=> PreparedStatements.GetOrAdd(board, b =>
			{
				return new DatabaseCommands(ConnectionPool, b);
			});

		public async Task ConsumeThread(Thread thread, string board)
		{
			var dbCommands = GetPreparedStatements(board);

			var hashObject = new ThreadHashObject(board, thread.OriginalPost.PostNumber);

			if (!ThreadHashes.TryGetValue(hashObject, out var threadHashes))
			{
				// Rebuild hashes from database, if they exist

				var hashes = await dbCommands.WithAccess(connection => dbCommands.GetHashesOfThread(hashObject.ThreadId));

				threadHashes = new SortedList<ulong, int>();

				foreach (var hashPair in hashes)
					threadHashes.Add(hashPair.Key, hashPair.Value);
			}

			List<Post> postsToAdd = new List<Post>(thread.Posts.Length);

			foreach (var post in thread.Posts)
			{
				if (threadHashes.TryGetValue(post.PostNumber, out int existingHash))
				{
					if (post.GenerateAsagiHash() != existingHash)
					{
						// Post has changed since we last saved it to the database

						Program.Log($"[Asagi] Post /{board}/{post.PostNumber} has been modified");

						await dbCommands.WithAccess(() => dbCommands.UpdatePost(post, false));

						threadHashes[post.PostNumber] = post.GenerateAsagiHash();
					}
					else
					{
						// Post has not changed
					}
				}
				else
				{
					// Post has not yet been inserted into the database

					if (post.FileMd5 != null)
					{
						string timestampString = post.TimestampedFilename.ToString();
						string radixString = Path.Combine(timestampString.Substring(0, 4), timestampString.Substring(4, 2));

						if (Config.FullImagesEnabled)
						{
							Directory.CreateDirectory(Path.Combine(ImageDownloadLocation, board, radixString));

							string fullImageFilename = Path.Combine(ImageDownloadLocation, radixString, post.TimestampedFilenameFull);
							string fullImageUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}";

							await DownloadFile(fullImageUrl, fullImageFilename);
						}

						if (Config.ThumbnailsEnabled)
						{
							Directory.CreateDirectory(Path.Combine(ThumbDownloadLocation, board, radixString));

							string thumbFilename = Path.Combine(ThumbDownloadLocation, radixString, $"{post.TimestampedFilename}s.jpg");
							string thumbUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg";

							await DownloadFile(thumbUrl, thumbFilename);
						}
					}

					postsToAdd.Add(post);
				}
			}

			if (threadHashes.Count == 0)
			{
				// We are inserting the thread for the first time.

				await dbCommands.WithAccess(() => dbCommands.InsertPosts(thread.Posts));
			}
			else
			{
				if (postsToAdd.Count > 0)
					await dbCommands.WithAccess(() => dbCommands.InsertPosts(postsToAdd));
			}

			foreach (var post in postsToAdd)
				threadHashes[post.PostNumber] = post.GenerateAsagiHash();

			Program.Log($"[Asagi] {postsToAdd.Count} posts have been inserted from thread /{board}/{thread.OriginalPost.PostNumber}");

			List<ulong> postNumbersToDelete = new List<ulong>(thread.Posts.Length);

			foreach (var postNumber in threadHashes.Keys)
			{
				if (thread.Posts.All(x => x.PostNumber != postNumber))
				{
					// Post has been deleted

					Program.Log($"[Asagi] Post /{board}/{postNumber} has been deleted");

					await dbCommands.WithAccess(() => dbCommands.DeletePostOrThread(postNumber));

					postNumbersToDelete.Add(postNumber);
				}
			}

			// workaround for not being able to remove from a collection while enumerating it
			foreach (var postNumber in postNumbersToDelete)
				threadHashes.Remove(postNumber, out _);


			if (thread.OriginalPost.Archived == true)
			{
				// We don't need the hashes if the thread is archived, since it will never change
				// If it does change, we can just grab a new set from the database

				ThreadHashes.TryRemove(hashObject, out _);
			}
		}

		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			if (deleted)
			{
				await GetPreparedStatements(board).DeletePostOrThread(threadId);
			}

			ThreadHashes.TryRemove(new ThreadHashObject(board, threadId), out _);
		}

		public async Task<ulong[]> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly)
		{
			var dbCommands = GetPreparedStatements(board);

			int archivedInt = archivedOnly ? 1 : 0;

			return await dbCommands.WithAccess(async connection =>
			{
				string checkQuery = $"SELECT num FROM `{board}` WHERE op = 1 AND (locked = {archivedInt} OR deleted = {archivedInt}) AND num IN ({string.Join(',', threadIdsToCheck)});";

				using (var command = new MySqlCommand(checkQuery, connection))
				using (var reader = await command.ExecuteReaderAsync())
				{
					return (from IDataRecord record in reader
						select (ulong)(uint)record[0]).ToArray();
				}
			});
		}

		public void Dispose()
		{
			foreach (var preparedStatements in PreparedStatements.Values)
				preparedStatements.Dispose();
		}

		~AsagiThreadConsumer()
		{
			Dispose();
		}

		private async Task DownloadFile(string imageUrl, string downloadPath)
		{
			if (File.Exists(downloadPath))
				return;

			Program.Log($"Downloading image {Path.GetFileName(downloadPath)}");

			using (var webStream = await YotsubaApi.HttpClient.GetStreamAsync(imageUrl))
			using (var fileStream = new FileStream(downloadPath, FileMode.Create))
			{
				await webStream.CopyToAsync(fileStream);
			}
		}

		private class DatabaseCommands : IDisposable
		{
			public SemaphoreSlim AccessSemaphore { get; } = new SemaphoreSlim(1);

			private SqlConnectionPool ConnectionPool { get; }

			private string Board { get; }

			private MySqlCommand InsertQuery { get; }
			private MySqlCommand UpdateQuery { get; }
			private MySqlCommand DeleteQuery { get; }
			private MySqlCommand SelectHashQuery { get; }

			private static readonly string CreateTablesQuery = Utility.GetEmbeddedText("Hayden.Consumers.AsagiSchema.sql");

			private const string BaseInsertQuery = "INSERT INTO `{0}`"
												   + "  (poster_ip, num, subnum, thread_num, op, timestamp, timestamp_expired, preview_orig, preview_w, preview_h,"
												   + "  media_filename, media_w, media_h, media_size, media_hash, media_orig, spoiler, deleted,"
												   + "  capcode, email, name, trip, title, comment, delpass, sticky, locked, poster_hash, poster_country, exif)"
												   + "    VALUES (0, @num, 0, @thread_num, @op, @timestamp, @timestamp_expired, @preview_orig, @preview_w, @preview_h,"
												   + "      @media_filename, @media_w, @media_h, @media_size, @media_hash, @media_orig, @spoiler, @deleted,"
												   + "      @capcode, @email, @name, @trip, @title, @comment, NULL, @sticky, @locked, @poster_hash, @poster_country, NULL);";

			private readonly DataTable postDataTable;

			public DatabaseCommands(SqlConnectionPool connectionPool, string board)
			{
				ConnectionPool = connectionPool;
				Board = board;

				CreateTables(board);

				InsertQuery = new MySqlCommand(string.Format(BaseInsertQuery, board));
				InsertQuery.Parameters.Add(new MySqlParameter("@num", MySqlDbType.UInt32, -1, "num"));
				InsertQuery.Parameters.Add(new MySqlParameter("@thread_num", MySqlDbType.UInt32, -1, "thread_num"));
				InsertQuery.Parameters.Add(new MySqlParameter("@op", MySqlDbType.Byte, -1, "op"));
				InsertQuery.Parameters.Add(new MySqlParameter("@timestamp", MySqlDbType.UInt32, -1, "timestamp"));
				InsertQuery.Parameters.Add(new MySqlParameter("@timestamp_expired", MySqlDbType.UInt32, -1, "timestamp_expired"));
				InsertQuery.Parameters.Add(new MySqlParameter("@preview_orig", MySqlDbType.VarChar, 20, "preview_orig"));
				InsertQuery.Parameters.Add(new MySqlParameter("@preview_w", MySqlDbType.UInt16, -1, "preview_w"));
				InsertQuery.Parameters.Add(new MySqlParameter("@preview_h", MySqlDbType.UInt16, -1, "preview_h"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_filename", MySqlDbType.Text, -1, "media_filename"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_w", MySqlDbType.UInt16, -1, "media_w"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_h", MySqlDbType.UInt16, -1, "media_h"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_size", MySqlDbType.UInt32, -1, "media_size"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_hash", MySqlDbType.VarChar, 25, "media_hash"));
				InsertQuery.Parameters.Add(new MySqlParameter("@media_orig", MySqlDbType.VarChar, 20, "media_orig"));
				InsertQuery.Parameters.Add(new MySqlParameter("@spoiler", MySqlDbType.Byte, -1, "spoiler"));
				InsertQuery.Parameters.Add(new MySqlParameter("@deleted", MySqlDbType.Byte, -1, "deleted"));
				InsertQuery.Parameters.Add(new MySqlParameter("@capcode", MySqlDbType.VarChar, 1, "capcode"));
				InsertQuery.Parameters.Add(new MySqlParameter("@email", MySqlDbType.VarChar, 100, "email"));
				InsertQuery.Parameters.Add(new MySqlParameter("@name", MySqlDbType.VarChar, 100, "name"));
				InsertQuery.Parameters.Add(new MySqlParameter("@trip", MySqlDbType.VarChar, 25, "trip"));
				InsertQuery.Parameters.Add(new MySqlParameter("@title", MySqlDbType.VarChar, 100, "title"));
				InsertQuery.Parameters.Add(new MySqlParameter("@comment", MySqlDbType.Text, -1, "comment"));
				//InsertQuery.Parameters.Add("@delpass", MySqlDbType.TinyText);
				InsertQuery.Parameters.Add(new MySqlParameter("@sticky", MySqlDbType.Byte, -1, "sticky"));
				InsertQuery.Parameters.Add(new MySqlParameter("@locked", MySqlDbType.Byte, -1, "locked"));
				InsertQuery.Parameters.Add(new MySqlParameter("@poster_hash", MySqlDbType.VarChar, 8, "poster_hash"));
				InsertQuery.Parameters.Add(new MySqlParameter("@poster_country", MySqlDbType.VarChar, 2, "poster_country"));
				//InsertQuery.Parameters.Add("@exif", MySqlDbType.Text);
				
				postDataTable = new DataTable();
				foreach (MySqlParameter param in InsertQuery.Parameters)
				{
					postDataTable.Columns.Add(param.ParameterName.Substring(1));
				}

				UpdateQuery = new MySqlCommand($"UPDATE `{board}` SET comment = @comment, deleted = @deleted, media_filename = COALESCE(@media_filename, media_filename), sticky = (@sticky OR sticky), locked = (@locked or locked) WHERE num = @thread_no AND subnum = @subnum");
				UpdateQuery.Parameters.Add("@comment", MySqlDbType.Text);
				UpdateQuery.Parameters.Add("@deleted", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@media_filename", MySqlDbType.Text);
				UpdateQuery.Parameters.Add("@sticky", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@locked", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
				UpdateQuery.Parameters.Add("@subnum", MySqlDbType.UInt32);

				DeleteQuery = new MySqlCommand($"UPDATE `{board}` SET deleted = 1, timestamp_expired = @timestamp_expired WHERE num = @thread_no AND subnum = 0");
				DeleteQuery.Parameters.Add("@timestamp_expired", MySqlDbType.UInt32);
				DeleteQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);

				SelectHashQuery = new MySqlCommand($"SELECT num, locked, sticky, comment, media_filename FROM `{board}` WHERE thread_num = @thread_no");
				SelectHashQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
			}

			private void CreateTables(string board)
			{
				using (var rentedConnection = ConnectionPool.RentConnection())
				{
					var command = new MySqlCommand($"SHOW TABLES LIKE '{board}\\_%';", rentedConnection.Object);
					DataTable tables = new DataTable();

					using (var reader = command.ExecuteReader())
						tables.Load(reader);

					if (tables.Rows.Count == 0)
					{
						Program.Log($"[Asagi] Creating tables for board /{board}/");

						string formattedQuery = string.Format(CreateTablesQuery, board);

						foreach (var splitString in formattedQuery.Split('$'))
						{
							command = new MySqlCommand(splitString, rentedConnection.Object);
							command.ExecuteNonQuery();
						}
					}

					command.Dispose();
				}
			}

			public async Task InsertPost(Post post)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					InsertQuery.Connection = rentedConnection;

					InsertQuery.Parameters["@post_no"].Value = post.PostNumber;
					InsertQuery.Parameters["@thread_no"].Value = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
					InsertQuery.Parameters["@is_op"].Value = post.ReplyPostNumber == 0 ? 1 : 0;
					InsertQuery.Parameters["@timestamp"].Value = post.UnixTimestamp;
					InsertQuery.Parameters["@timestamp_expired"].Value = 0;
					InsertQuery.Parameters["@preview_orig"].Value = post.TimestampedFilename.HasValue ? $"{post.TimestampedFilename}s.jpg" : null;
					InsertQuery.Parameters["@preview_w"].Value = post.ThumbnailWidth ?? 0;
					InsertQuery.Parameters["@preview_h"].Value = post.ThumbnailHeight ?? 0;
					InsertQuery.Parameters["@media_filename"].Value = post.OriginalFilenameFull;
					InsertQuery.Parameters["@media_w"].Value = post.ImageWidth ?? 0;
					InsertQuery.Parameters["@media_h"].Value = post.ImageHeight ?? 0;
					InsertQuery.Parameters["@media_size"].Value = post.FileSize ?? 0;
					InsertQuery.Parameters["@media_hash"].Value = post.FileMd5;
					InsertQuery.Parameters["@media_orig"].Value = post.TimestampedFilenameFull;
					InsertQuery.Parameters["@spoiler"].Value = post.SpoilerImage == true ? 1 : 0;
					InsertQuery.Parameters["@deleted"].Value = 0;
					InsertQuery.Parameters["@capcode"].Value = post.Capcode?.Substring(0, 1).ToUpperInvariant() ?? "N";
					InsertQuery.Parameters["@email"].Value = null; // 4chan api doesn't supply this????
					InsertQuery.Parameters["@name"].Value = HttpUtility.HtmlDecode(post.Name);
					InsertQuery.Parameters["@trip"].Value = post.Trip;
					InsertQuery.Parameters["@title"].Value = HttpUtility.HtmlDecode(post.Subject);
					InsertQuery.Parameters["@comment"].Value = post.Comment;
					InsertQuery.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
					InsertQuery.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
					InsertQuery.Parameters["@poster_hash"].Value = post.PosterID == "Developer" ? "Dev" : post.PosterID;
					InsertQuery.Parameters["@poster_country"].Value = post.CountryCode;

					await InsertQuery.ExecuteNonQueryAsync();
				}
			}

			public async Task InsertPosts(IEnumerable<Post> posts)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedCommand = (MySqlCommand)InsertQuery.Clone())
				using (var transaction = await rentedConnection.Object.BeginTransactionAsync())
				using (var clonedSet = postDataTable.Clone())
				using (var adapter = new MySqlDataAdapter($"SELECT * FROM {Board} WHERE 1 = 0", rentedConnection.Object))
				{
					clonedCommand.Connection = rentedConnection;
					clonedCommand.Transaction = transaction;

					clonedCommand.UpdatedRowSource = UpdateRowSource.None;
					

					foreach (var post in posts)
					{
						var row = clonedSet.NewRow();
						row["num"] = post.PostNumber;
						row["thread_num"] = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
						row["op"] = post.ReplyPostNumber == 0 ? 1 : 0;
						row["timestamp"] = post.UnixTimestamp;
						row["timestamp_expired"] = 0;
						row["preview_orig"] = post.TimestampedFilename.HasValue ? $"{post.TimestampedFilename}s.jpg" : null;
						row["preview_w"] = post.ThumbnailWidth ?? 0;
						row["preview_h"] = post.ThumbnailHeight ?? 0;
						row["media_filename"] = post.OriginalFilenameFull;
						row["media_w"] = post.ImageWidth ?? 0;
						row["media_h"] = post.ImageHeight ?? 0;
						row["media_size"] = post.FileSize ?? 0;
						row["media_hash"] = post.FileMd5;
						row["media_orig"] = post.TimestampedFilenameFull;
						row["spoiler"] = post.SpoilerImage == true ? 1 : 0;
						row["deleted"] = 0;
						row["capcode"] = post.Capcode?.Substring(0, 1).ToUpperInvariant() ?? "N";
						row["email"] = null; // 4chan api doesn't supply this????
						row["name"] = HttpUtility.HtmlDecode(post.Name);
						row["trip"] = post.Trip;
						row["title"] = HttpUtility.HtmlDecode(post.Subject);
						row["comment"] = post.Comment;
						row["sticky"] = post.Sticky == true ? 1 : 0;
						row["locked"] = post.Closed == true ? 1 : 0;
						row["poster_hash"] = post.PosterID == "Developer" ? "Dev" : post.PosterID;
						row["poster_country"] = post.CountryCode;

						clonedSet.Rows.Add(row);
					}

					adapter.InsertCommand = clonedCommand;
					adapter.UpdateBatchSize = 100;

					adapter.Update(clonedSet);

					transaction.Commit();
				}
			}

			public async Task UpdatePost(Post post, bool deleted)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					UpdateQuery.Connection = rentedConnection;

					UpdateQuery.Parameters["@comment"].Value = post.Comment;
					UpdateQuery.Parameters["@deleted"].Value = deleted ? 1 : 0;
					UpdateQuery.Parameters["@media_filename"].Value = post.OriginalFilenameFull;
					UpdateQuery.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
					UpdateQuery.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
					UpdateQuery.Parameters["@thread_no"].Value = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
					UpdateQuery.Parameters["@subnum"].Value = 0;

					await UpdateQuery.ExecuteNonQueryAsync();
				}
			}

			public async Task DeletePostOrThread(ulong theadNumber)
			{
				uint currentTimestamp = Utility.GetNewYorkTimestamp(DateTimeOffset.Now);

				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					DeleteQuery.Connection = rentedConnection;

					DeleteQuery.Parameters["@timestamp_expired"].Value = currentTimestamp;
					DeleteQuery.Parameters["@thread_no"].Value = theadNumber;

					await DeleteQuery.ExecuteNonQueryAsync();
				}
			}

			public async Task<IEnumerable<KeyValuePair<ulong, int>>> GetHashesOfThread(ulong theadNumber)
			{
				var threadHashes = new List<KeyValuePair<ulong, int>>();

				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedConnection = (MySqlCommand)SelectHashQuery.Clone())
				{
					clonedConnection.Connection = rentedConnection;

					clonedConnection.Parameters["@thread_no"].Value = theadNumber; 

					using (var reader = (MySqlDataReader)await clonedConnection.ExecuteReaderAsync())
					{
						Post tempPost = new Post();

						while (reader.Read())
						{
							tempPost.PostNumber = reader.GetUInt32("num");
							tempPost.Closed = reader.GetBoolean("locked") ? (bool?)true : null;
							tempPost.Sticky = reader.GetBoolean("sticky") ? (bool?)true : null;
							tempPost.Comment = reader["comment"] == DBNull.Value ? null : (string)reader["comment"];
							tempPost.OriginalFilename = reader["media_filename"] == DBNull.Value
								? null
								: ((string)reader["media_filename"]).Substring(0, ((string)reader["media_filename"]).LastIndexOf('.'));

							threadHashes.Add(new KeyValuePair<ulong, int>(tempPost.PostNumber, tempPost.GenerateAsagiHash()));
						}
					}
				}

				return threadHashes;
			}

			public void PrepareConnection(MySqlConnection connection)
			{
				//InsertQuery.Connection = connection;
				//InsertQuery.Prepare();
				//SelectHashQuery.Connection = connection;
				//SelectHashQuery.Prepare();
			}

			public async Task<T> WithAccess<T>(Func<MySqlConnection, Task<T>> taskFunc)
			{
				await AccessSemaphore.WaitAsync();

				try
				{
					using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
						return await taskFunc(rentedConnection.Object);
				}
				finally
				{
					AccessSemaphore.Release();
				}
			}

			public async Task WithAccess(Func<MySqlConnection, Task> taskFunc)
			{
				await AccessSemaphore.WaitAsync();

				try
				{
					using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
						await taskFunc(rentedConnection.Object);
				}
				finally
				{
					AccessSemaphore.Release();
				}
			}

			public async Task WithAccess(Func<Task> taskFunc)
			{
				await AccessSemaphore.WaitAsync();

				try
				{
					await taskFunc();
				}
				finally
				{
					AccessSemaphore.Release();
				}
			}

			private void Dispose(bool disposing)
			{
				AccessSemaphore?.Dispose();
				InsertQuery?.Dispose();
				UpdateQuery?.Dispose();
				DeleteQuery?.Dispose();
				SelectHashQuery?.Dispose();
			}

			public void Dispose()
			{
				Dispose(true);
				GC.SuppressFinalize(this);
			}

			~DatabaseCommands()
			{
				Dispose(false);
			}
		}

		private struct ThreadHashObject
		{
			public string Board { get; set; }

			public ulong ThreadId { get; set; }

			public ThreadHashObject(string board, ulong threadId)
			{
				Board = board;
				ThreadId = threadId;
			}
		}
	}
}