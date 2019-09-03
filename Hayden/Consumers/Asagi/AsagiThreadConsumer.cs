using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

namespace Hayden.Consumers
{
	public class AsagiThreadConsumer : IThreadConsumer
	{
		private AsagiConfig Config { get; }
		private string ThumbDownloadLocation { get; }
		private string ImageDownloadLocation { get; }

		private MySqlConnectionPool ConnectionPool { get; }

		private ConcurrentDictionary<string, DatabaseCommands> PreparedStatements { get; } = new ConcurrentDictionary<string, DatabaseCommands>();

		public AsagiThreadConsumer(AsagiConfig config, string[] boards)
		{
			Config = config;
			ConnectionPool = new MySqlConnectionPool(config.ConnectionString, config.SqlConnectionPoolSize);

			ThumbDownloadLocation = Path.Combine(Config.DownloadLocation, "thumb");
			ImageDownloadLocation = Path.Combine(Config.DownloadLocation, "image");

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

				var hashes = await dbCommands.GetHashesOfThread(hashObject.ThreadId);

				threadHashes = new SortedList<ulong, int>();

				foreach (var hashPair in hashes)
					threadHashes.Add(hashPair.Key, hashPair.Value);
			}

			List<Post> postsToAdd = new List<Post>(thread.Posts.Length);

			foreach (var post in thread.Posts)
			{
				if (threadHashes.TryGetValue(post.PostNumber, out int existingHash))
				{
					int hash = CalculateAsagiHash(post, true);

					if (hash != existingHash)
					{
						// Post has changed since we last saved it to the database

						Program.Log($"[Asagi] Post /{board}/{post.PostNumber} has been modified");

						await dbCommands.UpdatePost(post, false);

						threadHashes[post.PostNumber] = hash;
					}
					else
					{
						// Post has not changed
					}
				}
				else
				{
					// Post has not yet been inserted into the database
					
					postsToAdd.Add(post);

					if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
						continue; // skip the DB check since we're not even bothering with images

					if (post.FileMd5 != null)
					{
						MediaInfo mediaInfo =  await dbCommands.GetMediaInfo(post.FileMd5);

						if (mediaInfo?.Banned == true)
						{
							Program.Log($"[Asagi] Post /{board}/{post.PostNumber} contains a banned image; skipping");
							continue;
						}

						if (Config.FullImagesEnabled)
						{
							string fullImageName = mediaInfo?.MediaFilename ?? post.TimestampedFilenameFull;

							string radixString = Path.Combine(fullImageName.Substring(0, 4), fullImageName.Substring(4, 2));
							string radixDirectory = Path.Combine(ImageDownloadLocation, board, radixString);
							Directory.CreateDirectory(radixDirectory);

							string fullImageFilename = Path.Combine(radixDirectory, fullImageName);
							string fullImageUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}";

							await DownloadFile(fullImageUrl, fullImageFilename);
						}
						
						if (Config.ThumbnailsEnabled)
						{
							string thumbImageName;

							if (post.ReplyPostNumber == 0) // is OP
								thumbImageName = mediaInfo?.PreviewOpFilename ?? $"{post.TimestampedFilename}s.jpg";
							else
								thumbImageName = mediaInfo?.PreviewReplyFilename ?? $"{post.TimestampedFilename}s.jpg";

							string radixString = Path.Combine(thumbImageName.Substring(0, 4), thumbImageName.Substring(4, 2));
							string radixDirectory = Path.Combine(ThumbDownloadLocation, board, radixString);
							Directory.CreateDirectory(radixDirectory);

							string thumbFilename = Path.Combine(radixDirectory, thumbImageName);
							string thumbUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg";

							await DownloadFile(thumbUrl, thumbFilename);
						}
					}
				}
			}

			if (threadHashes.Count == 0)
			{
				// We are inserting the thread for the first time.

				await dbCommands.InsertPosts(thread.Posts);
			}
			else
			{
				if (postsToAdd.Count > 0)
					await dbCommands.InsertPosts(postsToAdd);
			}

