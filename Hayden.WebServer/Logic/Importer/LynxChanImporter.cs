using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;

namespace Hayden.WebServer.Logic.Importer
{
    public class LynxChanImporter : BaseImporter<LynxChanThread, LynxChanPost, LynxChanPostFile>
    {
		protected override void SetThreadInfo(ushort boardId, LynxChanThread thread, ref DBThread dbThread, bool exists)
		{
			var lastModifiedTime = thread.Posts.Max(x => x.CreationDateTime).UtcDateTime;

			if (exists)
			{
				dbThread.IsDeleted = thread.IsDeleted == true;
				dbThread.IsArchived = thread.Archived;
				dbThread.LastModified = lastModifiedTime;
				dbThread.Title = thread.Subject;
			}
			else
			{
				dbThread = new DBThread
				{
					BoardId = boardId,
					ThreadId = thread.OriginalPost.PostNumber,
					Title = thread.OriginalPost.Subject,
					IsArchived = thread.Archived,
					IsDeleted = thread.IsDeleted == true,
					LastModified = lastModifiedTime
				};
			}
		}

		protected override void SetPostInfo(ushort boardId, LynxChanThread thread, LynxChanPost post, ref DBPost dbPost, bool exists)
		{
			if (exists)
			{
				dbPost.Author = post.Name;
				dbPost.Email = post.Email;
				dbPost.DateTime = post.CreationDateTime.UtcDateTime;
				dbPost.IsDeleted = post.ExtensionIsDeleted == true;
				dbPost.ContentHtml = post.Markdown;
				dbPost.ContentRaw = post.Message;
			}
			else
			{
				dbPost = new DBPost
				{
					BoardId = boardId,
					PostId = post.PostNumber,
					ThreadId = thread.OriginalPost.PostNumber,
					ContentHtml = post.Markdown,
					ContentRaw = post.Message,
					Author = post.Name == "Anonymous" ? null : post.Name,
					Email = post.Email,
					Tripcode = null,
					DateTime = post.CreationDateTime.UtcDateTime,
					IsDeleted = post.ExtensionIsDeleted == true
				};
			}
		}

		protected override void SetFileInfo(ushort boardId, LynxChanThread thread, LynxChanPost post, LynxChanPostFile postFile, int index, DBFile dbFile, ref DBFileMapping dbFileMapping, bool exists)
		{
			if (!exists)
			{
				dbFileMapping = new DBFileMapping
				{
					BoardId = boardId,
					FileId = dbFile.Id,
					PostId = post.PostNumber,
					Index = (byte)index,
					Filename = !postFile.OriginalName.Contains('.') ? postFile.OriginalName : postFile.OriginalName.Remove(postFile.OriginalName.LastIndexOf('.')),
					IsDeleted = postFile.IsDeleted == true,
					IsSpoiler = postFile.ThumbnailUrl.Contains("spoiler")
				};
			}
			else
			{
				dbFileMapping.IsSpoiler = postFile.ThumbnailUrl.Contains("spoiler");
			}
		}

		protected override IEnumerable<LynxChanPostFile> GetPostFiles(LynxChanPost post)
			=> post.Files;

		protected override string GetFilesystemSourceFilePath(string board, LynxChanThread thread, LynxChanPost post, LynxChanPostFile postFile)
		{
			return Path.Combine(thread.ThreadId.ToString(), postFile.DirectPath);
		}

		protected override string GetFilesystemThumbnailFilePath(string board, LynxChanThread thread, LynxChanPost post, LynxChanPostFile postFile)
		{
			return Path.Combine(thread.ThreadId.ToString(), "thumbs", postFile.DirectThumbPath);
		}
	}
}
