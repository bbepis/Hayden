using Hayden.WebServer.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Controllers.Api
{
	[ApiModeActionFilter(true)]
	[Route("api")]
	public partial class ApiController : Controller
	{
		protected IOptions<ServerConfig> Config { get; set; }

		public ApiController(IOptions<ServerConfig> config)
		{
			Config = config;
		}
	}
}
