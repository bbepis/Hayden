using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Newtonsoft.Json;

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

		/// <inheritdoc />
		public override bool SupportsArchive => false; // should be changed later

		/// <inheritdoc />
		public override async Task<ApiResponse<LynxChanThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var rawThreadResponse = await MakeJsonApiCall<LynxChanRawThread>(new Uri($"{ImageboardWebsite}{board}/res/{threadNumber}.json"), client, modifiedSince, cancellationToken);
			
			if (rawThreadResponse.ResponseType != ResponseType.Ok)
				return new ApiResponse<LynxChanThread>(rawThreadResponse.ResponseType, null);

			var rawThread = rawThreadResponse.Data;

			var opPost = new LynxChanPost()
			{
				CreationDateTime = rawThread.CreationDateTime,
				Email = rawThread.Email,
				Files = rawThread.Files,
				Markdown = rawThread.Markdown,
				Message = rawThread.Message,
				Name = rawThread.Name,
				PostNumber = rawThread.ThreadId,
				Subject = rawThread.Subject,
				SignedRole = rawThread.SignedRole
			};

			rawThread.Posts.Insert(0, opPost);

			var thread = new LynxChanThread()
			{
				Posts = rawThread.Posts,
				Archived = rawThread.Archived,
				AutoSage = rawThread.AutoSage,
				Cyclic = rawThread.Cyclic,
				IsDeleted = rawThread.IsDeleted,
				Locked = rawThread.Locked,
				Pinned = rawThread.Pinned,
				ThreadId = rawThread.ThreadId,
				Title = rawThread.Title
			};

			return new ApiResponse<LynxChanThread>(ResponseType.Ok, thread);
		}

		/// <inheritdoc />
		public override async Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
		{
			var result = await MakeJsonApiCall<LynxChanCatalogItem[]>(new Uri($"{ImageboardWebsite}{board}/catalog.json"), client, modifiedSince, cancellationToken);

			if (result.ResponseType != ResponseType.Ok)
				return new ApiResponse<PageThread[]>(result.ResponseType, null);
			
			var response = new ApiResponse<PageThread[]>(ResponseType.Ok, result.Data.Select(x =>
				{
					return new PageThread(x.threadId, (ulong)x.lastBump.ToUnixTimeSeconds(), x.subject, x.message);
				})
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

			[JsonProperty("subject")]
			public string Subject { get; set; }

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
		}
	}
}