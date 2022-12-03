using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Dom;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public class ASPNetChanApi : BaseApi<IHtmlDocument>
	{
		public string ImageboardWebsite { get; }

		public ASPNetChanApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => true;

		/// <inheritdoc />
		protected override async Task<ApiResponse<IHtmlDocument>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/{threadNumber}"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(IHtmlDocument page, string board)
		{
			var thread = new Thread();

			var threadElement = page.QuerySelector(".thread");

			var postElements = threadElement.QuerySelectorAll(".post-container");

			var postList = new List<Post>();

			foreach (var postElement in postElements)
			{
				var post = new Post();
				post.AdditionalMetadata = new JObject();
				post.ContentType = ContentType.ASPNetChan;

				post.PostNumber = ulong.Parse(postElement.GetAttribute("data-post-no"));
				post.Author = postElement.GetAttribute("data-poster-name").TrimAndNullify();

				var posterId = postElement.GetAttribute("data-poster-id");

				if (!string.IsNullOrWhiteSpace(posterId))
					post.AdditionalMetadata["posterID"] = posterId;

				var timeElement = postElement.QuerySelector(".post-time > time");
				post.TimePosted = DateTimeOffset.Parse(timeElement.GetAttribute("datetime"));

				post.Subject = postElement.QuerySelector(".post-subject").TextContent.TrimAndNullify();
				post.Tripcode = postElement.QuerySelector(".poster-trip").TextContent.TrimAndNullify();

				var capcode = postElement.QuerySelector(".poster-capcode").TextContent.TrimAndNullify();
				if (!string.IsNullOrWhiteSpace(capcode))
					post.AdditionalMetadata["capcode"] = capcode;

				var flag = postElement.QuerySelector(".poster-flag")?.GetAttribute("title");
				if (!string.IsNullOrWhiteSpace(flag))
					post.AdditionalMetadata["flagName"] = flag;

				post.ContentRendered = postElement.QuerySelector(".post-body").InnerHtml.Trim();

				byte index = 0;
				var mediaList = new List<Media>();

				foreach (var postImageElement in postElement.QuerySelectorAll(".post-file"))
				{
					var fileLinkElement = (IHtmlAnchorElement)postImageElement.QuerySelector(".file-image");
					
					var fileUrl = fileLinkElement.Href;

					var originalFileDownloadElement = (IHtmlAnchorElement)postElement.QuerySelector(".file-header > small > a");
					var fileImageElement = (IHtmlImageElement)postImageElement.QuerySelector("img");

					var originalFilename = originalFileDownloadElement.Download.TrimAndNullify() ?? fileImageElement.AlternativeText.Trim();
					var thumbnailUrl = fileImageElement.Source;

					var extension = Path.GetExtension(originalFilename);
					if (string.IsNullOrWhiteSpace(extension))
						extension = Path.GetExtension(fileUrl);

					var isSpoiler = thumbnailUrl.EndsWith("/spoilerimage")
					                || fileImageElement.AlternativeText?.Trim() == "Spoilered";

					mediaList.Add(new Media
					{
						Filename = Path.GetFileNameWithoutExtension(originalFilename),
						FileExtension = extension,
						FileUrl = fileUrl,
						ThumbnailUrl = thumbnailUrl,
						ThumbnailExtension = isSpoiler ? "jpg" : Path.GetExtension(thumbnailUrl),
						IsSpoiler = isSpoiler,
						Index = index
					});

					index++;
				}

				post.Media = mediaList.ToArray();

				postList.Add(post);
			}

			thread.ThreadId = postList[0].PostNumber;
			thread.Title = postList[0].Subject;
			thread.Posts = postList.ToArray();

			var threadStats = page.QuerySelector("#thread_stats_page");
			if (threadStats != null && threadStats.TextContent.Contains("Archived"))
				thread.IsArchived = true;

			return thread;
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/catalog"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			return new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data
				.QuerySelectorAll("#catalog > .catalog-thread")
				.Select(x => new PageThread(ulong.Parse(x.GetAttribute("data-id")),
					ulong.Parse(x.GetAttribute("data-bumptime")),
					x.QuerySelector(".catalog-thread-subject").TextContent,
					x.QuerySelector(".catalog-thread-body").InnerHtml))
				.ToArray());
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/archive"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<ulong[]>(result.ResponseType, null);

			return new ApiResponse<ulong[]>(ResponseType.Ok, result.Data
				.QuerySelectorAll("#catalog > .catalog-thread")
				.Select(x => ulong.Parse(x.GetAttribute("data-id")))
				.ToArray());
		}
	}
}