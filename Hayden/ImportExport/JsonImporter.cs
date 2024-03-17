using System.Collections.Generic;
using System.IO;
using System.Text;
using Hayden.Config;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Hayden.ImportExport;

/// <summary>
/// Importer for .json based exported dumps
/// </summary>
public class JsonImporter : IForwardOnlyImporter
{
	private SourceConfig sourceConfig;
	private ConsumerConfig consumerConfig;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Json");

	public JsonImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig)
	{
		this.sourceConfig = sourceConfig;
		this.consumerConfig = consumerConfig;
	}

	private JsonTextReader GetJsonReader(string filename)
	{
		if (!File.Exists(filename))
			throw new FileNotFoundException("Cannot find import file");

		Stream filestream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
			FileOptions.SequentialScan | FileOptions.Asynchronous);

		if (filename.EndsWith(".zst"))
			filestream = new ZstdSharp.DecompressionStream(filestream, leaveOpen: false);

		return new JsonTextReader(new StreamReader(filestream, Encoding.UTF8));
	}

	private async IAsyncEnumerable<DumpedThread> InternalEnumerateEntries(string filename)
	{
		await using var jsonReader = GetJsonReader(filename);

		await jsonReader.ReadAsync();
		
		while (await jsonReader.ReadAsync() && jsonReader.TokenType != JsonToken.EndArray)
		{
			var jObject = (JObject)await JToken.ReadFromAsync(jsonReader);

			yield return jObject.ToObject<DumpedThread>();
		}
	}

	public async IAsyncEnumerable<(ThreadPointer, Thread)> RetrieveThreads(string[] allowedBoards)
	{
		var boardHashset = new HashSet<string>(allowedBoards);
		
		var path = sourceConfig.DbConnectionString;
		
		await foreach (var thread in InternalEnumerateEntries(path))
		{
			if (!boardHashset.Contains(thread.Board))
				continue;

			var threadPointer = new ThreadPointer(string.Intern(thread.Board), thread.ThreadId);

			yield return (threadPointer, thread);
		}
	}

	public class DumpedThread : Thread
	{
		public string Board { get; set; }
	}
}