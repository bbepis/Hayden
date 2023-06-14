using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public class TinyboardApi : BaseApi<IHtmlDocument>
	{
		public string ImageboardWebsite { get; }

		public TinyboardApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override async Task<ApiResponse<IHtmlDocument>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/res/{threadNumber}.html"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(IHtmlDocument page, string board)
		{
			var thread = new Thread();

			var threadElement = page.QuerySelector("body > form > div");

			var postElements = threadElement.QuerySelectorAll("div.post");

			var postList = new List<Post>();

			foreach (var postElement in postElements)
			{
				var post = new Post();
				post.ContentType = ContentType.Tinyboard;


				string fileUrl = null;
				string thumbUrl = null;
				string youtubeUrl = null;

				if (postElement.ClassList.Contains("op"))
				{
					var anchor = (IHtmlAnchorElement)threadElement.Children.First(x => x.TagName.Equals("a", StringComparison.OrdinalIgnoreCase));

					fileUrl = anchor.Href;
					thumbUrl = ((IHtmlImageElement)anchor.FirstElementChild).Source;

					post.Subject = postElement.QuerySelector("span.subject")?.TextContent.TrimAndNullify();
				}
				else
				{
					var anchor = (IHtmlAnchorElement)postElement.Children.FirstOrDefault(x => x.TagName.Equals("a", StringComparison.OrdinalIgnoreCase));

					if (anchor != null)
					{
						fileUrl = anchor.Href;
						thumbUrl = ((IHtmlImageElement)anchor.FirstElementChild).Source;
					}
				}

				if (fileUrl != null)
				{
					var host = new Uri(fileUrl).Host;
					if (host == "youtube.com" || host == "youtu.be")
					{
						// crystal.cafe custom youtube submission
						youtubeUrl = fileUrl;
						fileUrl = thumbUrl;
					}
				}

				post.PostNumber = ulong.Parse(postElement.QuerySelector("p.intro").Id);
				post.Author = postElement.QuerySelector("span.name").TextContent.TrimAndNullify();
				
				post.TimePosted = DateTimeOffset.Parse(postElement.QuerySelector("time").GetAttribute("datetime"));
				//post.Tripcode = postElement.QuerySelector(".poster-trip").TextContent.TrimAndNullify();
				
				post.ContentRendered = postElement.QuerySelector("div.body").InnerHtml.TrimAndNullify();

				post.Media = fileUrl == null
					? Array.Empty<Media>()
					: new Media[1]
					{
						new Media
						{
							Index = 0,
							FileUrl = fileUrl,
							ThumbnailUrl = thumbUrl,
							Filename = "unavailable",
							FileExtension = Path.GetExtension(fileUrl),
							ThumbnailExtension = Path.GetExtension(thumbUrl),
							AdditionalMetadata = new()
							{
								ExternalMediaUrl = youtubeUrl
							}
						}
					};

				postList.Add(post);
			}

			thread.ThreadId = postList[0].PostNumber;
			thread.Title = postList[0].Subject;
			thread.Posts = postList.ToArray();

			return thread;
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/catalog"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			return new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data
				.QuerySelectorAll("body a.catalog-link")
				.Select(x =>
				{
					var rawPostId = x.GetAttribute("href").Replace($"/{board}/res/", "").Replace(".html", "");
					var postId = ulong.Parse(rawPostId);

					//var updateDateTime = DateTime.SpecifyKind(DateTime.Parse(x.QuerySelector("img").GetAttribute("title")), DateTimeKind.Utc);
					//var timestamp = Utility.GetGMTTimestamp(new DateTimeOffset(updateDateTime));
					var timestamp = 0UL;

					var subject = x.QuerySelector("div.subject")?.TextContent.TrimAndNullify();
					var textContent = x.QuerySelector("div.replies")?.Text().TrimAndNullify();

					return new PageThread(postId, timestamp, subject, textContent);
				})
				.ToArray());
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException();
		}
	}
}