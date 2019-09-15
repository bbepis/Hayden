﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
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

		public AsagiThreadConsumer(AsagiConfig config, string[] boards)
		{
			Config = config;
			ConnectionPool = new MySqlConnectionPool(config.ConnectionString, config.SqlConnectionPoolSize);

			ThumbDownloadLocation = Path.Combine(Config.DownloadLocation, "thumb");
			ImageDownloadLocation = Path.Combine(Config.DownloadLocation, "image");

			foreach (var board in boards)
			{
				CreateTables(board).Wait();
			}

			Directory.CreateDirectory(ThumbDownloadLocation);
			Directory.CreateDirectory(ImageDownloadLocation);
		}

		private ConcurrentDictionary<ThreadHashObject, SortedList<ulong, int>> ThreadHashes { get; } = new ConcurrentDictionary<ThreadHashObject, SortedList<ulong, int>>();

		public async Task<IList<QueuedImageDownload>> ConsumeThread(Thread thread, string board)
		{
			var hashObject = new ThreadHashObject(board, thread.OriginalPost.PostNumber);
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>(thread.Posts.Length);

			async Task ProcessImages(Post post)
			{
				if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
					return; // skip the DB check since we're not even bothering with images

				if (post.FileMd5 != null)
				{
					MediaInfo mediaInfo = await GetMediaInfo(post.FileMd5, board);

					if (mediaInfo?.Banned == true)
					{
						Program.Log($"[Asagi] Post /{board}/{post.PostNumber} contains a banned image; skipping");
						return;
					}

					if (Config.FullImagesEnabled)
					{
						string fullImageName = mediaInfo?.MediaFilename ?? post.TimestampedFilenameFull;

						string radixString = Path.Combine(fullImageName.Substring(0, 4), fullImageName.Substring(4, 2));
						string radixDirectory = Path.Combine(ImageDownloadLocation, board, radixString);
						Directory.CreateDirectory(radixDirectory);

						string fullImageFilename = Path.Combine(radixDirectory, fullImageName);
						string fullImageUrl = $"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}";

						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
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

						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
					}
				}
			}

			if (!ThreadHashes.TryGetValue(hashObject, out var threadHashes))
			{
				// Rebuild hashes from database, if they exist
				
				var hashes = await GetHashesOfThread(hashObject.ThreadId, board);
				
				threadHashes = new SortedList<ulong, int>();

				foreach (var hashPair in hashes)
				{
					threadHashes.Add(hashPair.Key, hashPair.Value);

					var currentPost = thread.Posts.FirstOrDefault(post => post.PostNumber == hashPair.Key);

					if (currentPost != null)
						await ProcessImages(currentPost);
				}

				ThreadHashes.TryAdd(hashObject, threadHashes);
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

						await UpdatePost(post, board, false);

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

					await ProcessImages(post);
				}
			}

			if (threadHashes.Count == 0)
			{
				// We are inserting the thread for the first time.

				await InsertPosts(thread.Posts, board);
			}
			else
			{
				if (postsToAdd.Count > 0)
					await InsertPosts(postsToAdd, board);
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

					await DeletePostOrThread(postNumber, board);

					postNumbersToDelete.Add(postNumber);
				}
			}

			// workaround for not being able to remove from a collection while enumerating it
			foreach (var postNumber in postNumbersToDelete)
				threadHashes.Remove(postNumber, out _);


			threadHashes.TrimExcess();

			if (thread.OriginalPost.Archived == true)
			{
				// We don't need the hashes if the thread is archived, since it will never change
				// If it does change, we can just grab a new set from the database

				ThreadHashes.TryRemove(hashObject, out _);
			}

			return imageDownloads;
		}

		public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			if (deleted)
			{
				await DeletePostOrThread(threadId, board);
			}

			ThreadHashes.TryRemove(new ThreadHashObject(board, threadId), out _);
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


		private static readonly Regex quoteLinkRegex = new Regex(@"<span class=""capcodeReplies""><span style=""font-size: smaller;""><span style=""font-weight: bold;"">(?:Administrator|Moderator|Developer) Repl(?:y|ies):<\/span>.*?<\/span><br><\/span>", RegexOptions.Compiled);
		private static readonly Regex nonPublicTagRegex = new Regex(@"\[(\/?(banned|moot|spoiler|code))]", RegexOptions.Compiled);
		private static readonly Regex toggleExpansionRegex = new Regex(@"<span class=""abbr"">.*?<\/span>", RegexOptions.Compiled);
		private static readonly Regex exifCleanRegex = new Regex(@"<table class=""exif""[^>]*>.*?<\/table>", RegexOptions.Compiled);
		private static readonly Regex drawCleanRegex = new Regex(@"<br><br><small><b>Oekaki Post<\/b>.*?<\/small>", RegexOptions.Compiled);
		private static readonly Regex bannedRegex = new Regex(@"<(?:b|strong) style=""color:\s*red;"">(.*?)<\/(?:b|strong)>", RegexOptions.Compiled);
		private static readonly Regex mootCommentRegex = new Regex(@"<div style=""padding: 5px;margin-left: \.5em;border-color: #faa;border: 2px dashed rgba\(255,0,0,\.1\);border-radius: 2px"">(.*?)</div>", RegexOptions.Compiled);
		private static readonly Regex fortuneRegex = new Regex(@"<span class=""fortune"" style=""color:(.*?)""><br><br><b>(.*?)<\/b><\/span>", RegexOptions.Compiled);
		private static readonly Regex boldRegex = new Regex(@"<(?:b|strong)>(.*?)<\/(?:b|strong)>", RegexOptions.Compiled);
		private static readonly Regex codeTagRegex = new Regex("<pre[^>]*>", RegexOptions.Compiled);
		private static readonly Regex mathTagRegex = new Regex(@"<span class=""math"">(.*?)<\/span>", RegexOptions.Compiled);
		private static readonly Regex mathTag2Regex = new Regex(@"<div class=""math"">(.*?)<\/div>", RegexOptions.Compiled);
		private static readonly Regex quoteTagRegex = new Regex(@"<font class=""unkfunc"">(.*?)<\/font>", RegexOptions.Compiled);
		private static readonly Regex quoteTag2Regex = new Regex(@"<span class=""quote"">(.*?)<\/span>", RegexOptions.Compiled);
		private static readonly Regex quoteTag3Regex = new Regex(@"<span class=""(?:[^""]*)?deadlink"">(.*?)<\/span>", RegexOptions.Compiled);
		private static readonly Regex linkTagRegex = new Regex(@"<a[^>]*>(.*?)<\/a>", RegexOptions.Compiled);
		private static readonly Regex oldSpoilerTagRegex = new Regex(@"<span class=""spoiler""[^>]*>(.*?)<\/span>", RegexOptions.Compiled);
		private static readonly Regex shiftjisTagRegex = new Regex(@"<span class=\""sjis\"">(.*?)<\/span>", RegexOptions.Compiled);
		private static readonly Regex newLineRegex = new Regex(@"<br\s*\/?>", RegexOptions.Compiled);
		public static string CleanComment(string inputComment)
		{
			// Copied wholesale from https://github.com/bibanon/asagi/blob/master/src/main/java/net/easymodo/asagi/YotsubaAbstract.java

			if (string.IsNullOrWhiteSpace(inputComment))
				return string.Empty;

			// SOPA spoilers
			//text = text.replaceAll("<span class=\"spoiler\"[^>]*>(.*?)</spoiler>(</span>)?", "$1");

			// Admin-Mod-Dev quotelinks
			inputComment = quoteLinkRegex.Replace(inputComment, "");
			// Non-public tags
			inputComment = nonPublicTagRegex.Replace(inputComment, "[$1:lit]");
			// Comment too long, also EXIF tag toggle
			inputComment = toggleExpansionRegex.Replace(inputComment, "");
			// EXIF data
			inputComment = exifCleanRegex.Replace(inputComment, "");
			// DRAW data
			inputComment = drawCleanRegex.Replace(inputComment, "");
			// Banned/Warned text
			inputComment = bannedRegex.Replace(inputComment, "[banned]$1[/banned]");
			// moot inputComment
			inputComment = mootCommentRegex.Replace(inputComment, "[moot]$1[/moot]");
			// fortune inputComment
			inputComment = fortuneRegex.Replace(inputComment, "\n\n[fortune color=\"$1\"]$2[/fortune]");
			// bold inputComment
			inputComment = boldRegex.Replace(inputComment, "[b]$1[/b]");
			// code tags
			inputComment = codeTagRegex.Replace(inputComment, "[code]");
			inputComment = inputComment.Replace("</pre>", "[/code]");
			// math tags
			inputComment = mathTagRegex.Replace(inputComment, "[math]$1[/math]");
			inputComment = mathTag2Regex.Replace(inputComment, "[eqn]$1[/eqn]");
			// > implying I'm quoting someone
			inputComment = quoteTagRegex.Replace(inputComment, "$1");
			inputComment = quoteTag2Regex.Replace(inputComment, "$1");
			inputComment = quoteTag3Regex.Replace(inputComment, "$1");
			// Links
			inputComment = linkTagRegex.Replace(inputComment, "$1");
			// old spoilers
			inputComment = oldSpoilerTagRegex.Replace(inputComment, "[spoiler]$1[/spoiler]");
			// ShiftJIS
			inputComment = shiftjisTagRegex.Replace(inputComment, "[shiftjis]$1[/shiftjis]");
			// new spoilers
			inputComment = inputComment.Replace("<s>", "[spoiler]");
			inputComment = inputComment.Replace("</s>", "[/spoiler]");
			// new line/wbr
			inputComment = newLineRegex.Replace(inputComment, "\n");
			inputComment = inputComment.Replace("<wbr>", "");

			return HttpUtility.HtmlDecode(inputComment).Trim();
		}

		#region Sql

		private static readonly string CreateTablesQuery = Utility.GetEmbeddedText("Hayden.Consumers.Asagi.AsagiSchema.sql");

		private async Task CreateTables(string board)
		{
			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				DataTable tables = await rentedConnection.Object.CreateQuery($"SHOW TABLES LIKE '{board}\\_%';").ExecuteTableAsync();

				if (tables.Rows.Count == 0)
				{
					Program.Log($"[Asagi] Creating tables for board /{board}/");

					string formattedQuery = string.Format(CreateTablesQuery, board);

					foreach (var splitString in formattedQuery.Split('$'))
					{
						await rentedConnection.Object.CreateQuery(splitString).ExecuteNonQueryAsync();
					}
				}
			}
		}

		public async Task InsertPosts(ICollection<Post> posts, string board)
		{
			string insertQuerySql = $"INSERT INTO `{board}`"
									+ "  (poster_ip, num, subnum, thread_num, op, timestamp, timestamp_expired, preview_orig, preview_w, preview_h,"
									+ "  media_filename, media_w, media_h, media_size, media_hash, media_orig, spoiler, deleted,"
									+ "  capcode, email, name, trip, title, comment, delpass, sticky, locked, poster_hash, poster_country, exif)"
									+ "    VALUES (0, @num, 0, @thread_num, @op, @timestamp, @timestamp_expired, @preview_orig, @preview_w, @preview_h,"
									+ "      @media_filename, @media_w, @media_h, @media_size, @media_hash, @media_orig, @spoiler, @deleted,"
									+ "      @capcode, @email, @name, @trip, @title, @comment, NULL, @sticky, @locked, @poster_hash, @poster_country, @exif);";

			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			using (var chainedQuery = rentedConnection.Object.CreateQuery(insertQuerySql, true))
			{
				var bannedPosts = await rentedConnection.Object
														.CreateQuery($"SELECT num FROM `{board}` WHERE num NOT IN ({string.Join(',', posts.Select(post => post.PostNumber.ToString()))})")
														.ExecuteScalarListAsync<uint>();
				
				foreach (var post in posts)
				{
					if (bannedPosts.Contains((uint)post.PostNumber))
						continue;

					await chainedQuery
						.SetParam("@num", post.PostNumber)
						.SetParam("@thread_num", post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber)
						.SetParam("@op", post.ReplyPostNumber == 0 ? 1 : 0)
						.SetParam("@timestamp", post.UnixTimestamp)
						.SetParam("@timestamp_expired", 0)
						.SetParam("@preview_orig", post.TimestampedFilename.HasValue ? $"{post.TimestampedFilename}s.jpg" : null)
						.SetParam("@preview_w", post.ThumbnailWidth ?? 0)
						.SetParam("@preview_h", post.ThumbnailHeight ?? 0)
						.SetParam("@media_filename", post.OriginalFilenameFull)
						.SetParam("@media_w", post.ImageWidth ?? 0)
						.SetParam("@media_h", post.ImageHeight ?? 0)
						.SetParam("@media_size", post.FileSize ?? 0)
						.SetParam("@media_hash", post.FileMd5)
						.SetParam("@media_orig", post.TimestampedFilenameFull)
						.SetParam("@spoiler", post.SpoilerImage == true ? 1 : 0)
						.SetParam("@deleted", 0)
						.SetParam("@capcode", post.Capcode?.Substring(0, 1).ToUpperInvariant() ?? "N")
						.SetParam("@email", null)
						.SetParam("@name", HttpUtility.HtmlDecode(post.Name)?.Trim())
						.SetParam("@trip", post.Trip)
						.SetParam("@title", HttpUtility.HtmlDecode(post.Subject)?.Trim())
						.SetParam("@comment", CleanComment(post.Comment))
						.SetParam("@sticky", post.Sticky == true ? 1 : 0)
						.SetParam("@locked", post.Closed == true ? 1 : 0)
						.SetParam("@poster_hash", post.PosterID == "Developer" ? "Dev" : post.PosterID)
						.SetParam("@poster_country", post.CountryCode)
						.SetParam("@exif", GenerateExifColumnData(post))
						.ExecuteNonQueryAsync();
				}

			}
		}

		private static readonly Regex DrawRegex = new Regex(@"<small><b>Oekaki \s Post<\/b> \s \(Time: \s (.*?), \s Painter: \s (.*?)(?:, \s Source: \s (?<source>.*?))?(?:, \s Animation: \s (?<animation>.*?))?\)<\/small>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
		private static readonly Regex ExifRegex = new Regex(@"<table \s class=""exif""[^>]*>(.*)<\/table>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
		private static readonly Regex ExifDataRegex = new Regex(@"<tr><td>(.*?)<\/td><td>(.*?)</td><\/tr>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);

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

		public async Task UpdatePost(Post post, string board, bool deleted)
		{
			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				string sql = $"UPDATE `{board}` SET "
								+ "deleted = @deleted, "
								+ "media_filename = COALESCE(@media_filename, media_filename), "
								+ "sticky = (@sticky OR sticky), locked = (@locked or locked) "
							 + "WHERE num = @thread_no "
								+ "AND subnum = @subnum";

				await rentedConnection.Object.CreateQuery(sql)
									  .SetParam("@comment", post.Comment)
									  .SetParam("@deleted", deleted ? 1 : 0)
									  .SetParam("@media_filename", post.OriginalFilenameFull)
									  .SetParam("@sticky", post.Sticky == true ? 1 : 0)
									  .SetParam("@locked", post.Closed == true ? 1 : 0)
									  .SetParam("@thread_no", post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber)
									  .SetParam("@subnum", 0)
									  .ExecuteNonQueryAsync();
			}
		}

		public async Task DeletePostOrThread(ulong theadNumber, string board)
		{
			uint currentTimestamp = Utility.GetNewYorkTimestamp(DateTimeOffset.Now);

			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				await rentedConnection.Object.CreateQuery($"UPDATE `{board}` SET deleted = 1, timestamp_expired = @timestamp_expired WHERE num = @thread_no AND subnum = 0")
									  .SetParam("@timestamp_expired", currentTimestamp)
									  .SetParam("@thread_no", theadNumber)
									  .ExecuteNonQueryAsync();
			}
		}

		public async Task<IEnumerable<KeyValuePair<ulong, int>>> GetHashesOfThread(ulong threadNumber, string board)
		{
			var threadHashes = new List<KeyValuePair<ulong, int>>();

			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				var table = await rentedConnection.Object.CreateQuery($"SELECT num, locked, sticky, comment, media_filename FROM `{board}` WHERE thread_num = @thread_no")
												  .SetParam("@thread_no", (uint)threadNumber)
												  .ExecuteTableAsync();

				foreach (DataRow row in table.Rows)
				{
					uint postNumber = row.GetValue<uint>("num");
					bool? closed = row.GetValue<bool?>("locked");
					bool? sticky = row.GetValue<bool?>("sticky");
					string comment = row.GetValue<string>("comment");

					string originalFilename = row.GetValue<string>("media_filename")
												 ?.Substring(0, ((string)row["media_filename"]).LastIndexOf('.'));

					threadHashes.Add(new KeyValuePair<ulong, int>(postNumber, CalculateAsagiHash(sticky, closed, comment, originalFilename)));
				}
			}

			return threadHashes;
		}

		private async Task<MediaInfo> GetMediaInfo(string md5Hash, string board)
		{
			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				var chainedQuery = rentedConnection.Object.CreateQuery($"SELECT media, preview_op, preview_reply, banned FROM `{board}_images` WHERE `media_hash` = @media_hash");

				var table = await chainedQuery.SetParam("@media_hash", md5Hash)
											  .ExecuteTableAsync();

				if (table.Rows.Count == 0)
					//throw new DataException("Expecting image data in database to be created by trigger");
					return null;

				var row = table.Rows[0];

				return new MediaInfo(row.GetValue<string>("media"),
					row.GetValue<string>("preview_op"),
					row.GetValue<string>("preview_reply"),
					//row.GetValue<bool>("banned"));
					row.GetValue<ushort>("banned") > 0); // Why is this column SMALLINT and not TINYINT
			}
		}

		public async Task<ICollection<ulong>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly)
		{
			int archivedInt = archivedOnly ? 1 : 0;

			using (var rentedConnection = await ConnectionPool.RentConnectionAsync())
			{
				var chainedQuery = rentedConnection.Object.CreateQuery($"SELECT num FROM `{board}` WHERE op = 1 AND (locked = {archivedInt} OR deleted = {archivedInt}) AND num IN ({string.Join(',', threadIdsToCheck)});");

				return (await chainedQuery.ExecuteScalarListAsync<uint>())
				       .Select(num => (ulong)num)
					   .ToArray();
			}
		}
		
		#endregion

		public void Dispose()
		{
			ConnectionPool.Dispose();
		}

		private struct ThreadHashObject
		{
			public string Board { get; }

			public ulong ThreadId { get; }

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