			foreach (var post in postsToAdd)
				threadHashes[post.PostNumber] = CalculateAsagiHash(post, true);

			Program.Log($"[Asagi] {postsToAdd.Count} posts have been inserted from thread /{board}/{thread.OriginalPost.PostNumber}");

			List<ulong> postNumbersToDelete = new List<ulong>(thread.Posts.Length);

			foreach (var postNumber in threadHashes.Keys)
			{
				if (thread.Posts.All(x => x.PostNumber != postNumber))
				{
					// Post has been deleted

					Program.Log($"[Asagi] Post /{board}/{postNumber} has been deleted");

					await dbCommands.DeletePostOrThread(postNumber);

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

			return await dbCommands.CheckExistingThreads(threadIdsToCheck, archivedOnly);
		}

		public static int CalculateAsagiHash(bool? sticky, bool? closed, string comment, string originalFilename)
		{
			unchecked
			{
				int hashCode = sticky == true ? 2 : 1;
				hashCode = (hashCode * 397) ^ (closed == true ? 2 : 1);
				hashCode = (hashCode * 397) ^ (comment?.GetHashCode() ?? 0);
				hashCode = (hashCode * 397) ^ (originalFilename?.GetHashCode() ?? 0);
				return hashCode;
			}
		}

		public static int CalculateAsagiHash(Post post, bool cleanComment)
		{
			string comment = cleanComment ? CleanComment(post.Comment) : post.Comment;

			return CalculateAsagiHash(post.Sticky, post.Closed, comment, post.OriginalFilename);
		}

		public static string CleanComment(string inputComment)
		{
			// Copied wholesale from https://github.com/bibanon/asagi/blob/master/src/main/java/net/easymodo/asagi/YotsubaAbstract.java

			if (string.IsNullOrWhiteSpace(inputComment))
				return string.Empty;

			// SOPA spoilers
			//text = text.replaceAll("<span class=\"spoiler\"[^>]*>(.*?)</spoiler>(</span>)?", "$1");

			// Admin-Mod-Dev quotelinks
			inputComment = Regex.Replace(inputComment, @"<span class=""capcodeReplies""><span style=""font-size: smaller;""><span style=""font-weight: bold;"">(?:Administrator|Moderator|Developer) Repl(?:y|ies):<\/span>.*?<\/span><br><\/span>", "");
			// Non-public tags
			inputComment = Regex.Replace(inputComment, @"\[(\/?(banned|moot|spoiler|code))]", "[$1:lit]");
			// Comment too long, also EXIF tag toggle
			inputComment = Regex.Replace(inputComment, @"<span class=""abbr"">.*?<\/span>", "");
			// EXIF data
			inputComment = Regex.Replace(inputComment, @"<table class=""exif""[^>]*>.*?<\/table>", "");
			// DRAW data
			inputComment = Regex.Replace(inputComment, "<br><br><small><b>Oekaki Post</b>.*?</small>", "");
			// Banned/Warned text
			inputComment = Regex.Replace(inputComment, @"<(?:b|strong) style=""color:\s*red;"">(.*?)<\/(?:b|strong)>", "[banned]$1[/banned]");
			// moot inputComment
			inputComment = Regex.Replace(inputComment, @"<div style=""padding: 5px;margin-left: \.5em;border-color: #faa;border: 2px dashed rgba\(255,0,0,\.1\);border-radius: 2px"">(.*?)</div>", "[moot]$1[/moot]");
			// fortune inputComment
			inputComment = Regex.Replace(inputComment, @"<span class=""fortune"" style=""color:(.*?)""><br><br><b>(.*?)<\/b><\/span>", "\n\n[fortune color=\"$1\"]$2[/fortune]");
			// bold inputComment
			inputComment = Regex.Replace(inputComment, @"<(?:b|strong)>(.*?)<\/(?:b|strong)>", "[b]$1[/b]");
			// code tags
			inputComment = Regex.Replace(inputComment, "<pre[^>]*>", "[code]");
			inputComment = inputComment.Replace("</pre>", "[/code]");
			// math tags
			inputComment = Regex.Replace(inputComment, @"<span class=""math"">(.*?)<\/span>", "[math]$1[/math]");
			inputComment = Regex.Replace(inputComment, @"<div class=""math"">(.*?)<\/div>", "[eqn]$1[/eqn]");
			// > implying I'm quoting someone
			inputComment = Regex.Replace(inputComment, @"<font class=""unkfunc"">(.*?)<\/font>", "$1");
			inputComment = Regex.Replace(inputComment, @"<span class=""quote"">(.*?)<\/span>", "$1");
			inputComment = Regex.Replace(inputComment, @"<span class=""(?:[^""]*)?deadlink"">(.*?)<\/span>", "$1");
			// Links
			inputComment = Regex.Replace(inputComment, "<a[^>]*>(.*?)</a>", "$1");
			// old spoilers
			inputComment = Regex.Replace(inputComment, "<span class=\"spoiler\"[^>]*>(.*?)</span>", "[spoiler]$1[/spoiler]");
			// ShiftJIS
			inputComment = Regex.Replace(inputComment, "<span class=\"sjis\">(.*?)</span>", "[shiftjis]$1[/shiftjis]");
			// new spoilers
			inputComment = inputComment.Replace("<s>", "[spoiler]");
			inputComment = inputComment.Replace("</s>", "[/spoiler]");
			// new line/wbr
			inputComment = Regex.Replace(inputComment, "<br\\s*/?>", "\n");
			inputComment = inputComment.Replace("<wbr>", "");

			return HttpUtility.HtmlDecode(inputComment).Trim();
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
			using (var fileStream = new FileStream(downloadPath + ".part", FileMode.Create))
			{
				await webStream.CopyToAsync(fileStream);
			}

			File.Move(downloadPath + ".part", downloadPath);
		}

		private class DatabaseCommands : IDisposable
		{
			public SemaphoreSlim AccessSemaphore { get; } = new SemaphoreSlim(1);

			private MySqlConnectionPool ConnectionPool { get; }

			private string Board { get; }

			private MySqlCommand InsertQuery { get; }
			private MySqlCommand UpdateQuery { get; }
			private MySqlCommand DeleteQuery { get; }
			private MySqlCommand SelectPostHashQuery { get; }
			private MySqlCommand SelectMediaHashQuery { get; }

			private static readonly string CreateTablesQuery = Utility.GetEmbeddedText("Hayden.Consumers.Asagi.AsagiSchema.sql");

			private const string BaseInsertQuery = "INSERT INTO `{0}`"
												   + "  (poster_ip, num, subnum, thread_num, op, timestamp, timestamp_expired, preview_orig, preview_w, preview_h,"
												   + "  media_filename, media_w, media_h, media_size, media_hash, media_orig, spoiler, deleted,"
												   + "  capcode, email, name, trip, title, comment, delpass, sticky, locked, poster_hash, poster_country, exif)"
												   + "    VALUES (0, @num, 0, @thread_num, @op, @timestamp, @timestamp_expired, @preview_orig, @preview_w, @preview_h,"
												   + "      @media_filename, @media_w, @media_h, @media_size, @media_hash, @media_orig, @spoiler, @deleted,"
												   + "      @capcode, @email, @name, @trip, @title, @comment, NULL, @sticky, @locked, @poster_hash, @poster_country, @exif);";

			private readonly DataTable postDataTable;

			public DatabaseCommands(MySqlConnectionPool connectionPool, string board)
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
				InsertQuery.Parameters.Add(new MySqlParameter("@sticky", MySqlDbType.Byte, -1, "sticky"));
				InsertQuery.Parameters.Add(new MySqlParameter("@locked", MySqlDbType.Byte, -1, "locked"));
				InsertQuery.Parameters.Add(new MySqlParameter("@poster_hash", MySqlDbType.VarChar, 8, "poster_hash"));
				InsertQuery.Parameters.Add(new MySqlParameter("@poster_country", MySqlDbType.VarChar, 2, "poster_country"));
				InsertQuery.Parameters.Add(new MySqlParameter("@exif", MySqlDbType.Text, -1, "exif"));
				
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

				SelectPostHashQuery = new MySqlCommand($"SELECT num, locked, sticky, comment, media_filename FROM `{board}` WHERE thread_num = @thread_no");
				SelectPostHashQuery.Parameters.Add("@thread_no", MySqlDbType.UInt32);

				SelectMediaHashQuery = new MySqlCommand($"SELECT media, preview_op, preview_reply, banned FROM `{board}_images` WHERE `media_hash` = @media_hash");
				SelectMediaHashQuery.Parameters.Add("@media_hash", MySqlDbType.UInt32);
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
				using (var clonedCommand = InsertQuery.Clone())
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					clonedCommand.Connection = rentedConnection;

					clonedCommand.Parameters["@post_no"].Value = post.PostNumber;
					clonedCommand.Parameters["@thread_no"].Value = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
					clonedCommand.Parameters["@is_op"].Value = post.ReplyPostNumber == 0 ? 1 : 0;
					clonedCommand.Parameters["@timestamp"].Value = post.UnixTimestamp;
					clonedCommand.Parameters["@timestamp_expired"].Value = 0;
					clonedCommand.Parameters["@preview_orig"].Value = post.TimestampedFilename.HasValue ? $"{post.TimestampedFilename}s.jpg" : null;
					clonedCommand.Parameters["@preview_w"].Value = post.ThumbnailWidth ?? 0;
					clonedCommand.Parameters["@preview_h"].Value = post.ThumbnailHeight ?? 0;
					clonedCommand.Parameters["@media_filename"].Value = post.OriginalFilenameFull;
					clonedCommand.Parameters["@media_w"].Value = post.ImageWidth ?? 0;
					clonedCommand.Parameters["@media_h"].Value = post.ImageHeight ?? 0;
					clonedCommand.Parameters["@media_size"].Value = post.FileSize ?? 0;
					clonedCommand.Parameters["@media_hash"].Value = post.FileMd5;
					clonedCommand.Parameters["@media_orig"].Value = post.TimestampedFilenameFull;
					clonedCommand.Parameters["@spoiler"].Value = post.SpoilerImage == true ? 1 : 0;
					clonedCommand.Parameters["@deleted"].Value = 0;
					clonedCommand.Parameters["@capcode"].Value = post.Capcode?.Substring(0, 1).ToUpperInvariant() ?? "N";
					clonedCommand.Parameters["@email"].Value = null; // 4chan api doesn't supply this????
					clonedCommand.Parameters["@name"].Value = HttpUtility.HtmlDecode(post.Name)?.Trim();
					clonedCommand.Parameters["@trip"].Value = post.Trip;
					clonedCommand.Parameters["@title"].Value = HttpUtility.HtmlDecode(post.Subject)?.Trim();
					clonedCommand.Parameters["@comment"].Value = CleanComment(post.Comment);
					clonedCommand.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
					clonedCommand.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
					clonedCommand.Parameters["@poster_hash"].Value = post.PosterID == "Developer" ? "Dev" : post.PosterID;
					clonedCommand.Parameters["@poster_country"].Value = post.CountryCode;
					clonedCommand.Parameters["@exif"].Value = GenerateExifColumnData(post);

					await clonedCommand.ExecuteNonQueryAsync();
				}
			}

