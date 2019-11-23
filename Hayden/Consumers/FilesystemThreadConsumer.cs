using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Contract;
using Newtonsoft.Json;
using Thread = Hayden.Models.Thread;

namespace Hayden.Consumers
{
	public class FilesystemThreadConsumer : IThreadConsumer
	{
		public string ArchiveDirectory { get; set; }

		public FilesystemThreadConsumer(string baseArchiveDirectory)
		{
			ArchiveDirectory = baseArchiveDirectory;
		}

		public ConcurrentDictionary<ulong, int> ThreadCounters { get; } = new ConcurrentDictionary<ulong, int>();

		private static readonly JsonSerializer Serializer = new JsonSerializer
		{
			NullValueHandling = NullValueHandling.Ignore
		};

		public async Task<IList<QueuedImageDownload>> ConsumeThread(Thread thread, string board)
		{
			ulong threadNumber = thread.OriginalPost.PostNumber;


			// set up directories

			string threadDirectory = Path.Combine(ArchiveDirectory, threadNumber.ToString());
			string threadThumbsDirectory = Path.Combine(threadDirectory, "thumbs");

			if (!Directory.Exists(threadDirectory))
				Directory.CreateDirectory(threadDirectory);

			if (!Directory.Exists(threadThumbsDirectory))
				Directory.CreateDirectory(threadThumbsDirectory);


			// save thread JSON file

			string threadFileName = Path.Combine(threadDirectory, "thread.json");

			using (var threadFileStream = new FileStream(threadFileName, FileMode.Create))
			using (var streamWriter = new StreamWriter(threadFileStream))
			using (var jsonWriter = new JsonTextWriter(streamWriter))
			{
				Serializer.Serialize(jsonWriter, thread);
			}

			// download files that haven't already been downloaded

			if (!ThreadCounters.ContainsKey(threadNumber))
				ThreadCounters[threadNumber] = 0;

			var filesToDownload = thread.Posts
										.Skip(ThreadCounters[threadNumber])
										.Where(x => x.TimestampedFilename != null);

			List<QueuedImageDownload> imageDownloads = new List<QueuedImageDownload>();

			await Task.WhenAll(filesToDownload.Select(async x =>
			{
				string fullImageFilename = Path.Combine(threadDirectory, $"{x.TimestampedFilename}{x.FileExtension}");
				string fullImageUrl = $"https://i.4cdn.org/{board}/{x.TimestampedFilename}{x.FileExtension}";

				imageDownloads.Add(new QueuedImageDownload(new Uri(fullImageUrl), fullImageFilename));


				string thumbFilename = Path.Combine(threadThumbsDirectory, $"{x.TimestampedFilename}s.jpg");
				string thumbUrl = $"https://i.4cdn.org/{board}/{x.TimestampedFilename}s.jpg";

				imageDownloads.Add(new QueuedImageDownload(new Uri(thumbUrl), thumbFilename));
			}));

			ThreadCounters[threadNumber] = thread.Posts.Length;

			return imageDownloads;
		}

		public Task ThreadUntracked(ulong threadId, string board, bool deleted)
		{
			ThreadCounters.TryRemove(threadId, out _);

			return Task.CompletedTask;
		}

		public Task<ICollection<(ulong threadId, DateTime lastPostTime)>> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly, bool getTimestamps = true)
		{
			throw new System.NotImplementedException();
		}

		public void Dispose() { }
	}
}