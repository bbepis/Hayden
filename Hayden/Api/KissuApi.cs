using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the 4chan API.
	/// </summary>
	public class KissuApi : BaseApi<KissuThread>
	{
		public string ImageboardWebsite { get; }

		public KissuApi(SourceConfig sourceConfig) : base(sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite ?? "https://kissu.moe/";

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		public override async Task<ApiCapabilities> DetermineCapabilitiesAsync(HttpClient client)
		{
			var htmlPage = await MakeStringCall(new Uri(ImageboardWebsite), client);

			string[] boardList = null;

			if (htmlPage.ResponseType == ResponseType.Ok)
			{
				var foundJson = Regex.Match(htmlPage.Data, @"window\.site_json=(.*?})\s*;");
				if (foundJson.Success)
				{
					var obj = JObject.Parse(foundJson.Groups[1].Value.Replace("\\>", ">").Replace("\\<", "<"));

					boardList = ((JArray)obj["boards"]).Select(x => x.Value<string>("name")).ToArray();
				}
			}

			return new ApiCapabilities()
			{
				SupportsArchive = false,
				SupportsBoardLastModified = true,
				SupportsBoardListing = boardList != null,
				BoardList = boardList
			};
		}

		/// <inheritdoc />
		protected override async Task<ApiResponse<KissuThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var response = await MakeJsonApiCall<KissuThread[]>(new Uri($"{ImageboardWebsite}api/threads/{board}/{threadNumber}/full.json?_t={872601010}"), client, modifiedSince, cancellationToken);

			if (response.ResponseType != ResponseType.Ok)
				return new ApiResponse<KissuThread>(response.ResponseType, null);

			return new ApiResponse<KissuThread>(ResponseType.Ok, response.Data[0]);
		}

		protected override Thread ConvertThread(KissuThread thread, string board)
		{
			var op = thread.ConvertToPost(board, ImageboardWebsite);

			return new Thread
			{
				ThreadId = op.PostNumber,
				Title = thread.Subject,
				ArchivedTime = thread.Archived ? DateTimeOffset.MinValue : null,
				OriginalObject = thread,
				Posts = [op, ..(thread.Posts?.Select(x => x.ConvertToPost(board, ImageboardWebsite)) ?? [])],
				AdditionalMetadata = new()
				{
					Sticky = thread.Sticky.GetValueOrDefault()
				}
			};
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<ThreadOverviewInfo[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<Page[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<ThreadOverviewInfo[]>(result.ResponseType, null);

			var info = result.Data
				.SelectMany(x => x.Threads)
				.Select((x, i) => new ThreadOverviewInfo
				{
					ThreadId = x.ThreadNumber,
					ContentHtml = x.Html,
					Subject = x.Subject,
					LastModified = DateTimeOffset.FromUnixTimeSeconds((long)x.LastModified),
					Position = i,
					ReplyCount = null
				})
				.ToArray();

			return new ApiResponse<ThreadOverviewInfo[]>(ResponseType.Ok, info);
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}
	}

	public class KissuThread : KissuPost
	{
		[JsonProperty("last_replies")]
		public List<KissuPost> Posts { get; set; }

		[JsonIgnore]
		public KissuPost OriginalPost => Posts[0];

		[JsonProperty]
		public bool Archived { get; set; }
	}

	public class KissuEmbed
	{
		[JsonProperty("site")]
		public string Site { get; set; }

		[JsonProperty("code")]
		public string Code { get; set; }

		[JsonProperty("inputURL")]
		public string InputURL { get; set; }
	}

	public class KissuPost
	{
		[JsonProperty("no")]
		public ulong PostNumber { get; set; }

		[JsonProperty("resto")]
		public ulong ReplyPostNumber { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("sticky")]
		public bool? Sticky { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("closed")]
		public bool? Closed { get; set; }

		[JsonProperty("cyclical")]
		public string Cyclical { get; set; }

		[JsonProperty("time")]
		public uint UnixTimestamp { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("trip")]
		public string Trip { get; set; }

		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonProperty("country")]
		public string CountryCode { get; set; }

		[JsonProperty("sub")]
		public string Subject { get; set; }

		[JsonProperty("com")]
		public string Comment { get; set; }

		[JsonProperty("file_id")]
		public string TimestampedFilename { get; set; }

		[JsonProperty("filename")]
		public string OriginalFilename { get; set; }

		[JsonProperty("ext")]
		public string FileExtension { get; set; }

		[JsonProperty("fsize")]
		public uint? FileSize { get; set; }

		[JsonProperty("md5")]
		public string FileMd5 { get; set; }

		[JsonProperty("w")]
		public ushort? ImageWidth { get; set; }

		[JsonProperty("h")]
		public ushort? ImageHeight { get; set; }

		[JsonProperty("tn_w")]
		public ushort? ThumbnailWidth { get; set; }

		[JsonProperty("tn_h")]
		public ushort? ThumbnailHeight { get; set; }

		[JsonProperty("country_name")]
		public string CountryName { get; set; }

		[JsonProperty("embed")]
		public KissuEmbed Embed { get; set; }

		[JsonProperty("replies")]
		public uint? TotalReplies { get; set; }

		[JsonProperty("images")]
		public ushort? TotalImages { get; set; }

		[JsonProperty("location")]
		public string ImageSubdomain { get; set; }
		
		public Post ConvertToPost(string board, string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			var uri = new Uri(imageboardUrlRoot);

			var isDeleted = TimestampedFilename == "deleted" || TimestampedFilename == "";

			if (FileMd5 != null)
			{
				var mediaList = new List<Media>
				{
					new Media
					{
						FileUrl = !isDeleted ? $"{uri.Scheme}://{ImageSubdomain ?? "haiji"}.{uri.Host}/{board}/src/{TimestampedFilename}.{FileExtension}" : null,
						// Thumbnails on Vichan are FUCKED. They're typically .jpg, but can be
						// other formats such as .webp depending on the full file extension, Vichan version & fork
						// It's not possible to determine this through the API
						ThumbnailUrl = !isDeleted ? $"{uri.Scheme}://{"haiji"}.{uri.Host}/{board}/thumb/{TimestampedFilename}.webp" : null,
						TimestampedFilename = !isDeleted ? TimestampedFilename : null,
						Filename = OriginalFilename,
						FileExtension = FileExtension,
						ThumbnailExtension = "webp",
						Index = 0,
						FileSize = FileSize.Value,
						IsDeleted = isDeleted,
						IsSpoiler = false, // Vichan API does not expose this
						ImageHeight = ImageHeight,
						ImageWidth = ImageWidth,
						Md5Hash = Convert.FromHexString(FileMd5),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};

				media = mediaList.ToArray();
			}
			else if (Embed != null)
			{
				media = new[]
				{
					new Media
					{
						Index = 0,
						AdditionalMetadata = new()
						{
							ExternalMediaUrl = Embed.InputURL
						}
					}
				};
			}

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds(UnixTimestamp),
				Author = Name,
				Tripcode = Trip,
				Email = null,
				ContentRaw = Comment,
				ContentType = ContentType.Vichan,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = new()
				{
					Capcode = Capcode,
					CountryCode = CountryCode,
					CountryName = CountryName
				}
			};
		}
	}
}