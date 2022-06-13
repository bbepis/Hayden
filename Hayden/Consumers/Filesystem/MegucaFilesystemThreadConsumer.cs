﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;

namespace Hayden.Consumers
{
	public class MegucaFilesystemThreadConsumer : BaseFilesystemThreadConsumer<MegucaThread, MegucaPost>
	{
		public string ImageboardWebsite { get; }

		public MegucaFilesystemThreadConsumer(string imageboardWebsite, FilesystemConfig config) : base(config)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		// https://github.com/bakape/shamichan/blob/8e47f42785caa99bbbfd2b35221f47822dbec1f3/imager/common/images.go#L11
		public static Dictionary<uint, string> ExtensionMappings = new()
		{
			[0] = ".jpg",
			[1] = ".png",
			[2] = ".gif",
			[3] = ".webm",
			[4] = ".pdf",
			[5] = ".svg",
			[6] = ".mp4",
			[7] = ".mp3",
			[8] = ".ogg",
			[9] = ".zip",
			[10] = ".7z",
			[11] = ".tgz",
			[12] = ".txz",
			[13] = ".flac",
			[14] = "",
			[15] = ".txt",
			[16] = ".webp",
			[17] = ".rar",
			[18] = ".cbz",
			[19] = ".cbr",
			[20] = ".mp4", // HEVC
		};

		protected override IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(MegucaPost post, string threadImageDirectory, ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			if (post.Image?.Filename == null)
				yield break;

			string fullImageFilename = null, thumbFilename = null;
			Uri imageUrl = null, thumbUrl = null;

			if (Config.FullImagesEnabled)
			{
				string imageName = $"{post.Image.Sha1Hash}{ExtensionMappings[post.Image.FileType]}";
				fullImageFilename = Path.Combine(threadImageDirectory, imageName);
				imageUrl = new Uri($"{ImageboardWebsite}assets/images/src/{imageName}");
			}

			if (Config.ThumbnailsEnabled)
			{
				string thumbName = $"{post.Image.Sha1Hash}{ExtensionMappings[post.Image.ThumbType]}";
				thumbFilename = Path.Combine(threadThumbsDirectory, thumbName);
				thumbUrl = new Uri($"{ImageboardWebsite}assets/images/thumb/{thumbName}");
			}

			yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
		}

		/// <inheritdoc />
		protected override void PerformThreadUpdate(ThreadUpdateInfo<MegucaThread, MegucaPost> threadUpdateInfo, MegucaThread writtenThread)
		{
			writtenThread.Abbreviated = threadUpdateInfo.Thread.Abbreviated;
			writtenThread.Sticky = threadUpdateInfo.Thread.Sticky;
			writtenThread.Locked = threadUpdateInfo.Thread.Locked;
			writtenThread.PostCount = threadUpdateInfo.Thread.PostCount;
			writtenThread.ImageCount = threadUpdateInfo.Thread.ImageCount;
			writtenThread.UpdateTime = threadUpdateInfo.Thread.UpdateTime;
			writtenThread.BumpTime = threadUpdateInfo.Thread.BumpTime;
			writtenThread.Subject = threadUpdateInfo.Thread.Subject;
			writtenThread.IsDeleted = threadUpdateInfo.Thread.IsDeleted;

			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);
				
				existingPost.ContentBody = modifiedPost.ContentBody;
			}
		}
		
		public static uint CalculatePostHash(string postContent, string originalFilenameNoExt)
		{
			// The HTML content of a post can change due to public warnings and bans.
			uint hashCode = Utility.FNV1aHash32(postContent);

			// Attached files can be removed, and have their spoiler status changed
			Utility.FNV1aHash32(originalFilenameNoExt, ref hashCode);

			return hashCode;
		}

		/// <inheritdoc />
		public override uint CalculateHash(MegucaPost post)
		{
			return CalculatePostHash(post.ContentBody, post.Image?.Filename);
		}
	}
}