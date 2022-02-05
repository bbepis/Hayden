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
using Thread = Hayden.Models.Thread;

namespace Hayden.Consumers
{
	public class FilesystemThreadConsumer : IThreadConsumer<Thread, Post>
	{
		public string ArchiveDirectory { get; set; }
		public FilesystemConfig Config { get; set; }

		public FilesystemThreadConsumer(FilesystemConfig config)
		{
			Config = config;
			ArchiveDirectory = config.DownloadLocation;
		}

		private static readonly JsonSerializer Serializer = new JsonSerializer
		{
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None
		};

		private static void WriteJson(string filename, Thread thread)
		{
			using (var threadFileStream = new FileStream(filename, FileMode.Create))
			using (var streamWriter = new StreamWriter(threadFileStream))
			using (var jsonWriter = new JsonTextWriter(streamWriter))
			{
				Serializer.Serialize(jsonWriter, thread);
			}
		}

		private static Thread ReadJson(string filename)
		{
			using StreamReader streamReader = new StreamReader(File.OpenRead(filename));
			using JsonReader reader = new JsonTextReader(streamReader);

			return JToken.Load(reader).ToObject<Thread>();
		}

		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo<Thread, Post> threadUpdateInfo)
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
			
			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			foreach (var post in threadUpdateInfo.NewPosts.Where(x => x.TimestampedFilename.HasValue))
			{
				if (Config.FullImagesEnabled)
				{
					string fullImageFilename = Path.Combine(threadDirectory, post.TimestampedFilenameFull);
					string fullImageUrl = $"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilenameFull}";

					if (!File.Exists(fullImageFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));
				}

				if (Config.ThumbnailsEnabled)
				{
					string thumbFilename = Path.Combine(threadThumbsDirectory, $"{post.TimestampedFilename}s.jpg");
					string thumbUrl = $"https://i.4cdn.org/{pointer.Board}/{post.TimestampedFilename}s.jpg";

					if (!File.Exists(thumbFilename))
						imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
				}
			}

			return imageDownloads;
		}

		internal static void PerformJsonThreadUpdate(ThreadUpdateInfo<Thread, Post> threadUpdateInfo, string threadFileName)
		{
			if (!File.Exists(threadFileName))
			{
				// this is a brand new thread being written, so we just write it as-is

				WriteJson(threadFileName, threadUpdateInfo.Thread);
			}
			else
			{
				// this thread already exists on disk. we have to modify it such that we keep post deletions

				var writtenThread = ReadJson(threadFileName);

				// overwrite any modified posts

				foreach (var modifiedPost in threadUpdateInfo.UpdatedPosts)
				{
					var existingPost = writtenThread.Posts.First(x => x.PostNumber == modifiedPost.PostNumber);

					// We can't just overwrite the post because we might mangle some properties we want to preserve (i.e. file info)

					// So copy over the mutable properties

					existingPost.Comment = modifiedPost.Comment;

					if (modifiedPost.SpoilerImage.HasValue) // This might be null if the image is deleted
						existingPost.SpoilerImage = modifiedPost.SpoilerImage;

					// To mark deleted images, we still set this property but leave all other image-related properties intact
					existingPost.FileDeleted = modifiedPost.FileDeleted;

					// These are all OP-only properties. They're always null for replies so there's no harm in ignoring them

					if (existingPost.ReplyPostNumber == 0) // OP check
					{
						existingPost.Archived = modifiedPost.Archived;
						existingPost.ArchivedOn = modifiedPost.ArchivedOn;
						existingPost.Closed = modifiedPost.Closed;
						existingPost.BumpLimit = modifiedPost.BumpLimit;
						existingPost.ImageLimit = modifiedPost.ImageLimit;
						existingPost.TotalReplies = modifiedPost.TotalReplies;
						existingPost.TotalImages = modifiedPost.TotalImages;
						existingPost.UniqueIps = modifiedPost.UniqueIps;
					}
				}

				// mark any deleted posts

				foreach (var deletedPostId in threadUpdateInfo.DeletedPosts)
				{
					writtenThread.Posts.First(x => x.PostNumber == deletedPostId).ExtensionIsDeleted = true;
				}

				// add any new posts

				foreach (var newPost in threadUpdateInfo.NewPosts)
				{
					writtenThread.Posts.Add(newPost);
				}

				// write the modified thread back to disk

				WriteJson(threadFileName, writtenThread);
			}
		}

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			if (deleted)
			{
				string threadFileName = Path.Combine(ArchiveDirectory, board, threadId.ToString(), "thread.json");

				var thread = ReadJson(threadFileName);

				thread.IsDeleted = true;

				WriteJson(threadFileName, thread);
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

				var writtenThread = ReadJson(Path.Combine(threadDir, "thread.json"));

				if (writtenThread == null || writtenThread.Posts.Count == 0)
				{
					// this thread might as well not be archived if there's nothing in it
					continue;
				}

				if (archivedOnly && writtenThread.OriginalPost.Archived != true)
					continue;

				if (!getMetadata)
				{
					existingThreads.Add(new ExistingThreadInfo(threadId));
					continue;
				}

				List<(ulong, uint)> threadHashList = new();

				foreach (var post in writtenThread.Posts)
				{
					if (post.ExtensionIsDeleted == true)
						// We don't return deleted posts, otherwise Hayden will think the post still exists
						continue;

					threadHashList.Add((post.PostNumber, HaydenMysqlThreadConsumer.HaydenTrackedThread.CalculateYotsubaPostHash(post)));
				}

				existingThreads.Add(new ExistingThreadInfo(threadId,
					Utility.ConvertGMTTimestamp(writtenThread.Posts.Max(x => x.UnixTimestamp)),
					threadHashList));
			}

			return Task.FromResult<ICollection<ExistingThreadInfo>>(existingThreads);
		}

		public TrackedThread<Thread, Post> StartTrackingThread(ExistingThreadInfo existingThreadInfo)
		{
			return HaydenMysqlThreadConsumer.HaydenTrackedThread.StartTrackingThread(existingThreadInfo);
		}

		public TrackedThread<Thread, Post> StartTrackingThread()
		{
			return HaydenMysqlThreadConsumer.HaydenTrackedThread.StartTrackingThread();
		}

		public void Dispose() { }
	}
}