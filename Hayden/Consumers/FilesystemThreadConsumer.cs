using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

		private readonly SemaphoreSlim DownloadSemaphore = new SemaphoreSlim(10);

		public async Task ConsumeThread(Thread thread, string board)
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


			await Task.WhenAll(filesToDownload.Select(async x =>
			{
				string fullImageFilename = Path.Combine(threadDirectory, $"{x.TimestampedFilename}{x.FileExtension}");
				string fullImageUrl = $"https://i.4cdn.org/{board}/{x.TimestampedFilename}{x.FileExtension}";

				await DownloadFile(fullImageUrl, fullImageFilename);


				string thumbFilename = Path.Combine(threadThumbsDirectory, $"{x.TimestampedFilename}s.jpg");
				string thumbUrl = $"https://i.4cdn.org/{board}/{x.TimestampedFilename}s.jpg";

				await DownloadFile(thumbUrl, thumbFilename);
			}));

			ThreadCounters[threadNumber] = thread.Posts.Length;
		}

		public Task ThreadUntracked(ulong threadId, string board)
		{
			ThreadCounters.TryRemove(threadId, out _);

			return Task.CompletedTask;
		}

		public Task<ulong[]> CheckExistingThreads(IEnumerable<ulong> threadIdsToCheck, string board, bool archivedOnly)
		{
			throw new System.NotImplementedException();
		}

		private async Task DownloadFile(string imageUrl, string downloadPath)
		{
			if (File.Exists(downloadPath))
				return;

			await DownloadSemaphore.WaitAsync();

			Program.Log($"Downloading image {Path.GetFileName(downloadPath)}");

			try
			{
				using (var webStream = await YotsubaApi.HttpClient.GetStreamAsync(imageUrl))
				using (var fileStream = new FileStream(downloadPath, FileMode.Create))
				{
					await webStream.CopyToAsync(fileStream);
				}
			}
			finally
			{
				DownloadSemaphore.Release();
			}
		}

		public void Dispose() { }
	}
}