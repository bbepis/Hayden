using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Hayden.Api;
using Hayden.Models;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null

namespace Hayden
{
	public class FutabaApi : BaseApi<FutabaThread>
	{
		public string ImageboardWebsite { get; }

		public FutabaApi(string imageboardWebsite)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		private static readonly Regex DateTimeRegex = new(
				@"(?<Year>\d{2})\/(?<Month>\d{2})\/(?<Day>\d{2})\s*\(.\)\s*(?<Hour>\d{2}):(?<Minute>\d{2}):(?<Second>\d{2})(?>\s*ID:\s*(?<UserID>[^\s]+))?",
				RegexOptions.Compiled);

		/// <inheritdoc />
		public override async Task<ApiResponse<FutabaThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var response = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/res/{threadNumber}.htm"), client, modifiedSince, cancellationToken);
			
			if (response.ResponseType != ResponseType.Ok)
				return new ApiResponse<FutabaThread>(response.ResponseType, null);

			var document = response.Data;

			var thread = document.QuerySelector(".thre");

			FutabaPost GetPost(IElement element)
			{
				var post = new FutabaPost();
				var list = element.Children.ToList();
				
				var imageElement = (IHtmlImageElement)element.QuerySelector("a > img");
				post.ThumbnailUrl = imageElement.Source;

				var linkElement = (IHtmlLinkElement)imageElement.ParentElement;
				post.ImageUrl = linkElement.Href;

				var titleElement = element.QuerySelector("span.csb");
				if (titleElement != null)
				{
					// 2chan.net style

					post.Subject = titleElement.TextContent.Trim();
					
					var nameElement = element.QuerySelector("span.cnm");

					// todo
				}

				return post;
			}

			var opPost = GetPost(thread);



			throw new NotImplementedException();

			//return new ApiResponse<FutabaThread>(ResponseType.Ok, thread);
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}
	}
}