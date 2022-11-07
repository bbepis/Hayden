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

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to LynxChan API.
	/// </summary>
	public class LynxChanApi : BaseApi<LynxChanThread>
	{
		public string ImageboardWebsite { get; }

		public LynxChanApi(SourceConfig sourceConfig)
		{
			ImageboardWebsite = sourceConfig.ImageboardWebsite;

			if (!ImageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		protected override HttpRequestMessage CreateRequest(Uri uri, DateTimeOffset? modifiedSince)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.IfModifiedSince = modifiedSince;
			request.Headers.Referrer = new Uri(ImageboardWebsite);

			return request;
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false; // should be changed later

		/// <inheritdoc />
		protected override async Task<ApiResponse<LynxChanThread>> GetThreadInternal(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var rawThreadResponse = await MakeJsonApiCall<LynxChanRawThread>(new Uri($"{ImageboardWebsite}{board}/res/{threadNumber}.json"), client, modifiedSince, cancellationToken);
			
			if (rawThreadResponse.ResponseType != ResponseType.Ok)
				return new ApiResponse<LynxChanThread>(rawThreadResponse.ResponseType, null);

			var rawThread = rawThreadResponse.Data;

			var opPost = rawThread.MapToPost();

			if (rawThread.Posts == null)
				rawThread.Posts = new List<LynxChanPost>();

			rawThread.Posts.Insert(0, opPost);

			var thread = rawThread.MapToThread();

			return new ApiResponse<LynxChanThread>(ResponseType.Ok, thread);
		}

		protected override Thread ConvertThread(LynxChanThread thread, string board)
		{
			return new Thread
			{
				ThreadId = thread.OriginalPost.PostNumber,
				Title = thread.Subject,
				IsArchived = thread.Locked,
				OriginalObject = thread,
				Posts = thread.Posts.Select(x => x.ConvertToPost(ImageboardWebsite)).ToArray(),
				AdditionalMetadata = null
			};
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<LynxChanCatalogItem[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);
			
			var response = new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data.Select(x =>
					new PageThread(x.threadId, (ulong)x.lastBump.ToUnixTimeSeconds(), x.subject, x.message))
				.ToArray());

			return response;
		}

		/// <inheritdoc />
		public override Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Does not support archives");
		}

		private struct LynxChanCatalogItem
		{
			public DateTimeOffset lastBump;
			public string message;
			public ulong threadId;
			public string subject;
		}

		private class LynxChanRawThread : LynxChanThread
		{
			[JsonProperty("postId")]
			public ulong PostNumber { get; set; }

			[JsonProperty("creation")]
			public DateTimeOffset CreationDateTime { get; set; }

			[JsonProperty("markdown")]
			public string Markdown { get; set; }

			[JsonProperty("message")]
			public string Message { get; set; }

			[JsonProperty("email")]
			public string Email { get; set; }

			[JsonProperty("name")]
			public string Name { get; set; }

			[JsonProperty("signedRole")]
			public string SignedRole { get; set; }

			[JsonProperty("files")]
			public LynxChanPostFile[] Files { get; set; }

			public LynxChanPost MapToPost()
			{
				return new LynxChanPost
				{
					CreationDateTime = CreationDateTime,
					Email = Email,
					Files = Files,
					Markdown = Markdown,
					Message = Message,
					Name = Name,
					PostNumber = ThreadId,
					Subject = Subject,
					SignedRole = SignedRole
				};
			}

			public LynxChanThread MapToThread()
			{
				return new LynxChanThread
				{
					Posts = Posts,
					Archived = Archived,
					AutoSage = AutoSage,
					Cyclic = Cyclic,
					Locked = Locked,
					Pinned = Pinned,
					ThreadId = ThreadId,
					Subject = Subject
				};
			}
		}
	}

	public class LynxChanThread
	{
		[JsonProperty("posts")]
		public List<LynxChanPost> Posts { get; set; }

		[JsonIgnore]
		public LynxChanPost OriginalPost => Posts[0];

		[JsonProperty("threadId")]
		public ulong ThreadId { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }

		[JsonProperty("archived")]
		public bool Archived { get; set; }

		[JsonProperty("locked")]
		public bool Locked { get; set; }

		[JsonProperty("pinned")]
		public bool Pinned { get; set; }

		[JsonProperty("cyclic")]
		public bool Cyclic { get; set; }

		[JsonProperty("autoSage")]
		public bool AutoSage { get; set; }
	}

	public class LynxChanPost
	{
		[JsonProperty("postId")]
		public ulong PostNumber { get; set; }

		[JsonProperty("subject")]
		public string Subject { get; set; }

		[JsonProperty("creation")]
		public DateTimeOffset CreationDateTime { get; set; }

		[JsonProperty("markdown")]
		public string Markdown { get; set; }

		[JsonProperty("message")]
		public string Message { get; set; }

		[JsonProperty("id")]
		public string PosterID { get; set; }

		[JsonProperty("flagCode")]
		public string FlagCode { get; set; }

		[JsonProperty("flagName")]
		public string FlagName { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("signedRole")]
		public string SignedRole { get; set; }

		[JsonProperty("files")]
		public LynxChanPostFile[] Files { get; set; }

		private static readonly JsonSerializer jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

		public Post ConvertToPost(string imageboardUrlRoot)
		{
			Media[] media = Array.Empty<Media>();

			if (Files != null)
			{
				media = Files.Select((file, i) => new Media
				{
					FileUrl = $"{imageboardUrlRoot}{file.Path.Substring(1)}",
					ThumbnailUrl = $"{imageboardUrlRoot}{file.ThumbnailUrl.Substring(1)}",
					Filename = Path.GetFileNameWithoutExtension(file.OriginalName),
					FileExtension = Path.GetExtension(file.OriginalName),
					ThumbnailExtension = Path.GetExtension(file.OriginalName),
					Index = (byte)i,
					FileSize = (uint)file.FileSize,
					IsDeleted = false, // not exposed by API
					IsSpoiler = null, // not exposed by API
					// no reliable hashes from this API
					OriginalObject = this,
					AdditionalMetadata = null
				}).ToArray();
			}

			return new Post
			{
				PostNumber = PostNumber,
				TimePosted = CreationDateTime,
				Author = Name,
				Tripcode = null, // ?
				Email = Email,
				ContentRendered = Markdown,
				ContentRaw = Message,
				ContentType = ContentType.LynxChan,
				Media = media,
				OriginalObject = this,
				AdditionalMetadata = JObject.FromObject(new
				{
					posterId = PosterID,
					capcode = SignedRole, // i think this is correct?
					flagCode = FlagCode,
					flagName = FlagName
				}, jsonSerializer)
			};
		}
	}

	public class LynxChanPostFile
	{
		[JsonProperty("originalName")]
		public string OriginalName { get; set; }

		[JsonProperty("path")]
		public string Path { get; set; }

		[JsonProperty("thumb")]
		public string ThumbnailUrl { get; set; }

		[JsonProperty("mime")]
		public string MimeType { get; set; }

		[JsonProperty("size")]
		public ulong FileSize { get; set; }

		[JsonProperty("width")]
		public ulong? Width { get; set; }

		[JsonProperty("height")]
		public ulong? Height { get; set; }

		[JsonIgnore]
		public string DirectPath => Path.Substring(Path.LastIndexOf('/') + 1);

		[JsonIgnore]
		public string DirectThumbPath => ThumbnailUrl?.Substring(Path.LastIndexOf('/') + 1);
	}
}