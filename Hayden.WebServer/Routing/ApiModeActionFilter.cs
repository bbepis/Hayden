using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Routing
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiModeActionFilter : ActionFilterAttribute
    {
		public bool ApiMode { get; set; }

		public ApiModeActionFilter(bool apiMode)
		{
			ApiMode = apiMode;
		}

		public override void OnActionExecuting(ActionExecutingContext context)
		{
			var config = context.HttpContext.RequestServices.GetService<IOptions<ServerConfig>>().Value;

			if (config.ApiMode && !ApiMode)
			{
				// we're in API mode, and this is a frontend controller
				// return svelte frontend
				context.Result = ((Controller)context.Controller).View("~/View/Svelte.cshtml");
				return;
			}
			
			if (!config.ApiMode && ApiMode)
			{
				// we're not in API mode, and this is an API controller
				// disable this controller
				context.Result = new NotFoundResult();
				return;
			}

			base.OnActionExecuting(context);
		}
	}
}
