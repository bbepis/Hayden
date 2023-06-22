using System;
using Hayden.WebServer.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nest;

namespace Hayden.WebServer.Controllers.Api
{
	[ApiModeActionFilter(true)]
	[Route("api")]
	public partial class ApiController : Controller
	{
		protected IOptions<ServerConfig> Config { get; set; }
		protected ElasticClient ElasticClient { get; set; }

		public ApiController(IOptions<ServerConfig> config, IServiceProvider serviceProvider)
		{
			Config = config;
			ElasticClient = serviceProvider.GetService<ElasticClient>();
		}
	}
}
