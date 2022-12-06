using System;
using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hayden.WebServer.Routing
{
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
				var config = context.HttpContext.RequestServices.GetRequiredService<IOptions<ServerConfig>>().Value;

				var maxUploadSize = (long)(1024 * 1024 * (config.Settings.MaxFileUploadSizeMB ?? 4));

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
}
