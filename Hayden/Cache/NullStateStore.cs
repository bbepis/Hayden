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
		public Task WriteDownloadQueue(IList<QueuedImageDownload> imageDownloads)
		{
			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return Task.FromResult((IList<QueuedImageDownload>)new QueuedImageDownload[0]);
		}
	}
}