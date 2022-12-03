using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.Consumers
{
	public class FilesystemThreadConsumer : IThreadConsumer
	{
		public string ArchiveDirectory { get; set; }
		public ConsumerConfig Config { get; set; }

		public FilesystemThreadConsumer(ConsumerConfig config)
		{
			Config = config;
			ArchiveDirectory = config.DownloadLocation;
		}

		private static readonly JsonSerializer Serializer = new()
		{
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None
		};

		protected class WrittenThread
		{
			public Thread Thread { get; set; }
			public bool ThreadDeleted { get; set; }
			public List<ulong> DeletedPostIds { get; set; }

			public WrittenThread() {}

			public WrittenThread(Thread thread, bool threadDeleted)
			{
				Thread = thread;
				ThreadDeleted = false;
				DeletedPostIds = new List<ulong>();
			}

		}

		protected void WriteJson(string filename, WrittenThread thread)
		{
			using (var threadFileStream = new FileStream(filename, FileMode.Create))
			using (var streamWriter = new StreamWriter(threadFileStream))
			using (var jsonWriter = new JsonTextWriter(streamWriter))
			{
				Serializer.Serialize(jsonWriter, thread);
			}
		}

		protected WrittenThread ReadJson(string filename)
		{
			using StreamReader streamReader = new StreamReader(File.OpenRead(filename));
			using JsonReader reader = new JsonTextReader(streamReader);

			return JToken.Load(reader).ToObject<WrittenThread>();
		}

		/// <summary>
		/// Does nothing.
		/// </summary>
		public virtual Task InitializeAsync() => Task.CompletedTask;

		public Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo threadUpdateInfo)
		{
			var pointer = threadUpdateInfo.ThreadPointer;

			// set up directories

			string threadDirectory = Path.Combine(ArchiveDirectory, pointer.Board, pointer.ThreadId.ToString());
			string threadThumbsDirectory = Path.Combine(threadDirectory, "thumbs");

			if (!Directory.Exists(threadDirectory))
				Directory.CreateDirectory(threadDirectory);

			if (!Directory.Exists(threadThumbsDirectory))
				Directory.CreateDirectory(threadThumbsDirectory);


			// save thread JSON file

			string threadFileName = Path.Combine(threadDirectory, "thread.json");

			PerformJsonThreadUpdate(threadUpdateInfo, threadFileName);

			// download files from new posts only
			
			return Task.FromResult(CalculateImageDownloads(threadUpdateInfo, threadDirectory, pointer, threadThumbsDirectory));
		}

		protected IList<QueuedImageDownload> CalculateImageDownloads(
			ThreadUpdateInfo threadUpdateInfo,
			string threadImageDirectory,
			ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			if (!Config.FullImagesEnabled && !Config.ThumbnailsEnabled)
				return Array.Empty<QueuedImageDownload>();

			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var post in threadUpdateInfo.NewPosts)
			{
				foreach (var imageData in GetImageDownloadPaths(post, threadImageDirectory, pointer, threadThumbsDirectory))
				{
					var (queuedDownload, imageFilename, thumbFilename) = imageData;

					if (queuedDownload == null)
						continue;

					if (imageFilename != null && File.Exists(imageFilename))
						queuedDownload.FullImageUri = null;

					if (thumbFilename != null && File.Exists(thumbFilename))
						queuedDownload.ThumbnailImageUri = null;

					queuedDownload.Properties["imageFilename"] = imageFilename;
					queuedDownload.Properties["thumbFilename"] = thumbFilename;

					imageDownloads.Add(queuedDownload);
				}
			}

			return imageDownloads;
		}

		protected IEnumerable<(QueuedImageDownload download, string imageFilename, string thumbFilename)> GetImageDownloadPaths(Post post,
			string threadImageDirectory,
			ThreadPointer pointer,
			string threadThumbsDirectory)
		{
			if (post.Media == null || post.Media.Length == 0)
				yield break;


			foreach (var media in post.Media)
			{
				string fullImageFilename = null, thumbFilename = null;
				Uri imageUrl = null, thumbUrl = null;

				if (Config.FullImagesEnabled)
				{
					fullImageFilename = Path.Combine(threadImageDirectory, Path.ChangeExtension($"{post.PostNumber}-{media.Index}", media.FileExtension));
					imageUrl = new Uri(media.FileUrl);
				}

				if (Config.ThumbnailsEnabled)
				{
					thumbFilename = Path.Combine(threadThumbsDirectory, Path.ChangeExtension($"{post.PostNumber}-{media.Index}-thumb", media.ThumbnailExtension));
					thumbUrl = new Uri(media.ThumbnailUrl);
				}

				yield return (new QueuedImageDownload(imageUrl, thumbUrl), fullImageFilename, thumbFilename);
			}
		}

		public virtual async Task ProcessFileDownload(QueuedImageDownload queuedImageDownload, string imageTempFilename, string thumbTempFilename)
		{
			if (!queuedImageDownload.TryGetProperty("imageFilename", out string imageFilename)
			    || !queuedImageDownload.TryGetProperty("thumbFilename", out string thumbFilename))
			{
				throw new InvalidOperationException("Queued image download did not have the required properties");
			}

			if (imageTempFilename != null && !File.Exists(imageFilename))
				File.Move(imageTempFilename, imageFilename);

			if (thumbTempFilename != null && !File.Exists(thumbFilename))
				File.Move(thumbTempFilename, thumbFilename);
		}

		public void PerformJsonThreadUpdate(ThreadUpdateInfo threadUpdateInfo, string threadFileName)
		{
			WrittenThread writtenThread;

			try
			{
				writtenThread = ReadJson(threadFileName);
			}
			catch
			{
				// thread either doesn't exist, or is corrupt. treat it as a never seen before thread by writing it as-is

				WriteJson(threadFileName, new WrittenThread(threadUpdateInfo.Thread, false));
				return;
			}

			// this thread already exists on disk. we have to modify it such that we keep post deletions

			// overwrite any modified posts

			PerformThreadUpdate(threadUpdateInfo, writtenThread);

			// mark any deleted posts

			foreach (var deletedPostId in threadUpdateInfo.DeletedPosts)
			{
				if (!writtenThread.DeletedPostIds.Contains(deletedPostId))
					writtenThread.DeletedPostIds.Add(deletedPostId);
			}

			// add any new posts

			if (threadUpdateInfo.NewPosts.Count > 0)
				writtenThread.Thread.Posts = writtenThread.Thread.Posts.Concat(threadUpdateInfo.NewPosts).ToArray();

			// write the modified thread back to disk

			WriteJson(threadFileName, writtenThread);
		}

		protected void PerformThreadUpdate(ThreadUpdateInfo threadUpdateInfo, WrittenThread writtenThread)
		{
			foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
			{
				var existingPost = writtenThread.Thread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

				// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

				// So copy over the mutable properties

				existingPost.ContentRaw = modifiedPost.ContentRaw;
				existingPost.ContentRendered = modifiedPost.ContentRendered;

				if (existingPost.Media != null)
					foreach (var media in existingPost.Media)
					{
						if (modifiedPost.Media == null
						    || modifiedPost.Media.Length == 0
						    || modifiedPost.Media.Any(x => x.Filename == media.Filename && x.IsDeleted)
						    || modifiedPost.Media.All(x => x.Filename != media.Filename))
							media.IsDeleted = true;
					}

				existingPost.AdditionalMetadata = modifiedPost.AdditionalMetadata;
			}
		}

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			if (deleted)
			{
				string threadFileName = Path.Combine(ArchiveDirectory, board, threadId.ToString(), "thread.json");

				if (File.Exists(threadFileName))
				{
					var thread = ReadJson(threadFileName);
					
					thread.ThreadDeleted = true;

					WriteJson(threadFileName, thread);
				}
			}

			// ConsumeThread is always called with the archive flag before calling this method, so we don't need to worry about setting the archived flag

			return Task.CompletedTask;
		}

		public Task<ICollection<ExistingThreadInfo>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getMetadata = true)
		{
			var boardDirectory = Path.Combine(ArchiveDirectory, board);

			if (!Directory.Exists(boardDirectory))
				Directory.CreateDirectory(boardDirectory);

			var existingDirectories = Directory.GetDirectories(boardDirectory, "*", SearchOption.TopDirectoryOnly)
				.ToDictionary(x => ulong.Parse(Path.GetFileName(x)));

			var existingThreads = new List<ExistingThreadInfo>();

			foreach (var threadId in threadIdsToCheck)
			{
				if (!existingDirectories.TryGetValue(threadId, out var threadDir))
					continue;

				if (!getMetadata && !archivedOnly)
				{
					existingThreads.Add(new ExistingThreadInfo(threadId));
					continue;
				}

				var jsonFilePath = Path.Combine(threadDir, "thread.json");

				if (!File.Exists(jsonFilePath))
					continue;

				var writtenThread = ReadJson(jsonFilePath);

				if (writtenThread?.Thread == null || writtenThread.Thread.Posts.Length == 0)
				{
					// this thread might as well not be archived if there's nothing in it
					continue;
				}

				if (archivedOnly && writtenThread.Thread.IsArchived != true)
					continue;

				if (!getMetadata)
				{
					existingThreads.Add(new ExistingThreadInfo(threadId));
					continue;
				}

				List<(ulong, uint)> threadHashList = new();

				foreach (var post in writtenThread.Thread.Posts)
				{
					if (writtenThread.DeletedPostIds.Contains(post.PostNumber))
						// We don't return deleted posts, otherwise Hayden will think the post still exists
						continue;

					threadHashList.Add((post.PostNumber, CalculateHash(post)));
				}

				existingThreads.Add(new ExistingThreadInfo(threadId,
					writtenThread.Thread.IsArchived,
					writtenThread.Thread.Posts.Max(x => x.TimePosted),
					threadHashList));
			}

			return Task.FromResult<ICollection<ExistingThreadInfo>>(existingThreads);
		}

		public uint CalculateHash(Post post)
			=> HaydenMysqlThreadConsumer.CalculatePostHash(post.ContentRendered, post.ContentRaw,
				post.Media.Count(x => x.IsSpoiler ?? false),
				post.Media.Length,
				post.Media.Count(x => x.IsDeleted));

		public void Dispose() { }
	}
}