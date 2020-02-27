using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hayden.Contract
{
	/// <summary>
	/// An interface for handling storage of intermediate scraping state.
	/// </summary>
	public interface IStateStore
	{
		/// <summary>
		/// Writes the current image download queue to storage.
		/// </summary>
		/// <param name="imageDownloads">The list of queued storage downloads.</param>
		Task WriteDownloadQueue(IList<QueuedImageDownload> imageDownloads);

		/// <summary>
		/// Retrieves a list of queued image downloads from intermediate storage.
		/// </summary>
		Task<IList<QueuedImageDownload>> GetDownloadQueue();
	}
}