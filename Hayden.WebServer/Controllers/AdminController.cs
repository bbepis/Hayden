using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Logic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nest;

namespace Hayden.WebServer.Controllers
{
	[Route("admin")]
	[AdminAccessFilter(ModeratorRole.Admin, ModeratorRole.Developer)]
	public class AdminController : Controller
	{
		[HttpGet]
		public IActionResult Index()
		{
			return View("~/View/Svelte.cshtml");
		}

		public static FormattedString CurrentStatus { get; set; } = "Idle";
		public static float Progress { get; set; }

		public static Task CurrentTask { get; set; }

		[HttpGet("GetProgress")]
		public IActionResult GetProgress()
		{
			return Json(new { CurrentStatus = CurrentStatus.ToString(), Progress });
		}


		private IActionResult StartTask(Func<IServiceProvider, Task> action, IServiceProvider serviceProvider)
		{
			if (!HttpContext.User.IsLoggedIn())
				Unauthorized();

			if (CurrentTask?.IsCompleted == false)
				return StatusCode(StatusCodes.Status102Processing);
			
			var newScope = serviceProvider.CreateScope();

			CurrentTask = Task.Run(async () =>
			{
				try
				{
					using (newScope)
						await action(newScope.ServiceProvider);
				}
				catch (Exception ex)
				{
					CurrentStatus = $"EXCEPTION: {ex.Demystify()}";
				}
			});

			return StatusCode(StatusCodes.Status202Accepted);
		}

		[Route("reindex")]
		[HttpGet]
		public IActionResult Reindex([FromServices] IServiceProvider serviceProvider)
		{
			return StartTask(async provider =>
			{
				Progress = 0;
				CurrentStatus = "Initializing";

				var elasticClient = provider.GetRequiredService<ElasticClient>();
				var dbContext = provider.GetRequiredService<HaydenDbContext>();

				CurrentStatus = "Deleting index";
				var deleteResponse = await elasticClient.Indices.DeleteAsync(Indices.Index<PostIndex>());

				if (!deleteResponse.IsValid && deleteResponse.ApiCall?.HttpStatusCode != 404)
				{
					CurrentStatus = $"Failed: {deleteResponse.OriginalException}";
					return;
				}

				//Startup.StartupLogger.Log(LogLevel.Information, deleteResponse.DebugInformation);

				CurrentStatus = "Creating index";
				var createIndexResponse = await elasticClient.Indices.CreateAsync(PostIndex.IndexName, c => c
					.Map<PostIndex>(m => m.AutoMap())
				);

				// Startup.StartupLogger.Log(LogLevel.Information, createIndexResponse.DebugInformation);

				int reindexCount = 0;

				const int batchSize = 20000;
				const int subBatchSize = 100;

				var threadSubjects = await dbContext.Threads.Where(x => x.Title != null).ToDictionaryAsync(x => (x.BoardId, x.ThreadId), x => x.Title);

				IQueryable<DBPost> postQuery = dbContext.Posts.AsNoTracking()
					.Where(x => x.ContentHtml != null || x.ContentRaw != null);

				DBPost[] buffer = new DBPost[batchSize];

				int total = await postQuery.CountAsync();

				int currentIndex = 0;

				while (true)
				{
					var batchLength = await postQuery.OrderBy(x => x.PostId).Skip(currentIndex * batchSize).Take(batchSize).AsAsyncEnumerable().FillAsync(buffer);

					if (batchLength == 0)
						break;

					foreach (var subBatch in buffer.Take(batchLength).Batch(subBatchSize))
					{
						CurrentStatus = $"Reindexing PostIndex ({reindexCount++ * subBatchSize} / {total})";
						Progress = (reindexCount * subBatchSize) / (float)total;

						var response = await elasticClient.IndexManyAsync(subBatch
							.Select(x => new PostIndex()
							{
								PostId = x.PostId,
								ThreadId = x.ThreadId,
								BoardId = x.BoardId,
								//PostHtmlText = x.ContentHtml,
								PostRawText = x.ContentRaw,
								PostDateUtc = x.DateTime,
								//Subject = threadSubjects.TryGetValue((x.BoardId, x.ThreadId), out var subject) ? subject : null,
								IsOp = x.ThreadId == x.PostId
							}));

						//if (reindexCount == 1)
						//	// Startup.StartupLogger.Log(LogLevel.Information, response.DebugInformation);

						//if (response.ItemsWithErrors.Any())
						//	".".Trim();
					}

					currentIndex++;
				}

				Progress = 1;
				CurrentStatus = "Done";
			}, serviceProvider);
		}
	}

	public class FormattedString : IFormattable
	{
		public string Template { get; set; }

		public object[] Arguments { get; set; }

		public string ToString(string format, IFormatProvider formatProvider)
		{
			if (Arguments == null)
				return Template;

			return string.Format(Template, Arguments);
		}

		public override string ToString()
		{
			if (Arguments == null)
				return Template;

			return string.Format(Template, Arguments);
		}

		public object this[int i]
		{
			get => Arguments[i];
			set => Arguments[i] = value;
		}

		public void SetBox<T>(int index, T value) where T : struct
		{
			((Box<T>)this[index]).Value = value;
		}

		public static implicit operator FormattedString(FormattableString formattableString) => new FormattedString
		{
			Template = formattableString.Format,
			Arguments = formattableString.GetArguments()
		};

		public static implicit operator FormattedString(string str) => new FormattedString
		{
			Template = str,
			Arguments = null
		};
	}

	public class Box<T> where T : struct
	{
		public T Value { get; set; }

		public override string ToString()
		{
			return Value.ToString();
		}

		public Box(T value)
		{
			Value = value;
		}

		public static implicit operator Box<T>(T value) => new Box<T>(value);
	}
}
