using System;
using System.Collections.Generic;
using System.Text;

namespace Hayden
{
	public class ConfigFile
	{
		public dynamic Backend { get; set; }

		public dynamic Source { get; set; }

		public HaydenConfigOptions Hayden { get; set; }
	}

	public class HaydenConfigOptions
	{
		public bool DebugLogging { get; set; }
	}
}
