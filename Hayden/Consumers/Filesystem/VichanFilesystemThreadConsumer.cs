using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class VichanFilesystemThreadConsumer : BaseFilesystemThreadConsumer<VichanThread, VichanPost>
	{
		public string ImageboardWebsite { get; }

		public VichanFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(VichanPost post, string threadImageDirectory, ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			var vichanFiles = new List<VichanExtraFile>();

			if (post.OriginalFilename != null)
			{
				vichanFiles.Add(new VichanExtraFile
				{
					FileExtension = post.FileExtension,
					FileSize = post.FileSize,
					ImageHeight = post.ImageHeight,
					ImageWidth = post.ImageWidth,
					OriginalFilename = post.OriginalFilename,
					ThumbnailHeight = post.ThumbnailHeight,
					ThumbnailWidth = post.ThumbnailWidth,
					TimestampedFilename = post.TimestampedFilename
				});
			}

			if (post.ExtraFiles != null)
			{
				vichanFiles.AddRange(post.ExtraFiles);
			}

			foreach (var vichanFile in vichanFiles)
			{
				string fullImageFilename = null, thumbFilename = null;
				Uri imageUrl = null, thumbUrl = null;

				if (Config.FullImagesEnabled)
				{
					fullImageFilename = Path.Combine(threadImageDirectory, $"{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
					imageUrl = new Uri($"{ImageboardWebsite}{pointer.Board}/thumb/{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
				}

				if (Config.ThumbnailsEnabled)
				{
					thumbFilename = Path.Combine(threadThumbsDirectory, $"{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
					thumbUrl = new Uri($"{ImageboardWebsite}{pointer.Board}/src/{vichanFile.TimestampedFilename}{vichanFile.FileExtension}");
				}

				yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
			}
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<VichanThread, VichanPost> threadUpdateInfo, VichanThread writtenThread)
		{
			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties

				existingPost.Comment = modifiedPost.Comment;

				// These are all OP-only properties. They're always null for replies so there's no harm in ignoring them

				if (existingPost.ReplyPostNumber == 0) // OP check
				{
					existingPost.Closed = modifiedPost.Closed;
					existingPost.TotalReplies = modifiedPost.TotalReplies;
					existingPost.TotalImages = modifiedPost.TotalImages;
				}
			}
		}

		public static uint CalculatePostHash(string postHtml, string originalFilenameNoExt, bool? closed, uint? replyCount, ushort? imageCount)
		{
			// Null bool? values should evaluate to false everywhere
			static int EvaluateNullableBool(bool? value)
			{
				return value.HasValue
					? (value.Value ? 1 : 2)
					: 2;
			}

			// The HTML content of a post can change due to public warnings and bans.
			uint hashCode = Utility.FNV1aHash32(postHtml);

			// Attached files can be removed, and have their spoiler status changed
			Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

			// The OP of a thread can have numerous properties change.
			// As such, these properties are only considered mutable for OPs (because that's the only place they can exist) and immutable for replies.
			Utility.FNV1aHash32(EvaluateNullableBool(closed), ref hashCode);
			Utility.FNV1aHash32((int?)replyCount ?? -1, ref hashCode);
			Utility.FNV1aHash32(imageCount ?? -1, ref hashCode);

			return hashCode;
		}

		/// <inheritdoc />
		public override uint CalculateHash(VichanPost post)
			=> CalculatePostHash(post.Comment, post.OriginalFilename, post.Closed, post.TotalReplies, post.TotalImages);
	}
}