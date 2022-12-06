using Hayden.Config;

namespace Hayden.WebServer
{
	public class ServerConfig
	{
		public ServerDataConfig Data { get; set; }

		public ServerCaptchaConfig Captcha { get; set; }

		public bool ApiMode { get; set; }
	}

	public class ServerDataConfig
	{
		public string ImagePrefix { get; set; }
		public string FileLocation { get; set; }

		public DatabaseType? DBType { get; set; }
		public string DBConnectionString { get; set; }
	}

	public class ServerCaptchaConfig
	{
		public string HCaptchaSiteKey { get; set; }
		public string HCaptchaSecret { get; set; }
		public bool HCaptchaTesting { get; set; }
	}
}