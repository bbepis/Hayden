using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Contract;
using Hayden.Models;
using MySql.Data.MySqlClient;
using Thread = Hayden.Models.Thread;

namespace Hayden.Consumers
{
	public class AsagiThreadConsumer : IThreadConsumer
	{
		private MySqlConnection Connection { get; }
		private string RootDownloadLocation { get; }
		private string ThumbDownloadLocation { get; }
		private string ImageDownloadLocation { get; }

		private ConcurrentDictionary<string, DatabaseCommands> PreparedStatements { get; } = new ConcurrentDictionary<string, DatabaseCommands>();

		public AsagiThreadConsumer(MySqlConnection connection, string rootDownloadLocation)
		{
			Connection = connection;
			RootDownloadLocation = rootDownloadLocation;

			ThumbDownloadLocation = Path.Combine(RootDownloadLocation, "thumb");
			ImageDownloadLocation = Path.Combine(RootDownloadLocation, "images");

			Directory.CreateDirectory(ThumbDownloadLocation);
			Directory.CreateDirectory(ImageDownloadLocation);
		}

		private ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, int>> ThreadHashes { get; } = new ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, int>>();

		private DatabaseCommands GetPreparedStatements(string board)
			=> PreparedStatements.GetOrAdd(board, b => new DatabaseCommands(Connection, b));

		public async Task ConsumeThread(Thread thread, string board)
		{
			var dbCommands = GetPreparedStatements(board);

			var threadHashes = ThreadHashes.GetOrAdd(thread.OriginalPost.PostNumber, x =>
			{
				// Rebuild hashes from database, if they exist

				var hashes = dbCommands.WithAccess(async connection => dbCommands.GetHashesOfThread(x)).Result;

				return new ConcurrentDictionary<ulong, int>(hashes);
			});

			foreach (var post in thread.Posts)
			{
				if (threadHashes.TryGetValue(post.PostNumber, out int existingHash))
				{
					if (post.GenerateAsagiHash() != existingHash)
					{
						// Post has changed since we last saved it to the database

						Program.Log($"[Asagi] Post /{board}/{post.PostNumber} has been modified");

						await dbCommands.WithAccess(connection => dbCommands.UpdatePost(post, false));

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


						Directory.CreateDirectory(Path.Combine(ImageDownloadLocation, radixString));
						Directory.CreateDirectory(Path.Combine(ThumbDownloadLocation, radixString));


						string fullImageFilename = Path.Combine(ImageDownloadLocation, radixString, post.TimestampedFilenameFull);
						string fullImageUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}";

						await DownloadFile(fullImageUrl, fullImageFilename);


						string thumbFilename = Path.Combine(ThumbDownloadLocation, radixString, $"{post.TimestampedFilename}s.jpg");
						string thumbUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg";

						await DownloadFile(thumbUrl, thumbFilename);
					}

					await dbCommands.WithAccess(connection => dbCommands.InsertPost(post));

					threadHashes[post.PostNumber] = post.GenerateAsagiHash();
				}
			}

			foreach (var postNumber in threadHashes.Keys)
			{
				if (thread.Posts.All(x => x.PostNumber != postNumber))
				{
					// Post has been deleted

					Program.Log($"[Asagi] Post /{board}/{postNumber} has been deleted");

					await dbCommands.WithAccess(connection => dbCommands.DeletePost(postNumber));

					threadHashes.Remove(postNumber, out _);
				}
			}
		}

		public async Task ThreadUntracked(ulong threadId, string board)
		{
			ThreadHashes.Remove(threadId, out _);
		}

