using Hayden.WebServer.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Controllers.Api
{
	[ApiModeActionFilter(true)]
	[Route("api")]
	public partial class ApiController : Controller
	{
		protected IOptions<Config> Config { get; set; }

		public ApiController(IOptions<Config> config)
		{
			Config = config;
		}
	}
}
