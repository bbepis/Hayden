using System.IO;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.View;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Controllers
{
	[Route("image")]
	public class ImageController : Controller
	{
		protected IOptions<Config> Config { get; set; }

		public ImageController(IOptions<Config> config)
		{
			Config = config;
		}

		/// <summary>
		/// Serves an image. Ideally this should be handled by nginx serving files directly, as this is slower
		/// </summary>
		[HttpGet]
		[Route("{**path}")]
		public IActionResult ImagePath(string path)
		{
			string fullPath = Path.Combine(Config.Value.FileLocation, path.Replace('/', Path.DirectorySeparatorChar));

			if (!System.IO.File.Exists(fullPath))
				return NotFound();

			return File(System.IO.File.OpenRead(fullPath), path.EndsWith("png") ? "image/png" : "image/jpeg");
		}

		/// <summary>
		/// Serves an image by ID.
		/// </summary>
		[HttpGet]
		[Route("id/{id}")]
		public async Task<IActionResult> ImageId(uint id, [FromServices] HaydenDbContext dbContext)
		{
			var file = await dbContext.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

			if (file == null)
				return NotFound();

			var board = await dbContext.Boards.FindAsync(file.BoardId);

			if (Config.Value.ImagePrefix != null)
			{
				var urls = PostPartialViewModel.GenerateUrls(file, board.ShortName, Config.Value);
				return Redirect(urls.imageUrl);
			}

			string fullPath = Common.CalculateFilename(Config.Value.FileLocation, board.ShortName,
				Common.MediaType.Image, file.Sha256Hash, file.Extension);

			if (!System.IO.File.Exists(fullPath))
				return NotFound();

			return File(System.IO.File.OpenRead(fullPath), fullPath.EndsWith("png") ? "image/png" : "image/jpeg");
		}
	}
}