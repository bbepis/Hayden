using System;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hayden.WebServer.Logic
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
	public class AdminAccessFilter : Attribute, IAsyncAuthorizationFilter
	{
		public ModeratorRole[] AllowedRoles;

		public AdminAccessFilter(params ModeratorRole[] allowedRoles)
		{
			AllowedRoles = allowedRoles;
		}

		public AdminAccessFilter() 
			: this(ModeratorRole.Janitor,
				ModeratorRole.Moderator,
				ModeratorRole.Developer,
				ModeratorRole.Admin) {}

		public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
		{
			var moderator = await context.HttpContext.GetModeratorAsync();

			if (moderator == null)
			{
				context.Result = new ForbidResult(CookieAuthenticationDefaults.AuthenticationScheme);
				return;
			}

			if (!AllowedRoles.Contains(moderator.Role))
			{
				context.Result = new ForbidResult(CookieAuthenticationDefaults.AuthenticationScheme);
				return;
			}
		}
	}
}
