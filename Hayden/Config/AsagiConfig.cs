namespace Hayden.Config
{
	/// <summary>
	/// Configuration for the Asagi thread consumer backend.
	/// </summary>
	public class AsagiConfig
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

		/// <summary>
		/// The connection string of the MySql database to connect to.
		/// </summary>
		public string ConnectionString { get; set; }

		/// <summary>
		/// How many concurrent database connections should be opened. Ideally one per board.
		/// </summary>
		public int SqlConnectionPoolSize { get; set; }
	}
}