using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Config;
using Hayden.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.Consumers
{
	public abstract class BaseFilesystemThreadConsumer<TThread, TPost> : IThreadConsumer<TThread, TPost> where TPost : IPost where TThread : IThread<TPost>
	{
		public string ArchiveDirectory { get; set; }
		public FilesystemConfig Config { get; set; }

		protected BaseFilesystemThreadConsumer(FilesystemConfig config)
		{
			Config = config;
			ArchiveDirectory = config.DownloadLocation;
		}

		private static readonly JsonSerializer Serializer = new()
		{
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None
		};

		protected void WriteJson(string filename, TThread thread)
		{
			using (var threadFileStream = new FileStream(filename, FileMode.Create))
			using (var streamWriter = new StreamWriter(threadFileStream))
			using (var jsonWriter = new JsonTextWriter(streamWriter))
			{
				Serializer.Serialize(jsonWriter, thread);
			}
		}

		protected TThread ReadJson(string filename)
		{
			using StreamReader streamReader = new StreamReader(File.OpenRead(filename));
			using JsonReader reader = new JsonTextReader(streamReader);

			return JToken.Load(reader).ToObject<TThread>();
		}

		/// <summary>
		/// Does nothing.
		/// </summary>
		public virtual Task InitializeAsync() => Task.CompletedTask;

		public async Task<IList<QueuedImageDownload>> ConsumeThread(ThreadUpdateInfo<TThread, TPost> threadUpdateInfo)
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
			
			return CalculateImageDownloads(threadUpdateInfo, threadDirectory, pointer, threadThumbsDirectory);
		}

		protected abstract IList<QueuedImageDownload> CalculateImageDownloads(ThreadUpdateInfo<TThread, TPost> threadUpdateInfo,
			string threadDirectory,
			ThreadPointer pointer,
			string threadThumbsDirectory);

		public void PerformJsonThreadUpdate(ThreadUpdateInfo<TThread, TPost> threadUpdateInfo, string threadFileName)
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

				PerformThreadUpdate(threadUpdateInfo, writtenThread);

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

		protected abstract void PerformThreadUpdate(ThreadUpdateInfo<TThread, TPost> threadUpdateInfo, TThread writtenThread);

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

				var jsonFilePath = Path.Combine(threadDir, "thread.json");

				if (!File.Exists(jsonFilePath))
					continue;

				var writtenThread = ReadJson(jsonFilePath);

				if (writtenThread == null || writtenThread.Posts.Count == 0)
				{
					// this thread might as well not be archived if there's nothing in it
					continue;
				}

				if (archivedOnly && writtenThread.Archived != true)
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

					threadHashList.Add((post.PostNumber, CalculateHash(post)));
				}

				existingThreads.Add(new ExistingThreadInfo(threadId,
					Utility.ConvertGMTTimestamp(writtenThread.Posts.Max(x => x.UnixTimestamp)),
					threadHashList));
			}

			return Task.FromResult<ICollection<ExistingThreadInfo>>(existingThreads);
		}

		public abstract uint CalculateHash(TPost post);

		public void Dispose() { }
	}
}