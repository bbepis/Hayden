using System;
using System.Collections.Generic;
using Hayden.Contract;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Hayden.Config;

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
	[JsonConverter(typeof(BoardConfigConverter))]
	public BoardConfig[] Boards { get; set; }

	/// <summary>
	/// An array of filters to apply to threads.
	/// </summary>
	public ThreadFilter[] Filters { get; set; }

	/// <summary>
	/// The minimum amount of time (in seconds) that should be waited in-between API calls. Defaults to 1.0 seconds if null.
	/// </summary>
	public double? ApiDelay { get; set; }

	/// <summary>
	/// The minimum amount of time (in seconds) that should be waited in-between image downloads. Defaults to 0.1 seconds if null.
	/// </summary>
	public double? ImageDownloadDelay { get; set; }

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

	/// <summary>
	/// Set to true to ignore thread last updated times in initial scans of the imageboard.
	/// </summary>
	public bool ForceRescan { get; set; }

	/// <summary>
	/// The User-Agent header to send to websites when scraping.
	/// </summary>
	public string UserAgent { get; set; }

	/// <summary>
	/// The Cookie header to be passed onto requests. Useful for getting around cloudflare challenges
	/// </summary>
	public string CookieString { get; set; }
}

public class BoardConfig
{
	public string Board { get; set; }
	public string TranslatedBoardName { get; set; }

	public BoardConfig() { }

	public BoardConfig(string board, string translatedBoardName = null)
	{
		Board = board;
		TranslatedBoardName = translatedBoardName;
	}
}

public class BoardConfigConverter : JsonConverter
{
	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> JToken.FromObject(value, serializer).WriteTo(writer);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		var list = new List<BoardConfig>();

		if (reader.TokenType != JsonToken.StartArray)
			throw new Exception("Invalid token");

		while (true)
		{
			reader.Read();
			if (reader.TokenType == JsonToken.EndArray)
				break;

			if (reader.TokenType == JsonToken.String)
				list.Add(new BoardConfig() { Board = (string)reader.Value });
			else if (reader.TokenType == JsonToken.StartObject)
				list.Add(JToken.ReadFrom(reader).ToObject<BoardConfig>());
			else
				throw new Exception($"Unknown token type for board config: {reader.TokenType}");
		}

		return list.ToArray();
	}

	public override bool CanRead => true;
	public override bool CanWrite => false;
	public override bool CanConvert(Type objectType) => objectType == typeof(string) || objectType == typeof(BoardConfig);
}

public class ThreadFilterConverter : JsonConverter
{
	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> JToken.FromObject(value, serializer).WriteTo(writer);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		=> new ThreadFilter(JToken.ReadFrom(reader).ToObject<ThreadFilterConfig>());

	public override bool CanRead => true;
	public override bool CanWrite => false;
	public override bool CanConvert(Type objectType) => objectType == typeof(string) || objectType == typeof(BoardConfig);
}

public class ThreadFilterConfig
{
	/// <summary>
	/// The filter for thread titles. Only archives a thread if its title matches this regex.
	/// </summary>
	[JsonProperty("title")]
	public string ThreadTitleRegexFilter { get; set; }

	/// <summary>
	/// The filter for thread OP post content. Only archives a thread if its OP's content matches this regex.
	/// </summary>
	[JsonProperty("op")]
	public string OPContentRegexFilter { get; set; }

	/// <summary>
	/// The regex filter for either the thread subject or OP post content.
	/// </summary>
	[JsonProperty("any")]
	public string AnyFilter { get; set; }

	/// <summary>
	/// The regex blacklist for either the thread subject or OP post content.
	/// </summary>
	[JsonProperty("none")]
	public string AnyBlacklist { get; set; }

	/// <summary>
	/// The board to check. "*" for all boards
	/// </summary>
	[JsonProperty("board")]
	public string Board { get; set; }

	[JsonProperty("images")]
	public bool? FullImages { get; set; }
	[JsonProperty("thumbs")]
	public bool? Thumbnails{ get; set; }
}