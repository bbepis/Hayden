using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using Hayden.Config;
using Hayden.Models;
using Serilog;

namespace Hayden.ImportExport;

/// <summary>
/// Importer for .json based exported dumps
/// </summary>
public class JsonImporter : IForwardOnlyImporter
{
	private SourceConfig sourceConfig;
	private ConsumerConfig consumerConfig;
	private HaydenConfigOptions haydenConfig;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Json");

	public JsonImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig, HaydenConfigOptions haydenConfig)
	{
		this.sourceConfig = sourceConfig;
		this.consumerConfig = consumerConfig;
		this.haydenConfig = haydenConfig;
	}

	private Stream GetStream(string filename)
	{
		if (!File.Exists(filename))
			throw new FileNotFoundException("Cannot find import file");

		Stream filestream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
			FileOptions.SequentialScan | FileOptions.Asynchronous);

		if (filename.EndsWith(".zst"))
			filestream = new ZstdSharp.DecompressionStream(filestream, leaveOpen: false);

		return filestream;
	}

	private async IAsyncEnumerable<DumpedThread> InternalEnumerateEntries(string filename)
	{
		await using var stream = GetStream(filename);

		var deserializeOptions = new System.Text.Json.JsonSerializerOptions
		{
			AllowTrailingCommas = true,
			IncludeFields = true,
			PropertyNameCaseInsensitive = true,
			Converters =
			{
				new JsonStringEnumConverter()
			}
		};

		await foreach (var thread in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<DumpedThread>(stream, deserializeOptions))
			yield return thread;
	}

	public async IAsyncEnumerable<(ThreadPointer, Thread)> RetrieveThreads(string[] allowedBoards)
	{
		var boardHashset = new HashSet<string>(allowedBoards);
		
		var path = sourceConfig.DbConnectionString;
		
		await foreach (var thread in InternalEnumerateEntries(path))
		{
			if (boardHashset.Count > 0 && !boardHashset.Contains(thread.Board))
				continue;

			foreach (var post in thread.Posts)
			{
				if (post == null)
					continue;

				if (post.Media == null)
					post.Media = Array.Empty<Media>();

				foreach (var file in post.Media)
				{
					if (string.IsNullOrWhiteSpace(file.Filename))
						file.Filename = "";

					if (string.IsNullOrWhiteSpace(file.FileExtension))
						file.FileExtension = "";
				}

				post.AdditionalMetadata.Source ??= haydenConfig.Source;
			}

			var threadPointer = new ThreadPointer(string.Intern(thread.Board), thread.ThreadId);

			yield return (threadPointer, thread);
		}
	}
}