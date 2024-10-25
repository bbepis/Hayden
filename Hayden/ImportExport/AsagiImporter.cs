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
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Hayden.ImportExport;

public class AsagiImporter : IImporter
{
	private SourceConfig sourceConfig;
	private DbContextOptions<AsagiDbContext> dbContextOptions;

	public AsagiImporter(SourceConfig sourceConfig)
	{
		this.sourceConfig = sourceConfig;

		dbContextOptions = (DbContextOptions<AsagiDbContext>)new DbContextOptionsBuilder<AsagiDbContext>()
			.UseMySql(sourceConfig.DbConnectionString,
				ServerVersion.AutoDetect(sourceConfig.DbConnectionString), o =>
				{
					o.EnableIndexOptimizedBooleanColumns();
				})
			//.LogTo(s => Program.Log(s))
			.Options
			.WithExtension(new AsagiDbContext.AsagiDbExtension(sourceConfig.DbConnectionString));
		//using var dbContext = GetDbContext();

		//boardTables = dbContext.GetBoardTables().Result;
		
		//var cdnUrl = sourceConfig.ImageboardWebsite;

		//if (!cdnUrl.EndsWith('/'))
		//	cdnUrl += "/";

		contextPool = new PooledDbContextFactory<AsagiDbContext>(dbContextOptions);
	}

	private PooledDbContextFactory<AsagiDbContext> contextPool;

	private AsagiDbContext GetDbContext()
		=> contextPool.CreateDbContext();


	public async Task<string[]> GetBoardList()
	{
		await using var dbContext = GetDbContext();

		return await dbContext.GetBoardTables();
	}

	public async IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null)
	{
		await using var dbContext = GetDbContext();

		var (posts, _, threads, _) = dbContext.GetSets(board);

		if (posts == null)
			throw new Exception($"Tried to retrieve posts from a board that doesn't exist: {board}");

		IAsyncEnumerable<uint> threadIdEnumerable;

		// checking the *_threads table is significantly faster, if it exists
		if (threads != null)
		{
			var query = threads.AsNoTracking();

			if (minId.HasValue)
				query = query.Where(x => x.thread_num >= minId);

			if (maxId.HasValue)
				query = query.Where(x => x.thread_num <= maxId);

			threadIdEnumerable = query.Select(x => x.thread_num).AsAsyncEnumerable();
		}
		else
		{
			var query = posts.AsNoTracking();

			if (minId.HasValue)
				query = query.Where(x => x.thread_num >= minId);

			if (maxId.HasValue)
				query = query.Where(x => x.thread_num <= maxId);

			threadIdEnumerable = query.Select(x => x.thread_num).Distinct().AsAsyncEnumerable();
		}

		await foreach (var threadId in threadIdEnumerable)
		{
			yield return new ThreadPointer(board, threadId);
		}
	}

	public async Task<Thread> RetrieveThread(ThreadPointer pointer)
	{
		await using var dbContext = GetDbContext();

		var (posts, images, _, _) = dbContext.GetSets(pointer.Board);

		var query = posts.AsNoTracking()
			.Where(post => post.thread_num == (uint)pointer.ThreadId && post.subnum == 0);

		(AsagiDbContext.AsagiDbPost post, AsagiDbContext.AsagiDbImage image)[] threadPosts;

		// change depending on dataset capabilities
		if (images != null)
		{
			// left join
			// https://medium.com/@zabavnov/implementation-of-left-outer-join-for-entity-framework-b47469633e2f
			threadPosts = await query.GroupJoin(images,
				post => post.media_id,
				image => image.media_id,
				(post, image) => new { post, image })
			.SelectMany(
				g => g.image.DefaultIfEmpty(),
				(x, g) => new { x.post, image = g })
			.OrderBy(x => x.post.num)
			.ToAsyncEnumerable()
			.Select(x => (x.post, x.image)).ToArrayAsync();
		}
		else
		{
			threadPosts = await query.OrderBy(x => x.num)
				.ToAsyncEnumerable()
				.Select(x => (x, (AsagiDbContext.AsagiDbImage)null)).ToArrayAsync();
		}


		//var threadPosts = await posts
		//	.Where(x => (x.parent == (uint)pointer.ThreadId || x.num == (uint)pointer.ThreadId) && x.subnum == 0)
		//	.OrderBy(x => x.num)
		//	.AsNoTracking()
		//	.ToArrayAsync();

		//string radix = $"{pointer.ThreadId / 100000 % 1000:0000}/{pointer.ThreadId / 1000 % 100:00}";

		if (threadPosts.Length == 0)
			return null;

		return new Thread
		{
			ThreadId = pointer.ThreadId,
			IsArchived = false,
			Title = threadPosts[0].post.title,
			Posts = threadPosts.Select(x => new Post
			{
				PostNumber = x.post.num,
				TimePosted = Utility.ConvertNewYorkTimestamp(x.post.timestamp.Value),
				Author = x.post.name,
				Tripcode = x.post.trip,
				Email = x.post.email,
				Subject = x.post.title,
				ContentRaw = x.post.comment,
				ContentRendered = null,
				ContentType = ContentType.Yotsuba,
				IsDeleted = x.post.deleted,
				OriginalObject = x,
				Media = x.post.media_hash == null
					? Array.Empty<Media>()
					: new[]
					{
						new Media
						{
							Filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(x.post.media_filename)),
							FileExtension = Path.GetExtension(x.post.media_filename),
							Index = 0,
							FileSize = x.post.media_size,
							IsSpoiler = x.post.spoiler,
							//ThumbnailExtension = x.i == null ? null : Path.GetExtension(x.i.preview_op ?? x.i.preview_reply),
							Md5Hash = TryConvertBase64(x.post.media_hash),
							//FileUrl = $"{CdnUrl}data/{pointer.Board}/img/{radix}/{x.media_filename}",
							//ThumbnailUrl = $"{CdnUrl}data/{pointer.Board}/thumb/{radix}/{x.preview}"
							
						}
					},
				AdditionalMetadata = new()
				{
					Capcode = x.post.capcode == "N" || x.post.capcode == null ? null : x.post.capcode,
					CountryCode = x.post.poster_country,
					PosterID = x.post.poster_hash,
					AsagiExif = !string.IsNullOrWhiteSpace(x.post.exif) ? x.post.exif : null
				}
			}).ToArray(),
			AdditionalMetadata = new()
			{
				Locked = threadPosts[0].post.locked,
				TimeExpired = threadPosts[0].post.timestamp_expired,
			}
		};
	}

	private static byte[] TryConvertBase64(string inputHash)
	{
		if (string.IsNullOrWhiteSpace(inputHash))
			return null;

		var md5Hash = new byte[16];

		if (Convert.TryFromBase64String(inputHash, md5Hash, out _))
			return md5Hash;

		return null;
	}
}