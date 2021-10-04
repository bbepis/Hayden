namespace Hayden.Config
{
	/// <summary>
	/// Configuration object for the 4chan API
	/// </summary>
	public class YotsubaConfig
	{
		/// <summary>
		/// An array of boards to be archived.
		/// </summary>
		public string[] Boards { get; set; }

		/// <summary>
		/// The filter for thread titles. Only archives a thread if its title matches this regex.
		/// </summary>
		public string ThreadTitleRegexFilter { get; set; }

		/// <summary>
		/// The minimum amount of time (in seconds) that should be waited in-between API calls. Defaults to 1.0 seconds if null.
		/// </summary>
		public double? ApiDelay { get; set; }

		/// <summary>
		/// The minimum amount of time (in seconds) that should be waited in-between board scrapes. Defaults to 30.0 seconds if null.
		/// </summary>
		public double? BoardDelay { get; set; }

		/// <summary>
		/// True if downloading from the archive, false if not.
		/// </summary>
		public bool ReadArchive { get; set; }
	}
}