			public async Task InsertPosts(ICollection<Post> posts)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					IList<uint> bannedPosts = await rentedConnection.Object.ExecuteOrdinalList<uint>($"SELECT num FROM {Board} WHERE num NOT IN ({string.Join(',', posts.Select(x => x.PostNumber.ToString()))})");

					using (var clonedCommand = InsertQuery.Clone())
					using (var clonedSet = postDataTable.Clone())
					using (var adapter = new MySqlDataAdapter($"SELECT * FROM {Board} WHERE 1 = 0", rentedConnection.Object))
					{
						clonedCommand.Connection = rentedConnection;

						clonedCommand.UpdatedRowSource = UpdateRowSource.None;

						foreach (var post in posts)
						{
							if (bannedPosts.Contains((uint)post.PostNumber))
								continue;

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
							row["name"] = HttpUtility.HtmlDecode(post.Name)?.Trim();
							row["trip"] = post.Trip;
							row["title"] = HttpUtility.HtmlDecode(post.Subject)?.Trim();
							row["comment"] = CleanComment(post.Comment);
							row["sticky"] = post.Sticky == true ? 1 : 0;
							row["locked"] = post.Closed == true ? 1 : 0;
							row["poster_hash"] = post.PosterID == "Developer" ? "Dev" : post.PosterID;
							row["poster_country"] = post.CountryCode;
							row["exif"] = GenerateExifColumnData(post);

							clonedSet.Rows.Add(row);
						}

						adapter.InsertCommand = clonedCommand;
						adapter.UpdateBatchSize = 100;

						adapter.Update(clonedSet);
					}
				}
			}

			private static readonly Regex DrawRegex = new Regex(@"<small><b>Oekaki \s Post<\/b> \s \(Time: \s (.*?), \s Painter: \s (.*?)(?:, \s Source: \s (?<source>.*?))?(?:, \s Animation: \s (?<animation>.*?))?\)<\/small>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
			private static readonly Regex ExifRegex = new Regex(@"<table \s class=""exif""[^>]*>(.*)<\/table>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
			private static readonly Regex ExifDataRegex = new Regex(@"<tr><td>(.*?)<\/td><td>(.*?)</td><\/tr>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline |  RegexOptions.Compiled);
			private static string GenerateExifColumnData(Post post)
			{
				var exifJson = new JObject();

				if (!string.IsNullOrWhiteSpace(post.Comment))
				{
					var exifMatch = ExifRegex.Match(post.Comment);
					if (exifMatch.Success)
					{
						string data = exifMatch.Groups[1].Value;

						data = data.Replace("<tr><td colspan=\"2\"></td></tr><tr>", "");

						var exifDataMatches = ExifDataRegex.Matches(data);

						foreach (Match match in exifDataMatches)
						{
							string key = match.Groups[1].Value;
							string value = match.Groups[2].Value;
							exifJson[key] = value;
						}
					}

					var drawMatch = DrawRegex.Match(post.Comment);
					if (drawMatch.Success)
					{
						exifJson["Time"] = drawMatch.Groups[1].Value;
						exifJson["Painter"] = drawMatch.Groups[2].Value;
						exifJson["Source"] = drawMatch.Groups["source"].Success ? CleanComment(drawMatch.Groups["source"].Value) : null;
					}
				}

				if (post.UniqueIps.HasValue)
					exifJson["uniqueIps"] = post.UniqueIps.Value;

				if (post.Since4Pass.HasValue)
					exifJson["since4pass"] = post.Since4Pass.Value;

				if (post.TrollCountry != null)
					exifJson["trollCountry"] = post.TrollCountry;

				if (exifJson.Count == 0)
					return null;

				return exifJson.ToString(Formatting.None);
			}

			public async Task UpdatePost(Post post, bool deleted)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedCommand = UpdateQuery.Clone())
				{
					clonedCommand.Connection = rentedConnection;

					clonedCommand.Parameters["@comment"].Value = post.Comment;
					clonedCommand.Parameters["@deleted"].Value = deleted ? 1 : 0;
					clonedCommand.Parameters["@media_filename"].Value = post.OriginalFilenameFull;
					clonedCommand.Parameters["@sticky"].Value = post.Sticky == true ? 1 : 0;
					clonedCommand.Parameters["@locked"].Value = post.Closed == true ? 1 : 0;
					clonedCommand.Parameters["@thread_no"].Value = post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber;
					clonedCommand.Parameters["@subnum"].Value = 0;

					await clonedCommand.ExecuteNonQueryAsync();
				}
			}

			public async Task DeletePostOrThread(ulong theadNumber)
			{
				uint currentTimestamp = Utility.GetNewYorkTimestamp(DateTimeOffset.Now);

				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedQuery = DeleteQuery.Clone())
				{
					clonedQuery.Connection = rentedConnection;

					clonedQuery.Parameters["@timestamp_expired"].Value = currentTimestamp;
					clonedQuery.Parameters["@thread_no"].Value = theadNumber;

					await clonedQuery.ExecuteNonQueryAsync();
				}
			}

			public async Task<IEnumerable<KeyValuePair<ulong, int>>> GetHashesOfThread(ulong threadNumber)
			{
				var threadHashes = new List<KeyValuePair<ulong, int>>();

				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedQuery = SelectPostHashQuery.Clone())
				{
					clonedQuery.Connection = rentedConnection;

					clonedQuery.Parameters["@thread_no"].Value = threadNumber; 

					using (var reader = (MySqlDataReader)await clonedQuery.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							uint postNumber = reader.GetUInt32("num");
							bool? closed = reader.GetValue<bool?>("locked");
							bool? sticky = reader.GetValue<bool?>("sticky");
							string comment = reader.GetValue<string>("comment");
							string originalFilename = reader["media_filename"] == DBNull.Value
								? null
								: ((string)reader["media_filename"]).Substring(0, ((string)reader["media_filename"]).LastIndexOf('.'));

							threadHashes.Add(new KeyValuePair<ulong, int>(postNumber, AsagiThreadConsumer.CalculateAsagiHash(sticky, closed, comment, originalFilename)));
						}
					}
				}

				return threadHashes;
			}

			public async Task<MediaInfo> GetMediaInfo(string md5Hash)
			{
				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				using (var clonedQuery = SelectMediaHashQuery.Clone())
				{
					clonedQuery.Connection = rentedConnection;

					clonedQuery.Parameters["@media_hash"].Value = md5Hash;

					using (var reader = (MySqlDataReader)await clonedQuery.ExecuteReaderAsync())
					{
						if (!await reader.ReadAsync())
							//throw new DataException("Expecting image data in database to be created by trigger");
							return null;

						return new MediaInfo(reader.GetValue<string>("media"),
							reader.GetValue<string>("preview_op"),
							reader.GetValue<string>("preview_reply"),
							reader.GetBoolean("banned"));
					}
				}
			}

			public async Task<ulong[]> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, bool archivedOnly)
			{
				int archivedInt = archivedOnly ? 1 : 0;

				using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
				{
					string checkQuery = $"SELECT num FROM `{Board}` WHERE op = 1 AND (locked = {archivedInt} OR deleted = {archivedInt}) AND num IN ({string.Join(',', threadIdsToCheck)});";

					using (var command = new MySqlCommand(checkQuery, rentedConnection))
					using (var reader = await command.ExecuteReaderAsync())
					{
						return (from IDataRecord record in reader
							select (ulong)(uint)record[0]).ToArray();
					}
				}
			}

			private void Dispose(bool disposing)
			{
				AccessSemaphore?.Dispose();
				InsertQuery?.Dispose();
				UpdateQuery?.Dispose();
				DeleteQuery?.Dispose();
				SelectPostHashQuery?.Dispose();
				SelectMediaHashQuery?.Dispose();
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

		private class MediaInfo
		{
			public string MediaFilename { get; set; }
			public bool Banned { get; set; }
			public string PreviewOpFilename { get; set; }
			public string PreviewReplyFilename { get; set; }

			public MediaInfo(string mediaFilename, string previewOpFilename, string previewReplyFilename, bool banned)
			{
				MediaFilename = mediaFilename;
				Banned = banned;
				PreviewOpFilename = previewOpFilename;
				PreviewReplyFilename = previewReplyFilename;
			}
		}
	}
}