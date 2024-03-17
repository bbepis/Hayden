using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Hayden.Consumers;

/// <summary>
/// A thread consumer for the Asagi MySQL backend.
/// </summary>
public class AsagiNewThreadConsumer : IThreadConsumer
{
	private ConsumerConfig ConsumerConfig { get; }

	private MySqlConnectionPool ConnectionPool { get; }

	private ICollection<string> Boards { get; }

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Asagi");

	public AsagiNewThreadConsumer(ConsumerConfig consumerConfig, SourceConfig sourceConfig)
	{
		ConsumerConfig = consumerConfig;
		ConnectionPool = new MySqlConnectionPool(consumerConfig.ConnectionString, consumerConfig.SqlConnectionPoolSize ?? 4);
		Boards = sourceConfig.Boards.Keys;
	}

	public async Task InitializeAsync()
	{
		foreach (var board in Boards)
		{
			await CreateTables(board);
		}
	}

	public Task CommitAsync() => Task.CompletedTask;

	/// <inheritdoc/>
	public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
	{
		if (!(threadUpdateInfo.Thread.OriginalObject is YotsubaThread))
			throw new InvalidOperationException(
				"AsagiThreadConsumer can only accept threads of type YotsubaThread");

		List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();
		string board = threadUpdateInfo.ThreadPointer.Board;

		async Task ProcessImages(YotsubaPost post)
		{
			if (!ConsumerConfig.FullImagesEnabled && !ConsumerConfig.ThumbnailsEnabled)
				return; // skip the DB check since we're not even bothering with images

			if (post.FileMd5 != null)
			{
				MediaInfo mediaInfo = await GetMediaInfo(post.FileMd5, board);

				if (mediaInfo?.Banned == true)
				{
					Logger.Debug("Post /{board}/{postNumber} contains a banned image; skipping", board, post.PostNumber);
					return;
				}

				string fullImageFilename = null, thumbFilename = null;
				Uri imageUrl = null, thumbUrl = null;

				if (ConsumerConfig.FullImagesEnabled)
				{
					string fullImageName = mediaInfo?.MediaFilename ?? post.TimestampedFilenameFull;

					string radixString = Path.Combine(fullImageName.Substring(0, 4), fullImageName.Substring(4, 2));
					string radixDirectory = Path.Combine(ConsumerConfig.DownloadLocation, board, "image", radixString);
					Directory.CreateDirectory(radixDirectory);

					fullImageFilename = Path.Combine(radixDirectory, fullImageName);
					imageUrl = new Uri($"https://i.4cdn.org/{board}/{post.TimestampedFilenameFull}");
				}

				if (ConsumerConfig.ThumbnailsEnabled)
				{
					string thumbImageName;

					if (post.ReplyPostNumber == 0) // is OP
						thumbImageName = mediaInfo?.PreviewOpFilename ?? $"{post.TimestampedFilename}s.jpg";
					else
						thumbImageName = mediaInfo?.PreviewReplyFilename ?? $"{post.TimestampedFilename}s.jpg";

					string radixString = Path.Combine(thumbImageName.Substring(0, 4), thumbImageName.Substring(4, 2));
					string radixDirectory = Path.Combine(ConsumerConfig.DownloadLocation, board, "thumb", radixString);
					Directory.CreateDirectory(radixDirectory);

					thumbFilename = Path.Combine(radixDirectory, thumbImageName);
					thumbUrl = new Uri($"https://i.4cdn.org/{board}/{post.TimestampedFilename}s.jpg");
				}

				imageDownloads.Add(new QueuedImageDownload(imageUrl, thumbUrl, new()
				{
					["imageFilename"] = fullImageFilename,
					["thumbFilename"] = thumbFilename
				}));
			}
		}

		var yotsubaThread = (YotsubaThread)threadUpdateInfo.Thread.OriginalObject;

		await UpdatePostExif(yotsubaThread.OriginalPost, board);

		var newPosts = threadUpdateInfo.NewPosts.Select(x => (YotsubaPost)x.OriginalObject).ToArray();

		foreach (var post in newPosts)
		{
			await ProcessImages(post);

			Logger.Verbose("Post /{board}/{threadNumber}/{postNumber} inserted", board, threadUpdateInfo.ThreadPointer.ThreadId, post.PostNumber);
		}

		await InsertPosts(newPosts, board);

		foreach (var post in threadUpdateInfo.UpdatedPosts)
		{
			Logger.Debug("Post /{board}/{threadNumber}/{postNumber} has been modified", board, threadUpdateInfo.ThreadPointer.ThreadId, post.PostNumber);

			await UpdatePost((YotsubaPost)post.OriginalObject, board, false);
		}

		foreach (var postNumber in threadUpdateInfo.DeletedPosts)
		{
			Logger.Debug("Post /{board}/{threadNumber}/{postNumber} has been deleted", board, threadUpdateInfo.ThreadPointer.ThreadId, postNumber);

			await SetUntracked(postNumber, board, true);
		}

		return imageDownloads;
	}

