using Hayden.Config;

namespace Hayden.WebServer
{
	public class ServerConfig
	{
		public ServerCaptchaConfig Captcha { get; set; }

		public ServerExtensionsConfig Extensions { get; set; }

		public ServerSettingsConfig Settings { get; set; }

		public bool RedirectToHTTPS { get; set; }

		public bool SqlLogging { get; set; }
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
}