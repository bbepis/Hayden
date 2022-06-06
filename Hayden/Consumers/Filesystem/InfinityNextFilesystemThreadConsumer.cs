using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class InfinityNextFilesystemThreadConsumer : BaseFilesystemThreadConsumer<InfinityNextThread, InfinityNextPost>
	{
		public string ImageboardWebsite { get; }

		public InfinityNextFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(InfinityNextPost post,
			string threadImageDirectory, ThreadPointer pointer, string threadThumbsDirectory)
		{
			if (post.Attachments == null)
				yield break;

			foreach (var attachment in post.Attachments)
			{
				string fullImageFilename = null, thumbFilename = null;
				Uri imageUrl = null, thumbUrl = null;

				if (Config.FullImagesEnabled)
				{
					fullImageFilename = Path.Combine(threadImageDirectory, Path.GetFileName(attachment.FileUrl));
					imageUrl = new Uri($"{ImageboardWebsite}{attachment.FileUrl.Substring(1)}");
				}

				if (Config.ThumbnailsEnabled)
				{
					thumbFilename = Path.Combine(threadThumbsDirectory, Path.GetFileName(attachment.ThumbnailUrl));
					thumbUrl = new Uri($"{ImageboardWebsite}{attachment.ThumbnailUrl.Substring(1)}");
				}

				yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
			}
		}

		protected override void PerformThreadUpdate(ThreadUpdateInfo<InfinityNextThread, InfinityNextPost> threadUpdateInfo, InfinityNextThread writtenThread)
		{
			writtenThread.ReplyCount = threadUpdateInfo.Thread.ReplyCount;
			writtenThread.ReplyFileCount = threadUpdateInfo.Thread.ReplyFileCount;
			writtenThread.ReplyLast = threadUpdateInfo.Thread.ReplyLast;
			writtenThread.BumpedLast = threadUpdateInfo.Thread.BumpedLast;
			writtenThread.Stickied = threadUpdateInfo.Thread.Stickied;
			writtenThread.StickiedAt = threadUpdateInfo.Thread.StickiedAt;
			writtenThread.BumpLockedAt = threadUpdateInfo.Thread.BumpLockedAt;
			writtenThread.LockedAt = threadUpdateInfo.Thread.LockedAt;
			writtenThread.CyclicalAt = threadUpdateInfo.Thread.CyclicalAt;
			writtenThread.Locked = threadUpdateInfo.Thread.Locked;
			writtenThread.GlobalBumpedLast = threadUpdateInfo.Thread.GlobalBumpedLast;
			writtenThread.FeaturedAt = threadUpdateInfo.Thread.FeaturedAt;
			writtenThread.IsDeleted = threadUpdateInfo.Thread.IsDeleted;

			// overwrite any modified posts

			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties
				
				existingPost.UpdatedAt = modifiedPost.UpdatedAt;
				existingPost.UpdatedBy = modifiedPost.UpdatedBy;
				existingPost.DeletedAt = modifiedPost.DeletedAt;
				existingPost.AuthorIpNulledAt = modifiedPost.AuthorIpNulledAt;
				existingPost.CapcodeId = modifiedPost.CapcodeId;
				existingPost.Subject = modifiedPost.Subject;
				existingPost.AdventureId = modifiedPost.AdventureId;
				existingPost.BodyTooLong = modifiedPost.BodyTooLong;
				existingPost.BodyHasContent = modifiedPost.BodyHasContent;
				existingPost.BodyRightToLeft = modifiedPost.BodyRightToLeft;
				existingPost.BodySigned = modifiedPost.BodySigned;
				existingPost.ContentHtml = modifiedPost.ContentHtml;
				existingPost.ContentRaw = modifiedPost.ContentRaw;
				existingPost.GlobalBumpedLast = modifiedPost.GlobalBumpedLast;
				existingPost.Attachments = modifiedPost.Attachments;
			}
		}

		/// <summary>
		/// Calculates a hash from mutable properties of a post. Used for tracking if a post has been modified
		/// </summary>
		public static uint CalculatePostHash(string postContent, string postHtml, ulong updatedAt)
		{
			uint hashCode = Utility.FNV1aHash32(postContent);

			Utility.FNV1aHash32(postHtml, ref hashCode);
			Utility.FNV1aHash32(updatedAt, ref hashCode);

			return hashCode;
		}

		/// <inheritdoc />
		public override uint CalculateHash(InfinityNextPost post)
			=> CalculatePostHash(post.ContentRaw, post.ContentHtml, post.UpdatedAt.GetValueOrDefault());

	}
}