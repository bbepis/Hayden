using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hayden.Contract
{
	public interface IStateStore
	{
		Task WriteDownloadQueue(IList<QueuedImageDownload> imageDownloads);

		Task<IList<QueuedImageDownload>> GetDownloadQueue();
	}
}