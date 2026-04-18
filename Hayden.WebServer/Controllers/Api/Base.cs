using Hayden.WebServer.Config;
using Hayden.WebServer.Search;
using Microsoft.AspNetCore.Mvc;

namespace Hayden.WebServer.Controllers.Api;

[Route("api")]
public partial class ApiController : Controller
{
	protected ConfigOption<ServerSiteConfig> SiteConfig { get; set; }
	protected ConfigOption<ServerDataConfig> DataConfig { get; set; }
	protected ConfigOption<ServerSearchConfig> SearchConfig { get; set; }
	protected ISearchService SearchService { get; set; }

	public ApiController(ConfigOption<ServerSiteConfig> siteConfig, ConfigOption<ServerDataConfig> dataConfig, ConfigOption<ServerSearchConfig> searchConfig, ISearchService searchService)
	{
		SiteConfig = siteConfig;
		DataConfig = dataConfig;
		SearchService = searchService;
		SearchConfig = searchConfig;
	}
}