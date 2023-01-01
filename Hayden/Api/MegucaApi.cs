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

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null

namespace Hayden
{
	public class MegucaApi : BaseApi<MegucaThread>
	{
		// There used to be a JSON API for Meguca, but it was abandoned because imageboard owners did not like having their site scraped
		// https://github.com/bakape/meguca/blob/8cd63e0a97cd60241cd290689e0fcac070c7473f/server/router.go
		// There is also a websocket API, however it is close to useless for what we're trying to do

		public string ImageboardWebsite { get; }

		public MegucaApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		protected override async Task<ApiResponse<MegucaThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var response = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/{threadNumber}"), client, modifiedSince, cancellationToken);

			if (response.ResponseType != ResponseType.Ok)
				return new ApiResponse<MegucaThread>(response.ResponseType, null);

			var document = response.Data;

			var json = document.GetElementById("post-data");

			var rawThread = JsonConvert.DeserializeObject<MegucaRawThread>(json.TextContent);

			document.Dispose();

			var opPost = rawThread.MapToPost();

			if (rawThread.Posts == null)
				rawThread.Posts = new List<MegucaPost>();

			rawThread.Posts.Insert(0, opPost);

			rawThread.Posts = rawThread.Posts.OrderBy(x => x.PostNumber).ToList();

			var thread = rawThread.MapToThread();

			return new ApiResponse<MegucaThread>(ResponseType.Ok, thread);
		}

