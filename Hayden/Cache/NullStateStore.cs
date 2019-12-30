using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Contract;

namespace Hayden.Cache
{
	public class NullStateStore : IStateStore
	{
		public Task WriteDownloadQueue(IList<QueuedImageDownload> imageDownloads)
		{
			return Task.CompletedTask;
		}

		public Task<IList<QueuedImageDownload>> GetDownloadQueue()
		{
			return Task.FromResult((IList<QueuedImageDownload>)new QueuedImageDownload[0]);
		}
	}
}