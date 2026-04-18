using Microsoft.AspNetCore.Mvc;

namespace Hayden.WebServer.Controllers;

[Route("")]
public class FrontendController : Controller
{
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