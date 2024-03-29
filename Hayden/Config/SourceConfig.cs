using System.Collections.Generic;
using Hayden.Contract;

namespace Hayden.Config
{
	/// <summary>
	/// Configuration object for the 4chan API
	/// </summary>
	public class SourceConfig
	{
		/// <summary>
		/// The name of the <see cref="IFrontendApi"/> to use when starting up Hayden.
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// The website of the imageboard. Only applicable for some sources
		/// </summary>
		public string ImageboardWebsite { get; set; }

		/// <summary>
		/// The connection string of the source database to read data from.
		/// </summary>
		public string DbConnectionString { get; set; }

		/// <summary>
		/// An array of boards to be archived.
		/// </summary>
		public Dictionary<string, BoardRulesConfig> Boards { get; set; }

		/// <summary>
		/// The minimum amount of time (in seconds) that should be waited in-between API calls. Defaults to 1.0 seconds if null.
		/// </summary>
		public double? ApiDelay { get; set; }

		/// <summary>
		/// The minimum amount of time (in seconds) that should be waited in-between board scrapes. Defaults to 30.0 seconds if null.
		/// </summary>
		public double? BoardScrapeDelay { get; set; }

		/// <summary>
		/// True if downloading from the archive, false if not.
		/// </summary>
		public bool ReadArchive { get; set; }

		/// <summary>
		/// True if only performing a single scan from the source, otherwise false to infinitely scan the source for updates.
		/// </summary>
		public bool SingleScan { get; set; }
	}

	public class BoardRulesConfig
	{
		/// <summary>
		/// The filter for thread titles. Only archives a thread if its title matches this regex.
		/// </summary>
		public string ThreadTitleRegexFilter { get; set; }

		/// <summary>
		/// The filter for thread OP post content. Only archives a thread if its OP's content matches this regex.
		/// </summary>
		public string OPContentRegexFilter { get; set; }

		/// <summary>
		/// The regex filter for either the thread subject or OP post content.
		/// </summary>
		public string AnyFilter { get; set; }

		/// <summary>
		/// The regex blacklist for either the thread subject or OP post content.
		/// </summary>
		public string AnyBlacklist { get; set; }

		/// <summary>
		/// The board name to use when saving to the storage medium
		/// </summary>
		public string StoredBoardName { get; set; }
	}
}