using Hayden.Config;

namespace Hayden.WebServer
{
	public class ServerConfig
	{
		public ServerDataConfig Data { get; set; }

		public ServerCaptchaConfig Captcha { get; set; }

		public ServerExtensionsConfig Extensions { get; set; }

		public ServerSettingsConfig Settings { get; set; }

		public ServerSearchConfig Search { get; set; }

		public bool RedirectToHTTPS { get; set; }

		public bool SqlLogging { get; set; }
	}

	public class ServerDataConfig
	{
		public string ImagePrefix { get; set; }
		public string FileLocation { get; set; }

		public DatabaseType? DBType { get; set; }
		public string ProviderType { get; set; }
		public string DBConnectionString { get; set; }

		public string AuxiliaryDbLocation { get; set; }
	}

	public class ServerCaptchaConfig
	{
		public string HCaptchaSiteKey { get; set; }
		public string HCaptchaSecret { get; set; }
		public bool HCaptchaTesting { get; set; }
	}

	public class ServerExtensionsConfig
	{
		public string ImageDeleteCommand { get; set; }
	}

	public class ServerSettingsConfig
	{
		public string SiteName { get; set; }
		public double? MaxFileUploadSizeMB { get; set; }

		public bool CompactBoardsUi { get; set; }

		//public string[] QuoteList { get; set; }
		//public string BannerFilename { get; set; }
		//public NewsItem[] NewsItems { get; set; }

		public string ShiftJisArt { get; set; }
	}

	public class ServerSearchConfig
	{
		public bool Enabled { get; set; }
		public bool Debug { get; set; }

		public string ServerType { get; set; }

		public string Endpoint { get; set; }

		public string Username { get; set; }
		public string Password { get; set; }

		public string IndexName { get; set; }
	}

	public class NewsItem
	{
		public string Title { get; set; }
		public string DateString { get; set; }
		public string Content { get; set; }
	}
}