		public async Task<ulong[]> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly)
		{
			var dbCommands = GetPreparedStatements(board);

			int archivedInt = archivedOnly ? 1 : 0;

			return await dbCommands.WithAccess(async connection =>
			{
				string checkQuery = $"SELECT num FROM `{board}` WHERE op = 1 AND (locked = {archivedInt} OR deleted = {archivedInt}) AND num IN ({string.Join(',', threadIdsToCheck)});";

				var command = new MySqlCommand(checkQuery, connection);
				using (var reader = await command.ExecuteReaderAsync())
				{
					return (from IDataRecord record in reader
						select (ulong)(uint)record[0]).ToArray();
				}
			});
		}

		public void Dispose()
		{
			Connection.Dispose();
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

		private class DatabaseCommands
		{
			public SemaphoreSlim AccessSemaphore { get; } = new SemaphoreSlim(1);

			private MySqlConnection Connection { get; }

			private MySqlCommand InsertQuery { get; }
			private MySqlCommand UpdateQuery { get; }
			private MySqlCommand DeleteQuery { get; }
			private MySqlCommand SelectHashQuery { get; }

			private const string BaseInsertQuery = "INSERT INTO `{0}`"
												   + "  (poster_ip, num, subnum, thread_num, op, timestamp, timestamp_expired, preview_orig, preview_w, preview_h,"
												   + "  media_filename, media_w, media_h, media_size, media_hash, media_orig, spoiler, deleted,"
												   + "  capcode, email, name, trip, title, comment, delpass, sticky, locked, poster_hash, poster_country, exif)"
												   + "    SELECT 0, @post_no, 0, @thread_no, @is_op, @timestamp, @timestamp_expired, @preview_orig, @preview_w, @preview_h,"
												   + "      @media_filename, @media_w, @media_h, @media_size, @media_hash, @media_orig, @spoiler, @deleted,"
												   + "      @capcode, @email, @name, @trip, @title, @comment, NULL, @sticky, @locked, @poster_hash, @poster_country, NULL"
												   + "    FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM `{0}` WHERE num = @post_no AND subnum = 0)"
												   + "      AND NOT EXISTS (SELECT 1 FROM `{0}_deleted` WHERE num = @post_no AND subnum = 0);";

			public DatabaseCommands(MySqlConnection connection, string board)
			{
				Connection = connection;

				InsertQuery = new MySqlCommand(string.Format(BaseInsertQuery, board), connection);
				InsertQuery.Parameters.Add("@post_no", MySqlDbType.UInt32);
				InsertQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
				InsertQuery.Parameters.Add("@is_op", MySqlDbType.Byte);
				InsertQuery.Parameters.Add("@timestamp", MySqlDbType.UInt32);
				InsertQuery.Parameters.Add("@timestamp_expired", MySqlDbType.UInt32);
				InsertQuery.Parameters.Add("@preview_orig", MySqlDbType.VarChar, 20);
				InsertQuery.Parameters.Add("@preview_w", MySqlDbType.UInt16);
				InsertQuery.Parameters.Add("@preview_h", MySqlDbType.UInt16);
				InsertQuery.Parameters.Add("@media_filename", MySqlDbType.Text);
				InsertQuery.Parameters.Add("@media_w", MySqlDbType.UInt16);
				InsertQuery.Parameters.Add("@media_h", MySqlDbType.UInt16);
				InsertQuery.Parameters.Add("@media_size", MySqlDbType.UInt32);
				InsertQuery.Parameters.Add("@media_hash", MySqlDbType.VarChar, 25);
				InsertQuery.Parameters.Add("@media_orig", MySqlDbType.VarChar, 20);
				InsertQuery.Parameters.Add("@spoiler", MySqlDbType.Byte);
				InsertQuery.Parameters.Add("@deleted", MySqlDbType.Byte);
				InsertQuery.Parameters.Add("@capcode", MySqlDbType.VarChar, 1);
				InsertQuery.Parameters.Add("@email", MySqlDbType.VarChar, 100);
				InsertQuery.Parameters.Add("@name", MySqlDbType.VarChar, 100);
				InsertQuery.Parameters.Add("@trip", MySqlDbType.VarChar, 25);
				InsertQuery.Parameters.Add("@title", MySqlDbType.VarChar, 100);
				InsertQuery.Parameters.Add("@comment", MySqlDbType.Text);
				//InsertQuery.Parameters.Add("@delpass", MySqlDbType.TinyText);
				InsertQuery.Parameters.Add("@sticky", MySqlDbType.Byte);
				InsertQuery.Parameters.Add("@locked", MySqlDbType.Byte);
				InsertQuery.Parameters.Add("@poster_hash", MySqlDbType.VarChar, 8);
				InsertQuery.Parameters.Add("@poster_country", MySqlDbType.VarChar, 2);
				//InsertQuery.Parameters.Add("@exif", MySqlDbType.Text);
				InsertQuery.Prepare();
				

				UpdateQuery = new MySqlCommand($"UPDATE `{board}` SET comment = @comment, deleted = @deleted, media_filename = COALESCE(@media_filename, media_filename), sticky = (@sticky OR sticky), locked = (@locked or locked) WHERE num = @thread_no AND subnum = @subnum", connection);
				UpdateQuery.Parameters.Add("@comment", MySqlDbType.Text);
				UpdateQuery.Parameters.Add("@deleted", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@media_filename", MySqlDbType.Text);
				UpdateQuery.Parameters.Add("@sticky", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@locked", MySqlDbType.Byte);
				UpdateQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
				UpdateQuery.Parameters.Add("@subnum", MySqlDbType.UInt32);
				UpdateQuery.Prepare();

				DeleteQuery = new MySqlCommand($"UPDATE `{board}` SET deleted = 1, timestamp_expired = @timestamp_expired WHERE num = @thread_no AND subnum = 0", connection);
				DeleteQuery.Parameters.Add("@timestamp_expired", MySqlDbType.UInt32);
				DeleteQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
				DeleteQuery.Prepare();

				SelectHashQuery = new MySqlCommand($"SELECT num, locked, sticky, comment, media_filename FROM `{board}` WHERE num = @thread_no OR thread_num = @thread_no", connection);
				SelectHashQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);
				SelectHashQuery.Prepare();
			}

			public Task InsertPost(Post post)
			{
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
				InsertQuery.Parameters["@name"].Value = post.Name;
				InsertQuery.Parameters["@trip"].Value = post.Trip;
				InsertQuery.Parameters["@title"].Value = post.Subject;
				InsertQuery.Parameters["@comment"].Value = post.Comment;
				InsertQuery.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
				InsertQuery.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
				InsertQuery.Parameters["@poster_hash"].Value = post.PosterID == "Developer" ? "Dev" : post.PosterID;
				InsertQuery.Parameters["@poster_country"].Value = post.CountryCode;

				return InsertQuery.ExecuteNonQueryAsync();
			}

			public Task UpdatePost(Post post, bool deleted)
			{
				UpdateQuery.Parameters["@comment"].Value = post.Comment;
				UpdateQuery.Parameters["@deleted"].Value = 0;
				UpdateQuery.Parameters["@media_filename"].Value = post.OriginalFilenameFull;
				UpdateQuery.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
				UpdateQuery.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
				UpdateQuery.Parameters["@thread_no"].Value = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
				UpdateQuery.Parameters["@subnum"].Value = 0;

				return UpdateQuery.ExecuteNonQueryAsync();
			}

			public Task DeletePost(ulong theadNumber)
			{
				var currentNewYorkTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("US Eastern Standard Time"));

				int currentTimestamp = (int)(currentNewYorkTime - DateTime.UnixEpoch).TotalSeconds;

				DeleteQuery.Parameters["@timestamp_expired"].Value = currentTimestamp;
				DeleteQuery.Parameters["@thread_no"].Value = theadNumber;

				return DeleteQuery.ExecuteNonQueryAsync();
			}

			public IEnumerable<KeyValuePair<ulong, int>> GetHashesOfThread(ulong theadNumber)
			{
				SelectHashQuery.Parameters["@thread_no"].Value = theadNumber;

				using (var reader = SelectHashQuery.ExecuteReader())
				{
					Post tempPost = new Post();

					while (reader.Read())
					{
						tempPost.PostNumber = reader.GetUInt32("num");
						tempPost.Closed = reader.GetBoolean("locked") ? (bool?)true : null;
						tempPost.Sticky = reader.GetBoolean("sticky") ? (bool?)true : null;
						tempPost.Comment = reader["comment"] == DBNull.Value ? null : (string)reader["comment"];
						tempPost.OriginalFilename = reader["media_filename"] == DBNull.Value ? null
							: ((string)reader["media_filename"]).Substring(0, ((string)reader["media_filename"]).LastIndexOf('.'));

						yield return new KeyValuePair<ulong, int>(tempPost.PostNumber, tempPost.GenerateAsagiHash());
					}
				}
			}

			public async Task<T> WithAccess<T>(Func<MySqlConnection, Task<T>> taskFunc)
			{
				await AccessSemaphore.WaitAsync();

				try
				{
					return await taskFunc(Connection);
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
					await taskFunc(Connection);
				}
				finally
				{
					AccessSemaphore.Release();
				}
			}
		}
	}
}