		protected override Thread ConvertThread(MegucaThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.Subject,
				IsArchived = thread.Locked,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(board, ImageboardWebsite)).ToArray(),
				AdditionalMetadata = new JObject
				{
					["sticky"] = thread.Sticky
				}
			};
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var response = await MakeHtmlCall(new Uri($"{ImageboardWebsite}{board}/catalog"), client, modifiedSince, cancellationToken);

			if (response.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(response.ResponseType, null);

			var document = response.Data;

			var json = document.GetElementById("post-data");

			var catalog = JsonConvert.DeserializeObject<MegucaCatalog>(json.TextContent);

			var pageThreads = catalog.threads.Select(x =>
				new PageThread(x.id, x.bump_time, x.subject, x.body)).ToArray();

			return new ApiResponse<PageThread[]>(ResponseType.Ok, pageThreads);
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}

		// https://github.com/bakape/shamichan/blob/8e47f42785caa99bbbfd2b35221f47822dbec1f3/imager/common/images.go#L11
		public static Dictionary<uint, string> ExtensionMappings = new()
		{
			[0] = ".jpg",
			[1] = ".png",
			[2] = ".gif",
			[3] = ".webm",
			[4] = ".pdf",
			[5] = ".svg",
			[6] = ".mp4",
			[7] = ".mp3",
			[8] = ".ogg",
			[9] = ".zip",
			[10] = ".7z",
			[11] = ".tgz",
			[12] = ".txz",
			[13] = ".flac",
			[14] = "",
			[15] = ".txt",
			[16] = ".webp",
			[17] = ".rar",
			[18] = ".cbz",
			[19] = ".cbr",
			[20] = ".mp4", // HEVC
		};

		private class MegucaCatalog
		{
			public MegucaCatalogItem[] threads { get; set; }
		}

		private struct MegucaCatalogItem
		{
			public ulong bump_time;
			public string body;
			public ulong id;
			public string subject;
		}

		private class MegucaRawThread : MegucaThread
		{
			[JsonProperty("editing")]
			public bool Editing { get; set; }

			[JsonProperty("sage")]
			public bool Sage { get; set; }

			[JsonProperty("auth")]
			public uint Auth { get; set; }

			[JsonProperty("id")]
			public ulong PostNumber { get; set; }

			[JsonProperty("time")]
			public ulong PostTime { get; set; }

			[JsonProperty("body")]
			public string ContentBody { get; set; }

			[JsonProperty("flag")]
			public string Flag { get; set; }

			[JsonProperty("name")]
			public string AuthorName { get; set; }

			[JsonProperty("trip")]
			public string Tripcode { get; set; }

			[JsonProperty("image")]
			public MegucaPostImage Image { get; set; }

			public MegucaPost MapToPost()
			{
				return new MegucaPost
				{
					Editing = Editing,
					Sage = Sage,
					Auth = Auth,
					PostNumber = PostNumber,
					PostTime = PostTime,
					ContentBody = ContentBody,
					Flag = Flag,
					AuthorName = AuthorName,
					Tripcode = Tripcode,
					Image = Image
				};
			}

			public MegucaThread MapToThread()
			{
				return new MegucaThread
				{
					Abbreviated = Abbreviated,
					Sticky = Sticky,
					Locked = Locked,
					PostCount = PostCount,
					ImageCount = ImageCount,
					UpdateTime = UpdateTime,
					BumpTime = BumpTime,
					Subject = Subject,
					Posts = Posts
				};
			}
		}
	}

	public class MegucaThread
	{
		[JsonProperty("posts")]
		public List<MegucaPost> Posts { get; set; }

		[JsonIgnore]
		public MegucaPost OriginalPost => Posts[0];


		[JsonProperty("abbrev")]
		public bool Abbreviated { get; set; }

		[JsonProperty("sticky")]
		public bool Sticky { get; set; }

		[JsonProperty("locked")]
		public bool Locked { get; set; }

		[JsonProperty("post_count")]
		public ulong PostCount { get; set; }

		[JsonProperty("image_count")]
		public ulong ImageCount { get; set; }

		[JsonProperty("update_time")]
		public ulong UpdateTime { get; set; }

		[JsonProperty("bump_time")]
		public ulong BumpTime { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }
	}

	public class MegucaPost
	{
		[JsonProperty("editing")]
		public bool Editing { get; set; }

		[JsonProperty("sage")]
		public bool Sage { get; set; }

		[JsonProperty("auth")]
		public uint Auth { get; set; }

		[JsonProperty("id")]
		public ulong PostNumber { get; set; }

		[JsonProperty("time")]
		public ulong PostTime { get; set; }

		[JsonProperty("body")]
		public string ContentBody { get; set; }

		[JsonProperty("flag")]
		public string Flag { get; set; }

		[JsonProperty("name")]
		public string AuthorName { get; set; }

		[JsonProperty("trip")]
		public string Tripcode { get; set; }

		[JsonProperty("image")]
		public MegucaPostImage Image { get; set; }
		
		public Post ConvertToPost(string board, string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			if (Image != null)
				media = new[]
				{
					new Media
					{
						FileUrl = $"{imageboardUrlRoot}assets/images/src/{Image.Sha1Hash}{MegucaApi.ExtensionMappings[Image.FileType]}",
						ThumbnailUrl = $"{imageboardUrlRoot}assets/images/thumb/{Image.Sha1Hash}{MegucaApi.ExtensionMappings[Image.ThumbType]}",
						Filename = Image.Filename,
						FileExtension = MegucaApi.ExtensionMappings[Image.FileType],
						ThumbnailExtension = MegucaApi.ExtensionMappings[Image.ThumbType],
						Index = 0,
						FileSize = (uint)Image.FileSize,
						IsDeleted = false, // I don't know if images can be deleted without deleting the entire post? or even how to check
						IsSpoiler = Image.IsSpoiler,
						Md5Hash = Convert.FromBase64String(Image.Md5Hash),
						Sha1Hash= Convert.FromBase64String(Image.Sha1Hash),
						OriginalObject = this,
						AdditionalMetadata = null
					}
				};

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = DateTimeOffset.FromUnixTimeSeconds((long)PostTime),
				Author = AuthorName,
				Tripcode = Tripcode,
				Email = null,
				ContentRendered = null,
				ContentRaw = ContentBody,
				ContentType = ContentType.Meguca,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = Common.SerializeObject(new
				{
					flag = Flag
				})
			};
		}
	}

	public class MegucaPostImage
	{
		[JsonProperty("spoiler")]
		public bool IsSpoiler { get; set; }

		[JsonProperty("audio")]
		public bool Audio { get; set; }

		[JsonProperty("video")]
		public bool Video { get; set; }

		[JsonProperty("file_type")]
		public uint FileType { get; set; }

		[JsonProperty("thumb_type")]
		public uint ThumbType { get; set; }

		[JsonProperty("length")]
		public ulong Length { get; set; }

		[JsonProperty("dims")]
		public uint[] Dimensions { get; set; }

		[JsonProperty("size")]
		public ulong FileSize { get; set; }

		[JsonProperty("artist")]
		public string Artist { get; set; }

		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("md5")]
		public string Md5Hash { get; set; }

		[JsonProperty("sha1")]
		public string Sha1Hash { get; set; }

		[JsonProperty("name")]
		public string Filename { get; set; }
	}
}