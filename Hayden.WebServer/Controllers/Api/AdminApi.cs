using Hayden.Consumers.HaydenMysql.DB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Hayden.WebServer.Routing;
using Hayden.WebServer.WebDb;

namespace Hayden.WebServer.Controllers.Api;

public partial class ApiController
{
	[AdminAccessFilter(ModeratorRole.Janitor, ModeratorRole.Moderator, ModeratorRole.Admin)]
	[HttpPost("moderator/deletepost")]
	public async Task<IActionResult> DeletePost(ushort boardId, ulong postId, bool banImages, [FromServices] IDataProvider dataProvider)
	{
		return await dataProvider.DeletePost(boardId, postId, banImages) ? Ok() : BadRequest();
	}

	[AdminAccessFilter(ModeratorRole.Moderator, ModeratorRole.Admin)]
	[HttpPost("moderator/banuser")]
	public async Task<IActionResult> BanUser(ushort boardId, ulong postId, ulong seconds, bool indefinite, string internalReason, string publicReason,
		[FromServices] WebDbContext dbContext)
	{
		//         var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.BoardId == boardId && x.PostId == postId);

		//         if (post == null)
		//             return NotFound("Could not find post");

		//if (post.PosterIP == null)
		//	return UnprocessableEntity("Post does not have an IP address associated with it");

		//dbContext.BannedPosters.Add(new DBBannedPoster
		//{
		//	IPAddress = post.PosterIP,
		//	Reason = internalReason,
		//	PublicReason = publicReason,
		//	TimeBannedUTC = DateTime.UtcNow,
		//	TimeUnbannedUTC = indefinite ? null : DateTime.UtcNow + TimeSpan.FromSeconds(seconds)
		//});

		//await dbContext.SaveChangesAsync();

		return Ok();
	}

	private class ReportedPostInfo
	{
		public JsonPostModel Post { get; set; }
		public DBBoard Board { get; set; }

		public ReportInfo[] Reports { get; set; }

		public class ReportInfo
		{
			public uint Id { get; set; }
			public int Severity { get; set; }
			public string IPAddress { get; set; }
			public string Reason { get; set; }
		}
	}

	[AdminAccessFilter(ModeratorRole.Moderator, ModeratorRole.Admin)]
	[HttpGet("moderator/getreports")]
	public async Task<IActionResult> GetReports([FromQuery] int page,
		[FromServices] IServiceProvider serviceProvider,
		[FromServices] IDataProvider dataProvider)
	{
		const int pageSize = 20;

		using var serviceScope = serviceProvider.CreateScope();

		var dbContext = serviceScope.ServiceProvider.GetService<WebDbContext>();

		// we actually want to grab the top 20 posts, so we have to do some fucky calculations
		var reportedPosts = await dbContext.Reports
			.Where(x => !x.Resolved)
			.OrderByDescending(x => x.Category)
			.ThenByDescending(x => x.TimeReported)
			.Select(x => new { x.BoardId, x.PostId })
			.Distinct()
			.Skip((page - 1) * pageSize).Take(pageSize)
			.Join(dbContext.Reports, post => post, report => new { report.BoardId, report.PostId },
				(post, report) => report)
			.ToListAsync();

		var boards = await dataProvider.GetBoardInfo();

		var reports = new List<ReportedPostInfo>();

		foreach (var grouping in reportedPosts.GroupBy(x => (x.BoardId, x.PostId)))
		{
			var board = boards.First(x => x.Id == grouping.Key.BoardId);

			var post = await dataProvider.GetPost(board.ShortName, grouping.Key.PostId);

			reports.Add(new ReportedPostInfo()
			{
				Post = post,
				Board = board,
				Reports = grouping.Select(y => new ReportedPostInfo.ReportInfo
				{
					Id = y.Id,
					IPAddress = y.IPAddress,
					Reason = y.Reason,
					Severity = (int)y.Category
				}).ToArray()
			});
		}

		return Ok(reports);
	}

	[AdminAccessFilter(ModeratorRole.Moderator, ModeratorRole.Admin)]
	[HttpPost("moderator/markreportresolved")]
	public async Task<IActionResult> MarkReportResolved(int reportId,
		[FromServices] IServiceProvider serviceProvider
	)
	{
		using var serviceScope = serviceProvider.CreateScope();

		var dbContext = serviceScope.ServiceProvider.GetService<WebDbContext>();

		var report = await dbContext.Reports.FindAsync(reportId);

		if (report == null)
			return BadRequest();

		report.Resolved = true;
		dbContext.Update(report);
		await dbContext.SaveChangesAsync();

		return Ok();
	}
}