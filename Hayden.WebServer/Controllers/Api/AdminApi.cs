using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Logic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hayden.WebServer.Controllers.Api
{
	public partial class ApiController
	{
		[AdminAccessFilter(ModeratorRole.Janitor, ModeratorRole.Moderator, ModeratorRole.Admin)]
		[HttpPost("moderator/deletepost")]
		public async Task<IActionResult> DeletePost(ushort boardId, ulong postId, bool banImages,
			[FromServices] HaydenDbContext dbContext)
		{
			var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.BoardId == boardId && x.PostId == postId);

			if (post == null)
				return NotFound("Could not find post");

			var board = await dbContext.Boards.FindAsync(boardId);

			var mappings = await dbContext.FileMappings
				.Where(x => x.BoardId == boardId && x.PostId == postId)
				.ToArrayAsync();

			foreach (var mapping in mappings)
				dbContext.Remove(mapping);

            if (banImages && mappings.Length > 0)
			{
				var fileIds = mappings.Select(x => x.FileId).ToArray();

                var files = await dbContext.Files
					.Where(x => fileIds.Contains(x.Id))
					.ToArrayAsync();

                foreach (var file in files)
                {
	                file.FileBanned = true;

	                var fullFilename = Common.CalculateFilename(Config.Value.Data.FileLocation, board.ShortName, Common.MediaType.Image,
		                file.Sha256Hash, file.Extension);
	                var thumbFilename = Common.CalculateFilename(Config.Value.Data.FileLocation, board.ShortName, Common.MediaType.Image,
		                file.Sha256Hash, file.Extension);

					System.IO.File.Delete(fullFilename);
					System.IO.File.Delete(thumbFilename);
                }
			}

			// actually delete the post from the db?
			// flag on board object "PreserveDeleted"
			post.IsDeleted = true;

			await dbContext.SaveChangesAsync();

			return Ok();
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
	}
}