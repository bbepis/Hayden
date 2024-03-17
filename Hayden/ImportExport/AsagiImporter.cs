using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Hayden.Config;
using Hayden.Consumers.Asagi;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;

namespace Hayden.ImportExport;

public class AsagiImporter : IImporter
{
	private SourceConfig sourceConfig;
	private ConsumerConfig consumerConfig;
	private DbContextOptions<AsagiDbContext> dbContextOptions;

	public AsagiImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig)
	{
		this.sourceConfig = sourceConfig;
		this.consumerConfig = consumerConfig;

		dbContextOptions = new DbContextOptionsBuilder<AsagiDbContext>()
			.UseMySql(sourceConfig.DbConnectionString,
				ServerVersion.AutoDetect(sourceConfig.DbConnectionString), o =>
				{
					o.EnableIndexOptimizedBooleanColumns();
				})
			//.LogTo(s => Program.Log(s))
			.Options;

		//using var dbContext = GetDbContext();

		//boardTables = dbContext.GetBoardTables().Result;

		//CdnUrl = sourceConfig.ImageboardWebsite;

		//if (!CdnUrl.EndsWith('/'))
		//	CdnUrl += "/";
	}

	private AsagiDbContext GetDbContext()
		=> new AsagiDbContext(dbContextOptions,
			new AsagiDbContext.AsagiDbContextOptions { ConnectionString = sourceConfig.DbConnectionString });


	public async Task<string[]> GetBoardList()
	{
		await using var dbContext = GetDbContext();

		return await dbContext.GetBoardTables();
	}

	public async IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null)
	{
		await using var dbContext = GetDbContext();
		
		var query = dbContext.GetSets(board).posts.AsNoTracking();

		if (minId.HasValue)
			query = query.Where(x => x.thread_num >= minId);

		if (maxId.HasValue)
			query = query.Where(x => x.thread_num <= maxId);

		await foreach (var threadId in query.Select(x => x.thread_num).Distinct().AsAsyncEnumerable())
		{
			yield return new ThreadPointer(board, threadId);
		}
	}

	public async Task<Thread> RetrieveThread(ThreadPointer pointer)
	{
		await using var dbContext = GetDbContext();

		var (posts, images, threads, _) = dbContext.GetSets(pointer.Board);

		var threadPosts = await (from p in posts.AsNoTracking()
								 where p.thread_num == (uint)pointer.ThreadId && p.subnum == 0
								 orderby p.num
								 select new { p }).ToArrayAsync();

		//var threadPosts = await posts
		//	.Where(x => (x.parent == (uint)pointer.ThreadId || x.num == (uint)pointer.ThreadId) && x.subnum == 0)
		//	.OrderBy(x => x.num)
		//	.AsNoTracking()
		//	.ToArrayAsync();

		//string radix = $"{pointer.ThreadId / 100000 % 1000:0000}/{pointer.ThreadId / 1000 % 100:00}";

		return new Thread
		{
			ThreadId = pointer.ThreadId,
			IsArchived = false,
			Title = threadPosts[0].p.title,
			Posts = threadPosts.Select(x => new Post
			{
				PostNumber = x.p.num,
				TimePosted = Utility.ConvertNewYorkTimestamp(x.p.timestamp),
				Author = x.p.name,
				Tripcode = x.p.trip,
				Email = x.p.email,
				Subject = x.p.title,
				ContentRaw = x.p.comment,
				ContentRendered = null,
				ContentType = ContentType.Yotsuba,
				IsDeleted = x.p.deleted,
				OriginalObject = x,
				Media = x.p.media_hash == null
					? Array.Empty<Media>()
					: new[]
					{
						new Media
						{
							Filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(x.p.media_filename)),
							FileExtension = Path.GetExtension(x.p.media_filename),
							Index = 0,
							FileSize = x.p.media_size,
							IsSpoiler = x.p.spoiler,
							//ThumbnailExtension = x.i == null ? null : Path.GetExtension(x.i.preview_op ?? x.i.preview_reply),
							Md5Hash = Convert.FromBase64String(x.p.media_hash),
							//FileUrl = $"{CdnUrl}data/{pointer.Board}/img/{radix}/{x.media_filename}",
							//ThumbnailUrl = $"{CdnUrl}data/{pointer.Board}/thumb/{radix}/{x.preview}"
						}
					},
				AdditionalMetadata = new()
				{
					Capcode = x.p.capcode == "N" || x.p.capcode == null ? null : x.p.capcode,
					CountryCode = x.p.poster_country,
					PosterID = x.p.poster_hash,
					AsagiExif = !string.IsNullOrWhiteSpace(x.p.exif) ? x.p.exif : null
				}
			}).ToArray(),
			AdditionalMetadata = new()
			{
				Locked = threadPosts[0].p.locked,
				TimeExpired = threadPosts[0].p.timestamp_expired,
			}
		};
	}
}