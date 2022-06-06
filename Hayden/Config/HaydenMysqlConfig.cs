namespace Hayden.Config
{
	/// <summary>
	/// Configuration for the HaydenMysql thread consumer backend.
	/// </summary>
	public class HaydenMysqlConfig
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
		/// If set to true, the scraper will not use the MD5 hash to skip downloading media that has already been downloaded. This will safeguard against MD5 conflicts, however will obviously use a lot more bandwidth.
		/// </summary>
		public bool DoNotUseMd5HashForComparison { get; set; }
	}
}