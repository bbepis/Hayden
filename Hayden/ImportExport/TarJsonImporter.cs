using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Hayden.Consumers;
using Newtonsoft.Json;
using NodaTime;
using Serilog;

namespace Hayden.ImportExport;

/// <summary>
/// Importer for yukiparser parsed html files
/// </summary>
public class TarJsonImporter : IForwardOnlyImporter
{
	private SourceConfig sourceConfig;
	private ConsumerConfig consumerConfig;

	private ILogger Logger { get; } = SerilogManager.CreateSubLogger("Tar");

	public TarJsonImporter(SourceConfig sourceConfig, ConsumerConfig consumerConfig)
	{
		this.sourceConfig = sourceConfig;
		this.consumerConfig = consumerConfig;
	}

	private TarReader GetTarReader(string filename)
	{
		if (!File.Exists(filename))
			throw new FileNotFoundException("Cannot find tar file");

		Stream filestream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
			FileOptions.SequentialScan | FileOptions.Asynchronous);

		if (filename.EndsWith(".zst"))
			filestream = new ZstdSharp.DecompressionStream(filestream, leaveOpen: false);

		return new TarReader(filestream);
	}

	private async IAsyncEnumerable<TarEntry> InternalEnumerateEntries(string filename)
	{
		await using var tarReader = GetTarReader(filename);

		TarEntry entry;
		while ((entry = await tarReader.GetNextEntryAsync(false)) != null)
		{
			if (entry.EntryType == TarEntryType.RegularFile && entry.Length > 0 && entry.Name.EndsWith(".json") && entry.DataStream != null)
				yield return entry;
		}
	}

	private readonly JsonSerializer serializer = new();
	private static Regex nameRegex = new("(\\w+)\\/(\\d+)\\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public async IAsyncEnumerable<(ThreadPointer, Thread)> RetrieveThreads(string[] allowedBoards)
	{
		var boardHashset = new HashSet<string>(allowedBoards);
		//var pointerHashset = new HashSet<ThreadPointer>();
		
		var path = sourceConfig.DbConnectionString;

		var tarFiles = new List<string>();

		if (Directory.Exists(path))
		{
			tarFiles.AddRange(Directory.EnumerateFiles(path, "*.tar", SearchOption.TopDirectoryOnly));
			tarFiles.AddRange(Directory.EnumerateFiles(path, "*.tar.zst", SearchOption.TopDirectoryOnly));
		}
		else if (File.Exists(path))
			tarFiles.Add(path);

		tarFiles.Sort();

		foreach (var tarFile in tarFiles)
		{
			await foreach (var entry in InternalEnumerateEntries(tarFile))
			{
				var nameMatch = nameRegex.Match(entry.Name);

				if (!nameMatch.Success)
				{
					Logger.Debug("Skipping {filename} as it does not conform to regex", entry.Name);
					continue;
				}

				var board = nameMatch.Groups[1].Value;

				if (!boardHashset.Contains(board))
				{
					Logger.Debug("Skipping {filename} as it is not in allowed list of boards", entry.Name);
					continue;
				}

				using var streamReader = new StreamReader(entry.DataStream!, leaveOpen: true);
				await using var jsonReader = new JsonTextReader(streamReader);

				var posts = serializer.Deserialize<PostObject[]>(jsonReader);

				if (posts == null || posts.Length == 0)
				{
					Logger.Error($"Empty posts: {entry.Name}");
					continue;
				}

				var op = posts.MinBy(x => x.PostId);

				var threadPointer = new ThreadPointer(string.Intern(board), op.PostId);

				//if (pointerHashset.Contains(threadPointer))
				//{
				//	Logger.Debug("Skipping {filename} as it has been previously read", entry.Name);
				//	continue;
				//}
				//pointerHashset.Add(threadPointer);
				byte[] ConvertMd5Base64(PostObject post)
				{
					if (string.IsNullOrWhiteSpace(post.Md5String))
						return null;

					if (post.Md5String.Length < 22 || post.Md5String.Length > 24)
						Logger.Error(
							$"Failed MD5 decode on /{board}/{op.PostId}/{post.PostId}: \"{post.Md5String}\"");
					else if (post.Md5String.Length != 24)
						post.Md5String = post.Md5String.PadRight(24, '=');

					var md5Array = new byte[16];

					if (!Convert.TryFromBase64String(post.Md5String, md5Array, out _))
						Logger.Error(
							$"Failed MD5 decode on /{board}/{op.PostId}/{post.PostId}: \"{post.Md5String}\"");

					return md5Array;
				}

				var thread = new Thread
				{
					ThreadId = threadPointer.ThreadId,
					Title = op.Subject.TrimAndNullify(),
					Posts = posts.OrderBy(x => x.PostId).Select(x => new Post
					{
						PostNumber = x.PostId,
						Author = x.AuthorName?.TrimAndNullify(),
						Tripcode = x.AuthorTrip?.TrimAndNullify(),
						ContentRaw = AsagiThreadConsumer.CleanComment(x.HtmlContents),
						Email = x.Email,
						TimePosted = x.TimestampUtc.HasValue
							? Instant.FromUnixTimeSeconds((long)x.TimestampUtc).ToDateTimeOffset()
							: DateTimeOffset.UnixEpoch,
						ContentType = ContentType.Yotsuba,
						Subject = x.Subject, // this should only be applicable to 8chan posts
						Media = x.FileUrl == null && string.IsNullOrWhiteSpace(x.Md5String)
							? Array.Empty<Media>()
							: new Media[]
							{
								new Media
								{
									Filename = x.MediaFilename != null
										? Path.GetFileNameWithoutExtension(x.MediaFilename)
										: null,
									FileExtension = x.MediaFilename != null
										? Path.GetExtension(x.MediaFilename)
										: null,
									FileUrl = x.FileUrl,
									Index = 0, // this needs to be addressed with 8chan
									Md5Hash = ConvertMd5Base64(x),
									IsDeleted = x.FileDeleted ?? false
								}
							},
						AdditionalMetadata = new Post.PostAdditionalMetadata
						{
							Capcode = x.Capcode.TrimAndNullify(),
						}
					}).ToArray()
				};

				yield return (threadPointer, thread);
			}
		}
	}

	public class PostObject
	{
		public string AuthorName { get; set; }
		public string AuthorTrip { get; set; }
		public string Subject { get; set; }

		public string Email { get; set; }

		public string HtmlContents { get; set; }

		public string Capcode { get; set; }

		public ulong PostId { get; set; }

		public ulong? TimestampUtc { get; set; }
		public string TimestampText { get; set; }



		public string Md5String { get; set; }
		public string MediaFilename { get; set; }

		public string MediaSizeInfo { get; set; }
		public string ThumbSizeInfo { get; set; }
		public string RawThumbSizeInfo { get; set; }

		public string FileUrl { get; set; }
		public string ThumbnailUrl { get; set; }

		public bool? FileDeleted { get; set; }
	}
}