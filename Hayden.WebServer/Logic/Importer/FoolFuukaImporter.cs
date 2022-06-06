using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;

namespace Hayden.WebServer.Logic.Importer
{
    public class FoolFuukaImporter : BaseImporter<FoolFuukaThread, FoolFuukaPost, FoolFuukaPostMedia>
    {
		protected override void SetThreadInfo(ushort boardId, FoolFuukaThread thread, ref DBThread dbThread, bool exists)
		{
			var lastModifiedTime = Utility.ConvertGMTTimestamp(thread.Posts.Max(x => x.UnixTimestamp)).UtcDateTime;

			if (exists)
			{
				dbThread.IsDeleted = thread.IsDeleted == true;
				dbThread.IsArchived = thread.Archived;
				dbThread.LastModified = lastModifiedTime;
				dbThread.Title = thread.OriginalPost.Title;
			}
			else
			{
				dbThread = new DBThread
				{
					BoardId = boardId,
					ThreadId = thread.OriginalPost.PostNumber,
					Title = thread.OriginalPost.Title,
					IsArchived = thread.Archived,
					IsDeleted = thread.IsDeleted == true,
					LastModified = lastModifiedTime
				};
			}
		}

		protected override void SetPostInfo(ushort boardId, FoolFuukaThread thread, FoolFuukaPost post, ref DBPost dbPost, bool exists)
		{
			if (exists)
			{
				dbPost.Author = post.Author == "Anonymous" ? null : post.Author;
				dbPost.Email = post.Email;
				dbPost.Tripcode = post.Tripcode;
				dbPost.DateTime = Utility.ConvertGMTTimestamp(post.UnixTimestamp).UtcDateTime;
				dbPost.IsDeleted = post.ExtensionIsDeleted == true;
				dbPost.ContentRaw = post.SanitizedComment;
			}
			else
			{
				dbPost = new DBPost
				{
					BoardId = boardId,
					PostId = post.PostNumber,
					ThreadId = thread.OriginalPost.PostNumber,
					ContentHtml = null,
					ContentRaw = thread.OriginalPost.SanitizedComment,
					Author = post.Author == "Anonymous" ? null : post.Author,
					Email = post.Email,
					Tripcode = post.Tripcode,
					DateTime = Utility.ConvertGMTTimestamp(post.UnixTimestamp).UtcDateTime,
					IsDeleted = post.ExtensionIsDeleted == true
				};
			}
		}

		protected override void SetFileInfo(ushort boardId, FoolFuukaThread thread, FoolFuukaPost post, FoolFuukaPostMedia postFile, int index, DBFile dbFile, ref DBFileMapping dbFileMapping, bool exists)
		{
			if (!exists)
			{
				dbFileMapping = new DBFileMapping
				{
					BoardId = boardId,
					FileId = dbFile.Id,
					PostId = post.PostNumber,
					Index = (byte)index,
					Filename = !postFile.OriginalFilename.Contains('.') ? postFile.OriginalFilename : postFile.OriginalFilename.Remove(postFile.OriginalFilename.LastIndexOf('.')),
					IsDeleted = false,
					IsSpoiler = postFile.ThumbnailUrl.Contains("spoiler")
				};
			}
			else
			{
				dbFileMapping.IsSpoiler = postFile.ThumbnailUrl.Contains("spoiler");
			}
		}

		protected override IEnumerable<FoolFuukaPostMedia> GetPostFiles(FoolFuukaPost post)
			=> post.Media == null ? Enumerable.Empty<FoolFuukaPostMedia>() : Enumerable.Repeat(post.Media, 1);

		protected override string GetFilesystemSourceFilePath(string board, FoolFuukaThread thread, FoolFuukaPost post, FoolFuukaPostMedia postFile)
		{
			return Path.Combine(thread.OriginalPost.PostNumber.ToString(), postFile.TimestampedFilename);
		}

		protected override string GetFilesystemThumbnailFilePath(string board, FoolFuukaThread thread, FoolFuukaPost post, FoolFuukaPostMedia postFile)
		{
			return Path.Combine(thread.OriginalPost.PostNumber.ToString(), "thumbs", postFile.TimestampedThumbFilename);
		}
	}
}
