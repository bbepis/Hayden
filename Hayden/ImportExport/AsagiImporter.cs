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
using Serilog;

namespace Hayden.ImportExport;

public class AsagiImporter : IImporter
{
	private SourceConfig sourceConfig;
	private DbContextOptions<AsagiDbContext> dbContextOptions;

	private FileSourceType fileSourceType;

	private enum FileSourceType
	{
		None,
		Url,
		File
	}

	public AsagiImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig)
	{
		this.sourceConfig = sourceConfig;

		dbContextOptions = (DbContextOptions<AsagiDbContext>)new DbContextOptionsBuilder<AsagiDbContext>()
			.ConfigureAsagiMysql(sourceConfig.DbConnectionString)
			.Options;

		contextPool = new PooledDbContextFactory<AsagiDbContext>(dbContextOptions);

		if (!consumerConfig.FullImagesEnabled && !consumerConfig.ThumbnailsEnabled)
		{
			fileSourceType = FileSourceType.None;
			Log.Debug("Neither images or thumbnails have been requested, ignoring file source");
		}
		else if (string.IsNullOrWhiteSpace(sourceConfig.ImageboardWebsite))
		{
			fileSourceType = FileSourceType.None;

			if (consumerConfig.FullImagesEnabled || consumerConfig.ThumbnailsEnabled)
			{
				Log.Error($"Consumer config is requesting images and/or thumbnails, and sourceConfig.ImageboardWebsite has not been set to a valid value. Exiting");
				Environment.Exit(1);
			}
		}
		else
		{
			if (Directory.Exists(sourceConfig.ImageboardWebsite))
			{
				fileSourceType = FileSourceType.File;

				//if (!Directory.Exists(sourceConfig.ImageboardWebsite))
				//{
				//	Log.Error($"Interpreted \"{sourceConfig.ImageboardWebsite}\" as a disk path for file CDN but could not find directory. Exiting");
				//	Environment.Exit(1);
				//}
			}
			else if (Uri.TryCreate(sourceConfig.ImageboardWebsite, UriKind.Absolute, out var uri)
				&& !string.IsNullOrWhiteSpace(uri.Scheme))
			{
				fileSourceType = FileSourceType.Url;

				if (!sourceConfig.ImageboardWebsite.EndsWith("/"))
					sourceConfig.ImageboardWebsite += "/";
			}
			else
			{
				Log.Error($"Could not determine if file source CDN was a disk path or a URL: \"{sourceConfig.ImageboardWebsite}\". Exiting");
				Environment.Exit(1);
			}
		}
	}

	private PooledDbContextFactory<AsagiDbContext> contextPool;

	private AsagiDbContext GetDbContext()
		=> contextPool.CreateDbContext();


	public async Task<string[]> GetBoardList()
	{
		await using var dbContext = GetDbContext();

		return dbContext.GetBoardTables();
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

		//if (fileSourceType != FileSourceType.None && images == null)
		//{
		//	Log.Warning($"File table for board {pointer.Board} is missing; files will be unable to be imported");
		//}

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

		string GetMediaPath(AsagiDbContext.AsagiDbPost post, AsagiDbContext.AsagiDbImage image, bool thumbnail)
		{
			if (fileSourceType == FileSourceType.None || image == null)
				return null;

			var asagiFilename = thumbnail ? (image.preview_op ?? image.preview_reply) : image.media;

			if (string.IsNullOrWhiteSpace(asagiFilename))
				return null;

			string path;

			if (fileSourceType == FileSourceType.Url)
			{
				path = $"{sourceConfig.ImageboardWebsite}data/{pointer.Board}/{(thumbnail ? "thumb" : "image")}/{asagiFilename.Substring(0, 4)}/{asagiFilename.Substring(4, 2)}/{asagiFilename}";
			}
			else
			{
				path = Path.Join(sourceConfig.ImageboardWebsite, pointer.Board, thumbnail ? "thumb" : "image", asagiFilename.Substring(0, 4), asagiFilename.Substring(4, 2), asagiFilename);
			}

			return File.Exists(path) ? path : null;
		}

		if (threadPosts.Length == 0)
			return null;

		var isArchived = threadPosts[0].post.locked;
		var expiredTime = threadPosts[0].post.timestamp_expired.GetValueOrDefault() != 0
			? Utility.ConvertNewYorkTimestamp(threadPosts[0].post.timestamp_expired.Value)
			: (DateTimeOffset?)null;

		return new Thread
		{
			ThreadId = pointer.ThreadId,
			ArchivedTime = isArchived ? expiredTime : null,
			DeletedTime = !isArchived ? expiredTime : null,
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
				TimeDeleted = x.post.deleted ? Utility.ConvertNewYorkTimestamp(x.post.timestamp_expired.Value) : (DateTimeOffset?)null,
				OriginalObject = x,
				Media = x.post.media_hash == null
					? Array.Empty<Media>()
					: new[]
					{
						new Media
						{
							Filename = HttpUtility.HtmlDecode(Path.GetFileNameWithoutExtension(x.post.media_filename)),
							FileExtension = Path.GetExtension(x.post.media_filename),
							TimestampedFilename = Path.GetFileNameWithoutExtension(x.post.media_orig),
							Index = 0,
							FileSize = x.post.media_size,
							IsSpoiler = x.post.spoiler,
							ThumbnailExtension = x.image == null ? null : Path.GetExtension(x.image.preview_op ?? x.image.preview_reply),
							Md5Hash = TryConvertBase64(x.post.media_hash),
							FileUrl = GetMediaPath(x.post, x.image, false),
							ThumbnailUrl = GetMediaPath(x.post, x.image, true),
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
			AdditionalMetadata = null
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