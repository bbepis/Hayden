using Hayden.Config;

namespace Hayden
{
	public class ConfigFile
	{
		public ConsumerConfig Consumer { get; set; }

		public SourceConfig Source { get; set; }

		public ProxyConfig Proxy { get; set; } = new ProxyConfig();

		public HaydenConfigOptions Hayden { get; set; }
	}

	public class HaydenConfigOptions
	{
		public bool DebugLogging { get; set; } = false;

		public string ScraperType { get; set; } = "archive";
	}
}
