namespace Hayden.WebServer
{
	public class Config
	{
		public string ImagePrefix { get; set; }
		public string FileLocation { get; set; }
		public string DBConnectionString { get; set; }

		public bool ApiMode { get; set; }
	}
}