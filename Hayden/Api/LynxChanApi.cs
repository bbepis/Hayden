using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Newtonsoft.Json;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null

namespace Hayden
{
	/// <summary>
	/// Class that handles requests to LynxChan API.
	/// </summary>
	public class LynxChanApi : BaseApi<LynxChanThread>
	{
		public string ImageboardWebsite { get; }

		public LynxChanApi(string imageboardWebsite)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
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
		public override async Task<ApiResponse<LynxChanThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
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
					IsDeleted = IsDeleted,
					Locked = Locked,
					Pinned = Pinned,
					ThreadId = ThreadId,
					Subject = Subject
				};
			}
		}
	}
}