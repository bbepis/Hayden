using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.Models;
using Newtonsoft.Json;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the 4chan API.
	/// </summary>
	public class VichanApi : BaseApi<VichanThread>
	{
		public string ImageboardWebsite { get; }
		private Uri ImageboardUri { get; }

		public VichanApi(SourceConfig sourceConfig) : base(sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";

			ImageboardUri = new Uri(ImageboardWebsite);
		}

		public override Task<ApiCapabilities> DetermineCapabilitiesAsync(HttpClient client)
		{
			return Task.FromResult(new ApiCapabilities()
			{
				SupportsArchive = false,
				SupportsBoardLastModified = true
			});
		}

		/// <inheritdoc />
		protected override Task<ApiResponse<VichanThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			string url;

			if (ImageboardUri.Host == "lainchan.org")
				url = $"{ImageboardWebsite}{board}/res/{threadNumber}.json";
			else
				url = $"{ImageboardWebsite}{board}/thread/{threadNumber}.json";

			return MakeJsonApiCall<VichanThread>(new Uri(url), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(VichanThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.OriginalPost.Subject,
				ArchivedTime = thread.Archived ? DateTimeOffset.MinValue : null,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(board, ImageboardWebsite)).ToArray(),
				AdditionalMetadata = new()
				{
					Sticky = thread.OriginalPost.Sticky.GetValueOrDefault()
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

	public class VichanThread
	{
		[JsonProperty("posts")]
		public List<VichanPost> Posts { get; set; }

		[JsonIgnore]
		public VichanPost OriginalPost => Posts[0];

		[JsonProperty]
		public bool Archived { get; set; }
	}

	public class VichanPost
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

		[JsonProperty("tim")]
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
		public string Embed { get; set; }

		[JsonProperty("replies")]
		public uint? TotalReplies { get; set; }

		[JsonProperty("images")]
		public ushort? TotalImages { get; set; }

		[JsonProperty("extra_files")]
		public List<VichanExtraFile> ExtraFiles { get; set; }
		
		public Post ConvertToPost(string board, string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			// Thumbnails on Vichan are FUCKED. They're typically .jpg, but can be
			// other formats such as .webp depending on the full file extension, Vichan version & fork
			// It's not possible to determine this through the API
			var thumbnailExtension = "jpg";

			if (imageboardUrlRoot.Contains("lainchan.org"))
				thumbnailExtension = "png";


			if (FileMd5 != null)
			{
				var isDeleted = TimestampedFilename == "deleted" || TimestampedFilename == "";

				var mediaList = new List<Media>
				{
					new Media
					{
						FileUrl = !isDeleted ? $"{imageboardUrlRoot}{board}/src/{TimestampedFilename}{FileExtension}" : null,
						ThumbnailUrl = !isDeleted ? $"{imageboardUrlRoot}{board}/thumb/{TimestampedFilename}.{thumbnailExtension}" : null,
						TimestampedFilename = !isDeleted ? TimestampedFilename : null,
						Filename = OriginalFilename,
						FileExtension = FileExtension,
						ThumbnailExtension = thumbnailExtension,
						Index = 0,
						FileSize = FileSize.Value,
						ImageHeight = ImageHeight,
						ImageWidth = ImageWidth,
						IsDeleted = isDeleted,
						IsSpoiler = false, // Vichan API does not expose this
						Md5Hash = Convert.FromBase64String(FileMd5),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};

				if (ExtraFiles != null)
				{
					mediaList.AddRange(ExtraFiles.Select((file, i) =>
					{
						isDeleted = TimestampedFilename == "deleted" || TimestampedFilename == "";

						return new Media
						{
							FileUrl = !isDeleted ? $"{imageboardUrlRoot}{board}/src/{file.TimestampedFilename}{file.FileExtension}" : null,
							ThumbnailUrl = !isDeleted ? $"{imageboardUrlRoot}{board}/thumb/{file.TimestampedFilename}.{thumbnailExtension}" : null,
							TimestampedFilename = !isDeleted ? file.TimestampedFilename : null,
							Filename = file.OriginalFilename,
							FileExtension = file.FileExtension,
							ThumbnailExtension = thumbnailExtension,
							Index = (byte)(i + 1),
							FileSize = file.FileSize,
							ImageHeight = file.ImageHeight,
							ImageWidth = file.ImageWidth,
							IsDeleted = isDeleted,
							IsSpoiler = false, // Vichan API does not expose this
							Md5Hash = Convert.FromBase64String(file.FileMd5),
							OriginalObject = this,
							AdditionalMetadata = null
						};
					}));
				}

				media = mediaList.ToArray();
			}
			else if (!string.IsNullOrEmpty(Embed))
			{
				media = new[]
				{
					new Media
					{
						Index = 0,
						AdditionalMetadata = new()
						{
							ExternalMediaUrl = Embed
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
				ContentRendered = Comment,
				ContentRaw = null,
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

	public class VichanExtraFile
	{
		[JsonProperty("tim")]
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
	}
}