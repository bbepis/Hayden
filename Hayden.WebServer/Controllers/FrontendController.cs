using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Controllers
{
	[Route("")]
	public class FrontendController : Controller
	{
		protected IOptions<ServerConfig> Config { get; set; }

		public FrontendController(IOptions<ServerConfig> config)
		{
			Config = config;
		}

		[HttpGet]
		[Route("")]
		[Route("board/{board}")]
		[Route("board/{board}/page/{pageNumber}")]
		[Route("{board}/thread/{threadid}")]
		[Route("info")]
		[Route("search")]
		[Route("Login")]
		[Route("Register")]
		public IActionResult Svelte()
		{
			return View("~/View/Svelte.cshtml");
		}
	}
}