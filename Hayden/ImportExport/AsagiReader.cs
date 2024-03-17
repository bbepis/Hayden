using System.Collections.Generic;
using System.Linq;
using Hayden.Consumers.Asagi;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hayden.ImportExport;

internal class AsagiReader
{
	private AsagiDbContext DbContext { get; }

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("AsagiReader");

	public AsagiReader(string connectionString)
	{
		DbContext = new AsagiDbContext(new DbContextOptionsBuilder<AsagiDbContext>()
				.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
				.Options,
			new AsagiDbContext.AsagiDbContextOptions { ConnectionString = connectionString });
	}

	public async IAsyncEnumerable<CommonThread> ReadThreads()
	{
		var boards = await DbContext.GetBoardTables();

		Logger.Information("{boardCount} boards to export", boards.Length);

		foreach (var board in boards)
		{
			var (posts, images, threads, deleted) = DbContext.GetSets(board);

			int totalThreads = await threads.CountAsync();

			Logger.Information("{totalThreads} total threads from /{board}/", totalThreads, board);

			int lastThread = 0;
			int threadCount = 0;

			while (true)
			{
				var query = from p in posts
					from i in images.Where(x => x.media_id == p.media_id).DefaultIfEmpty()
					where threads
						      .Where(x => x.thread_num > lastThread)
						      .OrderBy(x => x.thread_num)
						      .Select(x => x.thread_num).Contains(p.thread_num)
					      && p.subnum == 0
					// orderby p.thread_num, p.num
					select new { p, i };

				var postData = await query.ToListAsync();

				if (postData.Count == 0)
					break;

				var groups = postData.GroupBy(x => x.p.thread_num);

				foreach (var group in groups)
				{
					var op = group.First(x => x.p.num == group.Key);

					yield return new CommonThread
					{
						board = board,
						threadId = group.Key,
						contentType = "Yotsuba",
						sticky = op.p.sticky,
						locked = op.p.locked,
						subject = op.p.title,
						timestamp_expired = op.p.timestamp_expired > 0
							? (ulong)Utility.ConvertNewYorkTimestamp(op.p.timestamp_expired).ToUnixTimeSeconds()
							: null,
						posts = group.OrderBy(x => x.p.num).Select(x => new CommonPost
						{
							postId = x.p.num,
							name = x.p.name,
							tripcode = x.p.trip,
							capcode = x.p.capcode,
							contentRaw = x.p.comment,
							deleted = x.p.deleted,
							timestamp = (ulong)Utility.ConvertNewYorkTimestamp(x.p.timestamp).ToUnixTimeSeconds(),
							timestamp_expired = x.p.timestamp_expired > 0
								? (ulong)Utility.ConvertNewYorkTimestamp(x.p.timestamp_expired).ToUnixTimeSeconds()
								: null,
							email = x.p.email,
							posterCountry = x.p.poster_country,
							posterHash = x.p.poster_hash,
							exif = x.p.exif,

							media = string.IsNullOrWhiteSpace(x.p.media_hash)
								? null
								: new CommonMedia[]
								{
									new CommonMedia
									{
										spoiler = x.p.spoiler,
										banned = x.i.banned,
										imageHeight = x.p.media_h,
										imageWidth = x.p.media_w,
										md5Hash = x.p.media_hash,
										originalFilename = x.p.media_filename,
										size = x.p.media_size
									}
								}
						}).ToArray()
					};
				}

				threadCount += groups.Count();

				Logger.Information("[{board}] {threadsProcessed} / {threadsTotal}", board, threadCount, totalThreads);
			}
		}
	}
}