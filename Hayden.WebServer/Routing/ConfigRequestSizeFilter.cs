using System;
using System.Net;
using Hayden.WebServer.Config;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hayden.WebServer.Routing;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ConfigRequestSizeFilter : Attribute, IAuthorizationFilter, IRequestSizePolicy
{
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		if (context == null)
			throw new ArgumentNullException(nameof(context));

		IRequestSizePolicy requestSizePolicy = context.FindEffectivePolicy<IRequestSizePolicy>();

		if (requestSizePolicy != null && requestSizePolicy != this)
		{
			//this._logger.NotMostEffectiveFilter(this.GetType(), requestSizePolicy.GetType(), typeof(IRequestSizePolicy));
		}

		IHttpMaxRequestBodySizeFeature requestBodySizeFeature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
		if (requestBodySizeFeature == null)
		{
			//this._logger.FeatureNotFound();
		}
		else if (requestBodySizeFeature.IsReadOnly)
		{
			//this._logger.FeatureIsReadOnly();
		}
		else
		{
			var siteConfig = context.HttpContext.RequestServices.GetRequiredService<ConfigOption<ServerSiteConfig>>().Value;

			var maxUploadSize = (long)(1024 * 1024 * (siteConfig.MaxFileUploadSizeMB ?? 4));

			requestBodySizeFeature.MaxRequestBodySize = maxUploadSize;

			if (context.HttpContext.Request.ContentLength > maxUploadSize)
			{
				context.Result = new JsonResult(new
				{
					message = "Request or file is too large"
				})
				{
					StatusCode = (int)HttpStatusCode.RequestEntityTooLarge
				};
			}
			//this._logger.MaxRequestBodySizeSet(this.Bytes.ToString((IFormatProvider)CultureInfo.InvariantCulture));
		}
	}
}