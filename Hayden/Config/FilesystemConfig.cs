namespace Hayden.Config
{
	/// <summary>
	/// Configuration for the Filesystem thread consumer backend.
	/// </summary>
	public class FilesystemConfig
	{
		/// <summary>
		/// The directory location to download image files and thumbnails to.
		/// </summary>
		public string DownloadLocation { get; set; }

		/// <summary>
		/// True to download full images, otherwise false to skip them.
		/// </summary>
		public bool FullImagesEnabled { get; set; }

		/// <summary>
		/// True to download thumbnail images, otherwise false to skip them.
		/// </summary>
		public bool ThumbnailsEnabled { get; set; }
	}
}