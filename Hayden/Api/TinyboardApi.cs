using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	public class TinyboardApi : BaseApi<IHtmlDocument>
	{
		// JSON API is disabled by default, and AFAIK no living Tinyboard site actually enables it
		// https://github.com/savetheinternet/Tinyboard/blob/b0babb3323def8821330cb3b80166dbc1858ce1f/inc/config.php#L1409
		// Supposedly it also had a lot of issues as well
		// https://github.com/savetheinternet/Tinyboard/issues/156
		// https://github.com/savetheinternet/Tinyboard/issues/157

		private static readonly Regex MediaSizeRegex = new(@"\((?:Spoiler Image,\s*)?((?:\d+)?(?:[\.,]\d+)?)\s?(\w+), (\d+x\d+|[Pp][Dd][Ff])?", RegexOptions.Compiled);

		public string ImageboardWebsite { get; }

		public TinyboardApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		public override Task<ApiCapabilities> DetermineCapabilitiesAsync(HttpClient client)
		{
			return Task.FromResult(new ApiCapabilities()
			{
				SupportsArchive = true,
				SupportsBoardLastModified = false,
				SupportsBoardReplyCount = true
			});
		}

		/// <inheritdoc />
		protected override async Task<ApiResponse<IHtmlDocument>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/res/{threadNumber}.html"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(IHtmlDocument page, string board)
		{
			var thread = new Thread();

			var threadElement = page.QuerySelector("div.post.op").ParentElement;

			var postElements = threadElement.QuerySelectorAll("div.post");

			var postList = new List<Post>();

			foreach (var postElement in postElements)
			{
				var post = new Post();
				post.ContentType = ContentType.Tinyboard;
				
				post.PostNumber = ulong.Parse(postElement.QuerySelector("p.intro").Id);

				string fileUrl = null;
				string thumbUrl = null;
				string youtubeUrl = null;

				bool isOp = postElement.ClassList.Contains("op");

				if (isOp)
				{
					post.Subject = postElement.QuerySelector("span.subject")?.TextContent.TrimAndNullify();
				}

				var fileContainerElement = isOp ? threadElement : postElement;
				var anchor = (IHtmlAnchorElement)fileContainerElement.Children.FirstOrDefault(x =>
					x.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
					&& x.ChildElementCount > 0); // seems to be a bug on lolcow.farm where there are empty <a></a> tags. could potentially be deleted files

				if (anchor != null)
				{
					fileUrl = anchor.Href;

					if (fileUrl.Contains("player.php"))
					{
						// TODO: this shit needs to be FIXED
					}

					if (anchor.FirstElementChild is IHtmlVideoElement videoElement)
						thumbUrl = videoElement.Source;
					else
						thumbUrl = ((IHtmlImageElement)anchor.FirstElementChild).Source;
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

				var nameElement = postElement.QuerySelector("span.name");
				post.Author = nameElement.TextContent.TrimAndNullify();

				if (nameElement.ParentElement.ClassList.Contains("email"))
					post.Email = nameElement.ParentElement.GetAttribute("href").Replace("mailto:", "").TrimAndNullify();

				post.AdditionalMetadata.Capcode =
					postElement.QuerySelector("span.capcode")?.TextContent.TrimAndNullify() // crystal.cafe / lolcow.farm
					?? postElement.QuerySelector("span.capcode-owner")?.TextContent.TrimAndNullify() // crystal.cafe
					?? postElement.QuerySelector("span.capcode-shaymin")?.TextContent.TrimAndNullify() // lolcow.farm
					?? postElement.QuerySelector("span.capcode-farmhand")?.TextContent.TrimAndNullify(); // lolcow.farm

				if (post.AdditionalMetadata.Capcode != null)
				{
					post.AdditionalMetadata.Capcode = post.AdditionalMetadata.Capcode.TrimStart(' ', '#');
				}
				
				post.TimePosted = DateTimeOffset.Parse(postElement.QuerySelector("time").GetAttribute("datetime"));
				//post.Tripcode = postElement.QuerySelector(".poster-trip").TextContent.TrimAndNullify();
				
				post.ContentRendered = postElement.QuerySelector("div.body").InnerHtml.TrimAndNullify();

				if (fileUrl != null)
				{
					var fileInfo = postElement.QuerySelector("p.fileinfo");
					var actualFileInfo = fileInfo?.QuerySelector("span.unimportant");

					if (actualFileInfo != null)
					{
						//var actualFilename = actualFileInfo.QuerySelector("a")!.GetAttribute("download")!;
						var filenameElement = actualFileInfo.QuerySelector("span.postfilename");
						var actualFilename = actualFileInfo.QuerySelector("span.postfilename")!.GetAttribute("title")
							?? filenameElement.TextContent;

						var mediaInfoMatch = MediaSizeRegex.Match(actualFileInfo.TextContent);

						decimal fileSize = decimal.Parse(mediaInfoMatch.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

						var sizeMultiplier = mediaInfoMatch.Groups[2].Value;

						if (sizeMultiplier == "MB")
							fileSize *= 1024 * 1024;
						else if (sizeMultiplier == "KB")
							fileSize *= 1024;
						else if (sizeMultiplier != "B")
							throw new Exception($"Post {post.PostNumber}: unknown size multiplier {sizeMultiplier}");

						post.Media =
						[
							new Media
							{
								Index = 0,
								FileUrl = fileUrl,
								ThumbnailUrl = thumbUrl,
								Filename = Path.GetFileNameWithoutExtension(actualFilename),
								FileExtension = Path.GetExtension(fileUrl),
								ThumbnailExtension = Path.GetExtension(thumbUrl),
								FileSize = (uint)fileSize,
								IsSpoiler = thumbUrl?.Contains("spoiler", StringComparison.OrdinalIgnoreCase), // supposedly we can check the file info element, but this hasn't failed me yet
								AdditionalMetadata = new()
								{
									ExternalMediaUrl = youtubeUrl
								}
							}
						];
					}
					else
					{
						post.Media =
						[
							new Media
							{
								Index = 0,
								FileUrl = fileUrl,
								ThumbnailUrl = thumbUrl,
								Filename = "<blank>",
								IsSpoiler = thumbUrl?.Contains("spoiler", StringComparison.OrdinalIgnoreCase),
								FileExtension = Path.GetExtension(fileUrl),
								ThumbnailExtension = Path.GetExtension(thumbUrl),
								AdditionalMetadata = new()
								{
									ExternalMediaUrl = youtubeUrl
								}
							}
						];
					}
				}
				else
				{
					post.Media = Array.Empty<Media>();
				}

				postList.Add(post);
			}

			thread.ThreadId = postList[0].PostNumber;
			thread.Title = postList[0].Subject;
			thread.Posts = postList.ToArray();

			return thread;
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<ThreadOverviewInfo[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/catalog"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<ThreadOverviewInfo[]>(result.ResponseType, null);

			return new ApiResponse<ThreadOverviewInfo[]>(ResponseType.Ok, result.Data
				.QuerySelectorAll("body a.catalog-link")
				.Select((x, i) =>
				{
					if (ImageboardWebsite.Contains("lolcow.farm") && x.QuerySelector("div.thread") == null)
					{
						// Some very specific bug with lolcow.farm that causes one OP to spill over into the catalog

						return null;
					}

					var rawPostId = Regex.Match(x.GetAttribute("href"), @"(\d+).html").Groups[1].Value;
					var postId = ulong.Parse(rawPostId);

					// In savetheinternet's infinite wisdom, there's no year attached to the timestamp that appears on the catalog page
					// https://github.com/savetheinternet/Tinyboard/blob/b0babb3323def8821330cb3b80166dbc1858ce1f/templates/themes/catalog/catalog.html#L22

					// We can try and reconstruct it. Relies on two assumptions:
					//   1. The latest post was made in the current year
					//   2. The posts are ordered by bump time
					// We just have to track when the time jumps back chronologically, then subtract a year when it happens
					// This is important to consider since we're looking at imageboards that have boards with threads that are 6+ years old

					// However I'm choosing not to right now, and just opting to track reply count even if it's less efficient.
					//   - There would be edge cases with the above method, when the year changes on the website, but there's a timezone difference from UTC/users computer
					//   - For the sites I'm looking at, they're very sage heavy, so bump time would never get updated

					//var updateDateTime = DateTime.SpecifyKind(DateTime.Parse(x.QuerySelector("img").GetAttribute("title")), DateTimeKind.Utc);
					//var timestamp = Utility.GetGMTTimestamp(new DateTimeOffset(updateDateTime));

					var subject = x.QuerySelector("div.subject")?.TextContent.TrimAndNullify();
					var textContent = x.QuerySelector("div.replies")?.Text().TrimAndNullify();

					var replyCountElement = x.QuerySelector("span.reply-count, div.replies > strong");

					if (replyCountElement == null)
						throw new Exception($"Could not determine reply count. Board /{board}/{postId}");

					var replyCountText = replyCountElement.Text().TrimAndNullify();
					var replyCount = int.Parse(Regex.Match(replyCountText, @"(\d+) repl(?:y|ies)").Groups[1].Value);

					return new ThreadOverviewInfo
					{
						ThreadId = postId,
						Subject = subject,
						ContentHtml = textContent,
						LastModified = null,
						Position = i,
						ReplyCount = replyCount
					};
				})
				.Where(x => x != null)
				.ToArray());
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not have an archive");
		}
	}
}