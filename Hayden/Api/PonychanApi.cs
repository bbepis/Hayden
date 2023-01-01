using System;
using System.Collections.Generic;
using System.IO;
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
	public class PonychanApi : BaseApi<PonychanThread>
	{
		public string ImageboardWebsite { get; }

		public PonychanApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override Task<ApiResponse<PonychanThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			return MakeJsonApiCall<PonychanThread>(new Uri($"{ImageboardWebsite}api.php?req=thread&board={board}&thread={threadNumber}"), client, modifiedSince, cancellationToken);
		}

		protected override Thread ConvertThread(PonychanThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.OriginalPost.Subject,
				IsArchived = thread.OriginalPost.Closed ?? false,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(board, ImageboardWebsite)).ToArray(),
				AdditionalMetadata = new JObject
				{
					["sticky"] = thread.OriginalPost.Sticky
				}
			};
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<PonychanCatalogItem[]>(new Uri($"{ImageboardWebsite}api.php?req=catalog&board={board}"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);

			return new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data
				.SelectMany(x => x.Threads)
				.Select(x => new PageThread(x.PostNumber,
					Math.Max(x.UnixTimestamp, x.LastReplies.Max(y => y.UnixTimestamp, 0)),
					x.Subject,
					x.Comment))
				.ToArray());
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}
	}

	public class PonychanThread
	{
		[JsonProperty("posts")]
		public List<PonychanPost> Posts { get; set; }

		[JsonIgnore]
		public PonychanPost OriginalPost => Posts[0];
	}

	public class PonychanPost
	{
		[JsonProperty("no")]
		public ulong PostNumber { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("sticky")]
		public bool? Sticky { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("closed")]
		public bool? Closed { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("mature")]
		public bool? Mature { get; set; }

		[JsonConverter(typeof(BoolIntConverter))]
		[JsonProperty("anon")]
		public bool? Anonymous { get; set; }

		[JsonProperty("time")]
		public uint UnixTimestamp { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("trip")]
		public string Trip { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("capcode")]
		public string Capcode { get; set; }

		[JsonProperty("sub")]
		public string Subject { get; set; }

		[JsonProperty("com")]
		public string Comment { get; set; }

		[JsonProperty("file")]
		public string ServerFilename { get; set; }

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
		
		private static readonly string[] preservedExtensionTypes =
		{
			// https://bitbucket.org/ponychan/ponychan-tinyboard/src/c2ad54a1360f92caae4f560fbb61abe28ed1c43f/core/inc/post/create.php#lines-434
			// https://bitbucket.org/ponychan/ponychan-tinyboard/src/master/core/inc/image.php
			".jpg",
			".png",
			".gif",
			".webp"
			// jpeg gets turned into jpg
		};

		public Post ConvertToPost(string board, string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			if (FileMd5 != null)
			{
				var thumbnailExtension = preservedExtensionTypes.Contains(FileExtension.ToLower())
					? FileExtension
					: ".jpg";

				media = new[]
				{
					new Media
					{
						FileUrl = $"{imageboardUrlRoot}{board}/src/{ServerFilename}",
						ThumbnailUrl = $"{imageboardUrlRoot}{board}/thumb/{Path.ChangeExtension(ServerFilename, thumbnailExtension)}",
						Filename = Path.GetFileName(OriginalFilename),
						FileExtension = FileExtension,
						ThumbnailExtension = thumbnailExtension,
						Index = 0,
						FileSize = FileSize.Value,
						IsDeleted = false, // Ponychan API does not expose this
						IsSpoiler = null, // Ponychan API does not expose this
						Md5Hash = Convert.FromBase64String(FileMd5),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};
			}

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds(UnixTimestamp),
				Author = Name,
				Tripcode = Trip,
				Email = Email,
				ContentRendered = null,
				ContentRaw = Comment,
				ContentType = ContentType.Ponychan,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = Common.SerializeObject(new
				{
					capcode = Capcode,
					ponychan_mature = Mature,
					ponychan_anonymous = Anonymous
				})
			};
		}
	}

	public class PonychanCatalogItem
	{
		[JsonProperty("page")]
		public int Page { get; set; }

		[JsonProperty("threads")]
		public PonychanCatalogThread[] Threads { get; set; }

		public class PonychanCatalogThread : PonychanPost
		{
			[JsonProperty("last_replies")]
			public PonychanPost[] LastReplies { get; set; }
		}
	}
}