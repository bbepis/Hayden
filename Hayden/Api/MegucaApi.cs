using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;
using Newtonsoft.Json;

namespace Hayden
{
	public class MegucaApi : BaseApi<MegucaThread>
	{
		// There used to be a JSON API for Meguca, but it was abandoned because imageboard owners did not like having their site scraped
		// https://github.com/bakape/meguca/blob/8cd63e0a97cd60241cd290689e0fcac070c7473f/server/router.go
		// There is also a websocket API, however it is close to useless for what we're trying to do

		public string ImageboardWebsite { get; }

		public MegucaApi(string imageboardWebsite)
		{
			ImageboardWebsite = imageboardWebsite;

			if (!imageboardWebsite.EndsWith("/"))
				ImageboardWebsite += "/";
		}

		/// <inheritdoc />
		public override bool SupportsArchive => false;

		/// <inheritdoc />
		public override async Task<ApiResponse<MegucaThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default)
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
}