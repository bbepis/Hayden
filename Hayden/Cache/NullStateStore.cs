using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;

namespace Hayden.Cache
{
	/// <summary>
	/// A dummy state store implementation that does not read or write from anywhere.
	/// </summary>
	public class NullStateStore : IStateStore
	{
		/// <inheritdoc/>
		public Task WriteDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public Task InsertToDownloadQueue(IReadOnlyCollection<QueuedImageDownload> imageDownloads)
		{
			return Task.CompletedTask;
		}

		public Task StoreKeyValue(string key, string value)
		{
			return Task.CompletedTask;
		}

		public Task<string> ReadKeyValue(string key)
		{
			return Task.FromResult((string)null);
		}

		/// <inheritdoc/>
		public Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return Task.FromResult((IList<QueuedImageDownload>)new QueuedImageDownload[0]);
		}
	}
}