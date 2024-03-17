using System;
using Hayden.WebServer.Routing;
using Hayden.WebServer.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nest;

namespace Hayden.WebServer.Controllers.Api
{
	[Route("api")]
	public partial class ApiController : Controller
	{
		protected IOptions<ServerConfig> Config { get; set; }
		protected ISearchService SearchService { get; set; }

		public ApiController(IOptions<ServerConfig> config, IServiceProvider serviceProvider)
		{
			Config = config;
			SearchService = serviceProvider.GetService<ISearchService>();
		}
	}
}
