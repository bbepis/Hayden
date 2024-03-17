using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Logic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Hayden.WebServer.Controllers.Api
{
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
            [FromServices] HaydenDbContext dbContext)
		{
            var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.BoardId == boardId && x.PostId == postId);

            if (post == null)
                return NotFound("Could not find post");

			if (post.PosterIP == null)
				return UnprocessableEntity("Post does not have an IP address associated with it");

			dbContext.BannedPosters.Add(new DBBannedPoster
			{
				IPAddress = post.PosterIP,
				Reason = internalReason,
				PublicReason = publicReason,
				TimeBannedUTC = DateTime.UtcNow,
				TimeUnbannedUTC = indefinite ? null : DateTime.UtcNow + TimeSpan.FromSeconds(seconds)
			});

			await dbContext.SaveChangesAsync();

			return Ok();
		}

		private class ReportedPostInfo
		{
			public ushort BoardId { get; set; }
			public ulong PostId { get; set; }

			public ReportInfo[] Reports { get; set; }

			public class ReportInfo
			{
				public string Severity { get; set; }
				public string IPAddress { get; set; }
				public string Reason { get; set; }
			}
		}

		[AdminAccessFilter(ModeratorRole.Moderator, ModeratorRole.Admin)]
		[HttpPost("moderator/getreports")]
		public async Task<IActionResult> GetReports(int page,
			[FromServices] IServiceProvider serviceProvider
			)
		{
			const int pageSize = 20;

			using var serviceScope = serviceProvider.CreateScope();

			var dbContext = serviceScope.ServiceProvider.GetService<HaydenDbContext>();

			if (dbContext == null)
				return UnprocessableEntity(new { error = "Reports require Hayden database structure" });

			// we actually want to grab the top 20 posts, so we have to do some fucky calculations
			var reportedPosts = await dbContext.Reports
				.Where(x => !x.Resolved)
				.OrderByDescending(x => x.Category)
				.ThenByDescending(x => x.TimeReported)
				.Select(x => new { x.BoardId, x.PostId })
				.Distinct()
				.Skip(page * pageSize).Take(pageSize)
				.Join(dbContext.Reports, post => post, report => new { report.BoardId, report.PostId },
					(post, report) => report)
				.ToListAsync();

			var reportList = reportedPosts
				.GroupBy(x => new { x.BoardId, x.PostId })
				.Select(x => new ReportedPostInfo()
				{
					BoardId = x.Key.BoardId,
					PostId = x.Key.PostId,
					Reports = x.Select(y => new ReportedPostInfo.ReportInfo()
					{
						IPAddress = y.IPAddress,
						Reason = y.Reason,
						Severity = y.Category.ToString()
					}).ToArray()
				}).ToArray();

            return Ok(reportList);
		}

		[AdminAccessFilter(ModeratorRole.Moderator, ModeratorRole.Admin)]
		[HttpPost("moderator/markreportresolved")]
		public async Task<IActionResult> MarkReportResolved(int reportId,
			[FromServices] IServiceProvider serviceProvider
			)
		{
			using var serviceScope = serviceProvider.CreateScope();

			var dbContext = serviceScope.ServiceProvider.GetService<HaydenDbContext>();

			if (dbContext == null)
				return UnprocessableEntity(new { error = "Reports require Hayden database structure" });

			var report = await dbContext.Reports.FindAsync(reportId);

			if (report == null)
				return BadRequest();

			report.Resolved = true;
			dbContext.Update(report);
			await dbContext.SaveChangesAsync();

            return Ok();
		}
	}
}