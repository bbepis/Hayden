using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Hayden.ImportExport;

public class HaydenImporter : IImporter
{
	private SourceConfig sourceConfig;
	private DbContextOptions dbContextOptions;
	private Dictionary<string, ushort> boardDictionary;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("HaydenDB");

	public HaydenImporter(SourceConfig sourceConfig)
	{
		this.sourceConfig = sourceConfig;

		var optionsBuilder = new DbContextOptionsBuilder();

		if (sourceConfig.DbConnectionString.StartsWith("Data Source"))
		{
			optionsBuilder.UseSqlite(sourceConfig.DbConnectionString);
		}
		else
		{
			optionsBuilder.UseMySql(sourceConfig.DbConnectionString,
				ServerVersion.AutoDetect(sourceConfig.DbConnectionString),
				y =>
				{
					y.CommandTimeout(86400);
					y.EnableIndexOptimizedBooleanColumns();
				});
		}

		dbContextOptions = optionsBuilder.Options;

		using var dbContext = GetDbContext();

		boardDictionary = dbContext.Boards.ToDictionary(x => x.ShortName, x => x.Id);

		
		this.dbContext = GetDbContext();
	}

	private HaydenDbContext GetDbContext() => new(dbContextOptions);
		
	public async Task<string[]> GetBoardList()
	{
		await using var dbContext = GetDbContext();

		return await dbContext.Boards.Select(x => x.ShortName).ToArrayAsync();
	}

	public async IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null)
	{
		await using var dbContext = GetDbContext();

		var query = dbContext.Threads
			.Where(x => x.BoardId == boardDictionary[board])
			.AsNoTracking();

		if (minId.HasValue)
			query = query.Where(x => x.ThreadId >= (ulong)minId.Value);

		if (maxId.HasValue)
			query = query.Where(x => x.ThreadId <= (ulong)maxId.Value);

		var threadIds = query
			.AsNoTracking()
			.Select(x => x.ThreadId);

		await foreach (var threadId in threadIds.AsAsyncEnumerable())
		{
			yield return new ThreadPointer(board, threadId);
		}
	}

	private HaydenDbContext dbContext;
	public async Task<Thread> RetrieveThread(ThreadPointer pointer)
	{
		var boardId = boardDictionary[pointer.Board];

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		var thread = await dbContext.Threads
			.AsNoTracking()
			.FirstAsync(x => x.BoardId == boardId && x.ThreadId == pointer.ThreadId);

		var getThreadTime = stopwatch.ElapsedMilliseconds;
		stopwatch.Restart();

		var threadPosts = await dbContext.Posts.AsNoTracking()
			.Where(x => x.BoardId == boardId && x.ThreadId == pointer.ThreadId)
			.ToAsyncEnumerable()
			.OrderBy(x => x.PostId)
			.ToArrayAsync();

		var getPostsTime = stopwatch.ElapsedMilliseconds;
		stopwatch.Restart();

		var fileMappings = await (from mapping in dbContext.FileMappings
			join post in dbContext.Posts on new { mapping.BoardId, mapping.PostId } equals new { post.BoardId, post.PostId }
			from file in dbContext.Files.Where(f => f.BoardId == mapping.BoardId && f.Id == mapping.FileId).DefaultIfEmpty()
			where post.BoardId == boardId && post.ThreadId == pointer.ThreadId
			select new { mapping, file }).ToArrayAsync();

		var getFilesTime = stopwatch.ElapsedMilliseconds;

		Logger.Debug($"Thread time: {getThreadTime}ms / Post time: {getPostsTime}ms / Files time: {getFilesTime}ms");
		

		return new Thread
		{
			ThreadId = pointer.ThreadId,
			IsArchived = thread.IsArchived,
			Title = thread.Title,
			Posts = threadPosts.Select(x => new Post
			{
				PostNumber = x.PostId,
				TimePosted = new DateTimeOffset(x.DateTime, TimeSpan.Zero),
				Author = x.Author,
				Tripcode = x.Tripcode,
				Email = x.Email,
				// Subject = x.,
				ContentRaw = x.ContentRaw,
				ContentRendered = x.ContentHtml,
				ContentType = x.ContentType,
				IsDeleted = x.IsDeleted,
				OriginalObject = x,
				Media = fileMappings.Where(y => y.mapping.PostId == x.PostId).Select(m =>
				{
					var mappingAdditionalMetadata = string.IsNullOrWhiteSpace(m.mapping.AdditionalMetadata)
						? null
						: JObject.Parse(m.mapping.AdditionalMetadata);

					byte[] tryConvertBase64(string input) =>
						string.IsNullOrWhiteSpace(input)
							? null
							: Convert.FromBase64String(input);

					return new Media
					{
						Filename = m.mapping.Filename,
						FileExtension = m.file?.Extension ?? mappingAdditionalMetadata?.Value<string>("missing_extension"),
						Index = m.mapping.Index,
						FileSize = m.file?.Size ?? mappingAdditionalMetadata?.Value<uint?>("missing_size"),
						IsSpoiler = m.mapping.IsSpoiler,
						IsDeleted = m.mapping.IsDeleted,
						ThumbnailExtension = m.file?.ThumbnailExtension,
						Sha256Hash = m.file?.Sha256Hash ?? tryConvertBase64(mappingAdditionalMetadata?.Value<string>("missing_sha256hash")),
						Sha1Hash = m.file?.Sha1Hash ?? tryConvertBase64(mappingAdditionalMetadata?.Value<string>("missing_sha1hash")),
						Md5Hash = m.file?.Md5Hash ?? tryConvertBase64(mappingAdditionalMetadata?.Value<string>("missing_md5hash")),
						AdditionalMetadata = string.IsNullOrWhiteSpace(m.mapping.AdditionalMetadata)
							? null
							: JsonConvert.DeserializeObject<Media.MediaAdditionalMetadata>(m.mapping.AdditionalMetadata)
						//FileUrl = $"{CdnUrl}data/{pointer.Board}/img/{radix}/{x.media_filename}",
						//ThumbnailUrl = $"{CdnUrl}data/{pointer.Board}/thumb/{radix}/{x.preview}"
					};
				}).ToArray(),
				AdditionalMetadata = string.IsNullOrWhiteSpace(x.AdditionalMetadata)
					? null
					: JsonConvert.DeserializeObject<Post.PostAdditionalMetadata>(x.AdditionalMetadata)
			}).ToArray()
		};
	}
}