	public async Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
	{
		if (!queuedImageDownload.TryGetProperty("imageFilename", out string imageFilename)
		    || !queuedImageDownload.TryGetProperty("thumbFilename", out string thumbFilename))
		{
			throw new InvalidOperationException("Queued image download did not have the required properties");
		}

		if (imageTempFilename != null)
			File.Move(imageTempFilename, imageFilename);

		if (thumbTempFilename != null)
			File.Move(thumbTempFilename, thumbFilename);
	}

	/// <inheritdoc/>
	public async Task ThreadUntracked(ulong threadId, string board, bool deleted)
	{
		await SetUntracked(threadId, board, deleted);
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

	/// <summary>
	/// Cleans a comment, and converts it to it's relevant BBCode used in FoolFuuka.
	/// </summary>
	/// <param name="inputComment">The raw comment to clean.</param>
	/// <returns>A cleaned comment.</returns>
	public static string CleanComment(string inputComment)
	{
		if (string.IsNullOrWhiteSpace(inputComment))
			return string.Empty;

		if (!inputComment.Contains('<') && !inputComment.Contains('['))
		{
			if (!inputComment.Contains('&'))
			{
				// No HTML encoding has been done at all
				return inputComment.Trim();
			}

			// Only escaping has been done
			return HttpUtility.HtmlDecode(inputComment).Trim();
		}

		// Copied wholesale from https://github.com/bibanon/asagi/blob/master/src/main/java/net/easymodo/asagi/YotsubaAbstract.java

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

	/// <summary>
	/// Runs a SQL query that creates the required tables for the board. Does nothing if they already exist.
	/// </summary>
	/// <param name="board">The board to create tables for.</param>
	private async Task CreateTables(string board)
	{
		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		DataTable tables = await rentedConnection.Object.CreateQuery($"SHOW TABLES LIKE '{board}\\_%';").ExecuteTableAsync();

		if (tables.Rows.Count == 0)
		{
			Logger.Information("Creating tables for board /{board}/", board);

			string formattedQuery = string.Format(CreateTablesQuery, board);

			foreach (var splitString in formattedQuery.Split('$'))
			{
				await rentedConnection.Object.CreateQuery(splitString).ExecuteNonQueryAsync();
			}
		}
	}

	/// <summary>
	/// Inserts a collection of posts into their relevant table on the database.
	/// </summary>
	/// <param name="posts">The posts to insert.</param>
	/// <param name="board">The board of the posts.</param>
	public async Task InsertPosts(ICollection<YotsubaPost> posts, string board)
	{
		if (posts.Count == 0)
			return;

		string insertQuerySql = $"INSERT INTO `{board}`"
		                        + "  (poster_ip, num, subnum, thread_num, op, timestamp, timestamp_expired, preview_orig, preview_w, preview_h,"
		                        + "  media_filename, media_w, media_h, media_size, media_hash, media_orig, spoiler, deleted,"
		                        + "  capcode, email, name, trip, title, comment, delpass, sticky, locked, poster_hash, poster_country, exif)"
		                        + "    VALUES (0, @num, 0, @thread_num, @op, @timestamp, @timestamp_expired, @preview_orig, @preview_w, @preview_h,"
		                        + "      @media_filename, @media_w, @media_h, @media_size, @media_hash, @media_orig, @spoiler, @deleted,"
		                        + "      @capcode, @email, @name, @trip, @title, @comment, NULL, @sticky, @locked, @poster_hash, @poster_country, @exif);";

		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		using var chainedQuery = rentedConnection.Object.CreateQuery(insertQuerySql, true);

		var bannedPosts = await rentedConnection.Object
			.CreateQuery($"SELECT num FROM `{board}` WHERE num NOT IN ({string.Join(',', posts.Select(post => post.PostNumber.ToString()))})")
			.ExecuteScalarListAsync<uint>();

		foreach (var post in posts)
		{
			if (bannedPosts.Contains((uint)post.PostNumber))
				continue;

			string capcode = post.Capcode?.Substring(0, 1).ToUpperInvariant() ?? "N";
			if (post.Capcode?.Equals("Manager", StringComparison.OrdinalIgnoreCase) == true)
				capcode = "G";

			string posterTrip = post.Trip;
			if (post.Trip?.Equals("Developer", StringComparison.OrdinalIgnoreCase) == true)
				posterTrip = "Dev";

			await chainedQuery
				.SetParam("@num", post.PostNumber)
				.SetParam("@thread_num", post.ReplyPostNumber != 0 ? post.ReplyPostNumber : post.PostNumber)
				.SetParam("@op", post.ReplyPostNumber == 0 ? 1 : 0)
				.SetParam("@timestamp", Utility.GetNewYorkTimestamp(Utility.ConvertGMTTimestamp(post.UnixTimestamp)))
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
				.SetParam("@capcode", capcode)
				.SetParam("@email", null)
				.SetParam("@name", HttpUtility.HtmlDecode(post.Name)?.Trim())
				.SetParam("@trip", posterTrip)
				.SetParam("@title", HttpUtility.HtmlDecode(post.Subject)?.Trim())
				.SetParam("@comment", CleanComment(post.Comment))
				.SetParam("@sticky", post.Sticky == true ? 1 : 0)
				.SetParam("@locked", (post.Closed == true) && (post.Archived != true) ? 1 : 0)
				.SetParam("@poster_hash", post.PosterID == "Developer" ? "Dev" : post.PosterID)
				.SetParam("@poster_country", post.CountryCode)
				.SetParam("@exif", GenerateExifColumnData(post))
				.ExecuteNonQueryAsync();
		}
	}

	private static readonly Regex DrawRegex = new Regex(@"<small><b>Oekaki \s Post<\/b> \s \(Time: \s (.*?), \s Painter: \s (.*?)(?:, \s Source: \s (?<source>.*?))?(?:, \s Animation: \s (?<animation>.*?))?\)<\/small>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
	private static readonly Regex ExifRegex = new Regex(@"<table \s class=""exif""[^>]*>(.*)<\/table>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
	private static readonly Regex ExifDataRegex = new Regex(@"<tr><td>(.*?)<\/td><td>(.*?)</td><\/tr>", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);

	/// <summary>
	/// Generates the string for the Exif column in an Asagi schema database, from a post.
	/// </summary>
	/// <param name="post">The post to generate the string from.</param>
	private static string GenerateExifColumnData(YotsubaPost post)
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

		if (post.BoardFlagName != null)
			exifJson["trollCountry"] = post.BoardFlagName;

		if (exifJson.Count == 0)
			return null;

		return exifJson.ToString(Formatting.None);
	}

	/// <summary>
	/// Updates an existing post in the database.
	/// </summary>
	/// <param name="post">The post to update.</param>
	/// <param name="board">The board that the post belongs to.</param>
	/// <param name="deleted">True if the post was explicitly deleted, false if it was not.</param>
	public async Task UpdatePost(YotsubaPost post, string board, bool deleted)
	{
		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		string sql = $"UPDATE `{board}` SET "
		             + "comment = @comment, "
		             + "deleted = @deleted, "
		             + "media_filename = COALESCE(@media_filename, media_filename), "
		             + "sticky = @sticky, "
		             + "locked = @locked OR locked "
		             + "WHERE num = @no "
		             + "AND subnum = @subnum";

		await rentedConnection.Object.CreateQuery(sql)
			.SetParam("@comment", CleanComment(post.Comment))
			.SetParam("@deleted", deleted ? 1 : 0)
			.SetParam("@media_filename", post.OriginalFilenameFull)
			.SetParam("@sticky", post.Sticky == true ? 1 : 0)
			.SetParam("@locked", post.Archived != true && post.Closed == true ? 1 : 0)
			.SetParam("@no", post.PostNumber)
			.SetParam("@subnum", 0)
			.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// Updates the exif column of a post in the database. Generally only done for the OP post, when the thread has been updated.
	/// </summary>
	/// <param name="post">The post to update.</param>
	/// <param name="board">The board of the post.</param>
	public async Task UpdatePostExif(YotsubaPost post, string board)
	{
		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		string sql = $"UPDATE `{board}` SET "
		             + "exif = @exif "
		             + "WHERE num = @post_no "
		             + "AND subnum = 0";

		await rentedConnection.Object.CreateQuery(sql)
			.SetParam("@exif", GenerateExifColumnData(post))
			.SetParam("@post_no", post.PostNumber)
			.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// Sets a post tracking status in the database.
	/// </summary>
	/// <param name="postNumber">The number of the post.</param>
	/// <param name="board">The board that the post belongs to.</param>
	/// <param name="deleted">True if the post was explicitly deleted, false if not.</param>
	public async Task SetUntracked(ulong postNumber, string board, bool deleted)
	{
		uint currentTimestamp = Utility.GetNewYorkTimestamp(DateTimeOffset.Now);

		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		await rentedConnection.Object.CreateQuery($"UPDATE `{board}` SET deleted = @deleted, timestamp_expired = @timestamp_expired WHERE num = @post_no AND subnum = 0")
			.SetParam("@timestamp_expired", currentTimestamp)
			.SetParam("@post_no", postNumber)
			.SetParam("@deleted", deleted ? 1 : 0)
			.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// Retrieves media info for a specific image hash from the database.
	/// </summary>
	/// <param name="md5Hash">A base64 encoded string of the MD5 hash.</param>
	/// <param name="board">The board that the media belongs to.</param>
	private async Task<MediaInfo> GetMediaInfo(string md5Hash, string board)
	{
		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

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

	/// <inheritdoc/>
	public async Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, MetadataMode metadataMode = MetadataMode.FullHashMetadata, bool excludeDeletedPosts = true)
	{
		int archivedInt = archivedOnly ? 1 : 0;

		await using var rentedConnection = await ConnectionPool.RentConnectionAsync();

		string query;

		if (metadataMode == MetadataMode.FullHashMetadata)
		{
			query = $@"
				SELECT TABLE1.num, MAX(TABLE2.timestamp), MAX(TABLE1.timestamp_expired)
				FROM `{board}` TABLE1
					INNER JOIN `{board}` TABLE2 ON TABLE2.thread_num = TABLE1.num
				WHERE TABLE1.op = 1
					AND (
						({archivedInt} = 1 AND TABLE1.timestamp_expired != 0)
						OR ({archivedInt} = 0 AND TABLE1.timestamp_expired = 0)
						OR TABLE1.deleted = {archivedInt}
					)
					AND TABLE1.num IN ({string.Join(',', threadIdsToCheck)})
				GROUP BY TABLE1.num";

			var table = await rentedConnection.Object.CreateQuery(query).ExecuteTableAsync();

			var items = new List<ExistingThreadInfo>();

			foreach (DataRow row in table.Rows)
			{
				var hashes = new List<(ulong PostId, uint PostHash)>();

				var rows = rentedConnection.Object
					.CreateQuery($"SELECT num, comment, spoiler, locked, media_filename " +
					             $"FROM `{board}` " +
					             $"WHERE thread_num = @threadid {(excludeDeletedPosts ? "AND deleted = 0" : "")}")
					.SetParam("@threadid", (uint)row[0])
					.ExecuteRowsAsync();

				await foreach (var postRow in rows)
				{
					string filenameNoExt = postRow.GetValue<string>("media_filename");
					filenameNoExt = filenameNoExt?.Substring(0, filenameNoExt.IndexOf('.'));

					var hash = CalculatePostHash(postRow.GetValue<string>("comment"), postRow.GetValue<bool>("spoiler"),
						filenameNoExt, postRow.GetValue<bool>("locked"));

					hashes.Add(((uint)postRow[0], hash));
				}

				items.Add(new ExistingThreadInfo((uint)row[0], (uint)Convert.ChangeType(row[0], typeof(uint)) != 0, Utility.ConvertNewYorkTimestamp((uint)row[1]).UtcDateTime, hashes));
			}

			return items;
		}
		else if (metadataMode == MetadataMode.ThreadIdAndPostId)
		{
			// TODO: this could very likely be rewritten better
			query = $@"
				SELECT TABLE1.num, TABLE2.num
				FROM `{board}` TABLE1
					INNER JOIN `{board}` TABLE2 ON TABLE2.thread_num = TABLE1.num
				WHERE TABLE1.op = 1
					AND (
						({archivedInt} = 1 AND TABLE1.timestamp_expired != 0)
						OR ({archivedInt} = 0 AND TABLE1.timestamp_expired = 0)
						OR TABLE1.deleted = {archivedInt}
					)
					AND TABLE1.num IN ({string.Join(',', threadIdsToCheck)})
				GROUP BY TABLE1.num";

			var table = await rentedConnection.Object.CreateQuery(query).ExecuteTableAsync();

			var items = new List<ExistingThreadInfo>();
			var rowEnumerable = table.Rows.Cast<DataRow>().Select(x => ((uint)x[0], (uint)x[1]));

			foreach (var group in rowEnumerable.GroupBy(x => x.Item1, x => x.Item2))
			{
				items.Add(new ExistingThreadInfo(group.Key, false, DateTimeOffset.MinValue, group.Select(x => ((ulong)x, (uint)0)).ToArray()));
			}

			return items;
		}
		else
		{
			query = $@"SELECT num
						   FROM `{board}`
						   WHERE op = 1
							 AND (
							 	({archivedInt} = 1 AND timestamp_expired != 0)
							 	OR ({archivedInt} = 0 AND timestamp_expired = 0)
							 	OR deleted = {archivedInt}
							 )
							 AND num IN ({string.Join(',', threadIdsToCheck)})";

			var chainedQuery = rentedConnection.Object.CreateQuery(query);

			var items = new List<ExistingThreadInfo>();

			await foreach (var row in chainedQuery.ExecuteRowsAsync())
			{
				items.Add(new ExistingThreadInfo((uint)row[0]));
			}

			return items;
		}
	}

	#endregion

	public static uint CalculatePostHash(string cleanedPostComment, bool? spoilerImage, string originalFilenameNoExt, bool? closed)
	{
		// Null bool? values should evaluate to false everywhere
		static int EvaluateNullableBool(bool? value)
		{
			return value.HasValue
				? (value.Value ? 1 : 2)
				: 2;
		}

		// The HTML content of a post can change due to public warnings and bans.
		uint hashCode = Utility.FNV1aHash32(cleanedPostComment);

		// Attached files can be removed, and have their spoiler status changed
		Utility.FNV1aHash32(EvaluateNullableBool(spoilerImage), ref hashCode);
		Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

		// The OP of a thread can have numerous properties change.
		// As such, these properties are only considered mutable for OPs (because that's the only place they can exist) and immutable for replies.
		Utility.FNV1aHash32(EvaluateNullableBool(closed), ref hashCode);

		return hashCode;
	}

	/// <inheritdoc />
	public uint CalculateHash(Post post)
	{
		if (!(post.OriginalObject is YotsubaPost))
			throw new InvalidOperationException("AsagiThreadConsumer can only accept posts of type YotsubaPost");

		var yotsubaPost = (YotsubaPost)post.OriginalObject;

		return CalculatePostHash(CleanComment(yotsubaPost.Comment), yotsubaPost.SpoilerImage, yotsubaPost.OriginalFilename, yotsubaPost.Closed);
	}

	/// <summary>
	/// Disposes the object.
	/// </summary>
	public void Dispose()
	{
		ConnectionPool.Dispose();
	}

	/// <summary>
	/// Information relating to a single media object tracked in the database.
	/// </summary>
	private class MediaInfo
	{
		/// <summary>
		/// The timestamped filename of the media.
		/// </summary>
		public string MediaFilename { get; set; }

		/// <summary>
		/// True if the media should be forbidden from downloading, false if otherwise.
		/// </summary>
		public bool Banned { get; set; }

		// These two properties are designed weirdly. They're the same thing, but only one is used depending on whether the post is the OP or not.

		/// <summary>
		/// The preview file filename. Only used when the post that the media is attached to is an OP post
		/// </summary>
		public string PreviewOpFilename { get; set; }

		/// <summary>
		/// The preview file filename. Only used when the post that the media is attached to is a non-OP post
		/// </summary>
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