using System.IO;
using Microsoft.AspNetCore.Mvc;
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
			string fullPath = Path.Combine(Config.Value.FileLocation, path);

			if (!System.IO.File.Exists(fullPath))
				return NotFound();

			return File(System.IO.File.OpenRead(fullPath), path.EndsWith("png") ? "image/png" : "image/jpeg");
		}
	}
}