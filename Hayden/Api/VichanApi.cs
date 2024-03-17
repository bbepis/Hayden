using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Thread = Hayden.Models.Thread;

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to the 4chan API.
	/// </summary>
	public class VichanApi : BaseApi<VichanThread>
	{
		public string ImageboardWebsite { get; }

		public VichanApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override Task<ApiResponse<VichanThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeJsonApiCall<VichanThread>(new Uri($"{ImageboardWebsite}{board}/thread/{threadNumber}.json"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(VichanThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.OriginalPost.Subject,
				IsArchived = thread.Archived,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(board, ImageboardWebsite)).ToArray(),
				AdditionalMetadata = new()
				{
					Sticky = thread.OriginalPost.Sticky.GetValueOrDefault()
				}
			};
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<Page[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			return new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data.SelectMany(x => x.Threads).ToArray());
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

		[JsonProperty("custom_spoiler")]
		public byte? CustomSpoiler { get; set; }

		[JsonProperty("replies")]
		public uint? TotalReplies { get; set; }

		[JsonProperty("images")]
		public ushort? TotalImages { get; set; }

		[JsonProperty("extra_files")]
		public List<VichanExtraFile> ExtraFiles { get; set; }
		
		public Post ConvertToPost(string board, string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			if (FileMd5 != null)
			{
				var mediaList = new List<Media>
				{
					new Media
					{
						FileUrl = $"{imageboardUrlRoot}{board}/src/{TimestampedFilename}{FileExtension}",
						// Thumbnails on Vichan are FUCKED. They're typically .jpg, but can be
						// other formats such as .webp depending on the full file extension, Vichan version & fork
						// It's not possible to determine this through the API
						ThumbnailUrl = $"{imageboardUrlRoot}{board}/thumb/{TimestampedFilename}.jpg",
						Filename = OriginalFilename,
						FileExtension = FileExtension,
						ThumbnailExtension = "jpg",
						Index = 0,
						FileSize = FileSize.Value,
						IsDeleted = false, // Vichan API does not expose this
						IsSpoiler = null, // Vichan API does not expose this
						Md5Hash = Convert.FromBase64String(FileMd5),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};

				if (ExtraFiles != null)
				{
					mediaList.AddRange(ExtraFiles.Select((file, i) => new Media
					{
						FileUrl = $"{imageboardUrlRoot}{board}/src/{file.TimestampedFilename}{file.FileExtension}",
						// Thumbnails on Vichan are FUCKED. They're typically .jpg, but can be
						// other formats such as .webp depending on the full file extension, Vichan version & fork
						// It's not possible to determine this through the API
						ThumbnailUrl = $"{imageboardUrlRoot}{board}/thumb/{file.TimestampedFilename}.jpg",
						Filename = file.OriginalFilename,
						FileExtension = file.FileExtension,
						ThumbnailExtension = "jpg",
						Index = (byte)i,
						FileSize = file.FileSize,
						IsDeleted = false, // Vichan API does not expose this
						IsSpoiler = null, // Vichan API does not expose this
						Md5Hash = Convert.FromBase64String(file.FileMd5),
						OriginalObject = this,
						AdditionalMetadata = new()
						{
							CustomSpoiler = CustomSpoiler
						}
					}));
				}

				media = mediaList.ToArray();
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