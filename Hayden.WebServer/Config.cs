namespace Hayden.WebServer
{
	public class Config
	{
		public string ImagePrefix { get; set; }
		public string FileLocation { get; set; }
		public string DBConnectionString { get; set; }
		public string HCaptchaSiteKey { get; set; }
		public string HCaptchaSecret { get; set; }
		public bool HCaptchaTesting { get; set; }

		public bool ApiMode { get; set; }
	}
}