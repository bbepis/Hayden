namespace Hayden.Config
{
	public class AsagiConfig
	{
		public string DownloadLocation { get; set; }

		public bool FullImagesEnabled { get; set; }

		public bool ThumbnailsEnabled { get; set; }

		public string ConnectionString { get; set; }

		public int SqlConnectionPoolSize { get; set; }
	}
}