using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.MediaInfo;
using Hayden.WebServer.Controllers.Api;
using Hayden.WebServer.DB.Elasticsearch;
using Hayden.WebServer.Logic;
using Hayden.WebServer.Logic.Importer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nest;
using Newtonsoft.Json.Linq;

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
		
		[HttpGet("StartRehash")]
		public IActionResult StartRename([FromServices] IServiceProvider serviceProvider)
		{
			return StartTask(async provider =>
			{
				CurrentStatus = "Reading files from database";

				await using var context = provider.GetRequiredService<HaydenDbContext>();
				var config = provider.GetRequiredService<IOptions<ServerConfig>>();

				var boards = await context.Boards.AsNoTracking().ToDictionaryAsync(x => x.Id);

				var files = await context.Files.AsNoTracking()
					.Where(x => x.Size != 0)
					.ToArrayAsync();

				var semaphore = new SemaphoreSlim(1);

				async Task Persist()
				{
					try
					{
						await semaphore.WaitAsync();

						await context.SaveChangesAsync().ConfigureAwait(true);
						context.DetachAllEntities();

						GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
						GC.Collect();
					}
					finally
					{
						semaphore.Release();
					}
				}

				float total = files.Length;
				int current = 0;

				CurrentStatus = (FormattableString)$"Renaming files ({new Box<int>(1)} / {files.Length})";

#if DEBUG
				const int concurrent = 1;
#else
				const int concurrent = 8;
#endif

				//await files.ForEachAsync(async file =>
				foreach (var file in files)
				{
					try
					{
						Progress = current / total;
						var localCurrent = Interlocked.Increment(ref current);
						CurrentStatus.SetBox(0, current);

						if (localCurrent % 100 == 0)
							await Persist();

						//var oldFilename = Common.CalculateFilename(config.Value.FileLocation,
						//	boards[file.BoardId].ShortName, Common.MediaType.Image, file.Md5Hash, file.Extension);

						//if (System.IO.File.Exists(oldFilename))
						//{
						//	var newFilename = Common.CalculateFilename(config.Value.FileLocation,
						//		boards[file.BoardId].ShortName, Common.MediaType.Image, file.Sha256Hash, file.Extension);

						//	System.IO.File.Move(oldFilename, newFilename);
						//}

						//oldFilename = Common.CalculateFilename(config.Value.FileLocation,
						//	boards[file.BoardId].ShortName, Common.MediaType.Thumbnail, file.Md5Hash, "jpg");

						//if (System.IO.File.Exists(oldFilename))
						//{
						//	var newFilename = Common.CalculateFilename(config.Value.FileLocation,
						//		boards[file.BoardId].ShortName, Common.MediaType.Thumbnail, file.Sha256Hash, "jpg");

						//	System.IO.File.Move(oldFilename, newFilename);
						//}

						var imageFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
							boards[file.BoardId].ShortName, Common.MediaType.Image, file.Sha256Hash, file.Extension);

						if (System.IO.File.Exists(imageFilename))
						{
							file.FileExists = true;
							context.Update(file);
						}

						var thumbFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
							boards[file.BoardId].ShortName, Common.MediaType.Thumbnail, file.Sha256Hash, "jpg");

						if (System.IO.File.Exists(thumbFilename))
						{
							file.ThumbnailExtension = "jpg";
							context.Update(file);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.ToStringDemystified());
					}
				}
				//}, concurrent);

				await Persist();

				Progress = 1;
				CurrentStatus = "Done";
			}, serviceProvider);
		}
		
		public IActionResult StartRemap([FromServices] IServiceProvider serviceProvider)
		{
			return StartTask(async provider =>
			{
				CurrentStatus = "Reading file mappings from database";

				await using var context = provider.GetRequiredService<HaydenDbContext>();
				var config = provider.GetRequiredService<IOptions<ServerConfig>>();

				var boards = await context.Boards.AsNoTracking().ToDictionaryAsync(x => x.Id);

				var fileMappings = await context.FileMappings.AsNoTracking()
					.GroupJoin(context.Files.AsNoTracking(),
						fileMapping => new { fileMapping.BoardId, FileId = fileMapping.FileId ?? 0 },
						file => new { file.BoardId, FileId = file.Id },
						(mapping, enumerable) => new { mapping, enumerable })
					.SelectMany(x => x.enumerable.DefaultIfEmpty(), (x, file) => new { x.mapping, file })
					.Where(x => x.file == null)
					.Select(x => x.mapping)
					.ToArrayAsync();

				//var files = await context.Files.AsNoTracking()
				//	.Where(x => fileMappings.Select(x => x.mapping.FileId).Contains(x.Id))
				//	.Distinct()
				//	.ToDictionaryAsync(x => x.Id);

				var semaphore = new SemaphoreSlim(1);

				async Task Persist()
				{
					try
					{
						await semaphore.WaitAsync();

						await context.SaveChangesAsync().ConfigureAwait(true);
						context.DetachAllEntities();

						GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
						GC.Collect();
					}
					finally
					{
						semaphore.Release();
					}
				}

				float total = fileMappings.Length;
				int current = 0;

				CurrentStatus = (FormattableString)$"Renaming files ({new Box<int>(1)} / {fileMappings.Length})";
				

				foreach (var fileMapping in fileMappings)
				{
					try
					{
						Progress = current / total;
						var localCurrent = Interlocked.Increment(ref current);
						CurrentStatus.SetBox(0, current);

						if (localCurrent % 100 == 0)
							await Persist();

						var files = await context.Files.AsNoTracking().Where(x =>
								x.Sha256Hash == context.Files.AsNoTracking()
									.First(y => y.Id == fileMapping.FileId).Sha256Hash)
							.ToArrayAsync();

						var actualReferencedFile = files.FirstOrDefault(x => x.BoardId == fileMapping.BoardId);

						uint newFileId;

						if (actualReferencedFile != null)
						{
							newFileId = actualReferencedFile.Id;
						}
						else
						{
							var oldFile = files.FirstOrDefault();
							if (oldFile == null)
								throw new Exception("why");

							var oldImageFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
								boards[oldFile.BoardId].ShortName,
								Common.MediaType.Image, oldFile.Sha256Hash, oldFile.Extension);
							var oldThumbFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
								boards[oldFile.BoardId].ShortName,
								Common.MediaType.Thumbnail, oldFile.Sha256Hash, "jpg");

							var newImageFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
								boards[fileMapping.BoardId].ShortName,
								Common.MediaType.Image, oldFile.Sha256Hash, oldFile.Extension);
							var newThumbFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
								boards[fileMapping.BoardId].ShortName,
								Common.MediaType.Thumbnail, oldFile.Sha256Hash, "jpg");

							if (!System.IO.File.Exists(newImageFilename))
								System.IO.File.Copy(oldImageFilename, newImageFilename);

							if (!System.IO.File.Exists(newThumbFilename))
							{
								if (!System.IO.File.Exists(oldThumbFilename))
								{
									var md5Filename = Common.CalculateFilename(config.Value.Data.FileLocation,
										boards[fileMapping.BoardId].ShortName, Common.MediaType.Thumbnail, oldFile.Md5Hash, "jpg");

									System.IO.File.Move(md5Filename, oldThumbFilename);
								}

								System.IO.File.Copy(oldThumbFilename, newThumbFilename);
							}

							var newFile = new DBFile
							{
								BoardId = fileMapping.BoardId,
								Extension = oldFile.Extension,
								Sha256Hash = oldFile.Sha256Hash,
								Sha1Hash = oldFile.Sha1Hash,
								Md5Hash = oldFile.Md5Hash,
								ImageHeight = oldFile.ImageHeight,
								ImageWidth = oldFile.ImageWidth,
								Size = oldFile.Size,
								AdditionalMetadata = oldFile.AdditionalMetadata
							};

							context.Add(newFile);

							await Persist();

							newFileId = newFile.Id;
						}

						context.Remove(fileMapping);

						var newFileMapping = new DBFileMapping
						{
							BoardId = fileMapping.BoardId,
							PostId = fileMapping.PostId,
							FileId = newFileId,
							Filename = fileMapping.Filename,
							Index = fileMapping.Index,
							IsDeleted = fileMapping.IsDeleted,
							IsSpoiler = fileMapping.IsSpoiler,
							AdditionalMetadata = fileMapping.AdditionalMetadata
						};


						context.Remove(fileMapping);
						context.Add(newFileMapping);

						await Persist();
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
					}
				}

				await Persist();

				Progress = 1;
				CurrentStatus = "Done";
			}, serviceProvider);
		}
		
		public IActionResult StartRehash([FromServices] IServiceProvider serviceProvider)
		{
			return StartTask(async provider =>
			{
				CurrentStatus = "Reading files from database";

				await using var context = provider.GetRequiredService<HaydenDbContext>();
				var config = provider.GetRequiredService<IOptions<ServerConfig>>();

				var boards = await context.Boards.AsNoTracking().ToDictionaryAsync(x => x.Id);

				var files = await context.Files.AsNoTracking()
					.Where(x => x.Size == 0)
					.ToArrayAsync();
				
				var semaphore = new SemaphoreSlim(1);

				async Task Persist()
				{
					try
					{
						await semaphore.WaitAsync();

						await context.SaveChangesAsync().ConfigureAwait(true);
						context.DetachAllEntities();

						GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
						GC.Collect();
					}
					finally
					{
						semaphore.Release();
					}
				}

				float total = files.Length;
				int current = 0;

				CurrentStatus = (FormattableString)$"Processing files ({new Box<int>(1)} / {files.Length})";

#if DEBUG
				const int concurrent = 1;
				const int persistPeriod = 1;
#else
				const int concurrent = 8;
				const int persistPeriod = 100;
#endif

				await files.ForEachAsync(async file =>
				{
					try
					{
						Progress = current / total;
						CurrentStatus.SetBox(0, Interlocked.Increment(ref current));

						var filename = Common.CalculateFilename(config.Value.Data.FileLocation,
							boards[file.BoardId].ShortName, Common.MediaType.Image, file.Sha256Hash, file.Extension);

						(DBFile _, bool md5Changed) = await FileImporterTools.UpdateDbFile(filename, provider.GetRequiredService<IMediaInspector>(), file);

						try
						{
							await semaphore.WaitAsync();

							if (md5Changed)
							{
								var newFilename = Common.CalculateFilename(config.Value.Data.FileLocation,
									boards[file.BoardId].ShortName, Common.MediaType.Image, file.Sha256Hash,
									file.Extension);

								var existingFile = await context.Files.FirstOrDefaultAsync(x =>
									                   x.BoardId == file.BoardId && x.Sha256Hash == file.Sha256Hash) ??
								                   context.Files.Local.FirstOrDefault(x =>
									                   x.BoardId == file.BoardId &&
									                   x.Sha256Hash.ByteArrayEquals(file.Sha256Hash));

								if (existingFile != null)
								{
									if (existingFile.Id == 0) // conflicts with new file that hasn't been pushed yet
										return;

									await context.Database.ExecuteSqlRawAsync(
										$"UPDATE file_mappings SET FileId = {existingFile.Id} WHERE FileId = {file.Id} AND BoardId = {file.BoardId}");

									//foreach (var invalidFileMapping in context.FileMappings.Where(x => x.BoardId == file.BoardId && x.FileId == file.Id))
									//{
									//	invalidFileMapping.FileId = existingFile.Id;
									//	context.Update(invalidFileMapping);
									//}

									if (!System.IO.File.Exists(newFilename))
									{
										System.IO.File.Copy(filename, newFilename);
									}


									//existingFile.OriginalMd5Hash = file.OriginalMd5Hash;

									var obj = existingFile.AdditionalMetadata ?? new JObject();

									const string key = "md5ConflictHistory";

									JArray array = !obj.TryGetValue(key, out var rawArray) ? new JArray() : (JArray)rawArray;

									array.Add(JObject.FromObject(new Md5Conflict(file.Md5Hash, existingFile.Md5Hash)));

									obj[key] = array;
									existingFile.AdditionalMetadata = obj;

									context.Update(existingFile);
									context.Remove(file);
								}
								else
								{
									if (!System.IO.File.Exists(newFilename))
										System.IO.File.Copy(filename, newFilename);

									context.Update(file);
								}
							}
							else
							{
								context.Update(file);
							}
						}
						finally
						{
							semaphore.Release();
						}

						if (current % persistPeriod == 0)
							await Persist();
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
					}
				}, concurrent);

				await Persist();
				
				Progress = 1;
				CurrentStatus = "Done";
			}, serviceProvider);
		}
		
		[Route("import")]
		[HttpGet]
		public IActionResult Reprocess([FromServices] IServiceProvider serviceProvider)
		{
			return StartTask(async provider =>
			{
				//var dbContext = provider.GetRequiredService<HaydenDbContext>();
				//var config = provider.GetRequiredService<IOptions<Config>>();

				//var importer = new FoolFuukaImporter();

				//await importer.Import("/mnt/nas3btank/Data/HaydenServer/foolfuuka", dbContext, config, new Progress<(float, FormattedString)>(tuple =>
				//{
				//	var (progress, status) = tuple;

				//	Progress = progress;
				//	CurrentStatus = status;
				//}));

				//Progress = 0;
				//CurrentStatus = "Initializing";

				////const string searchDir = "G:\\utg-archive\\anoncafe";
				//const string searchDir = "G:\\utg-archive\\tvch";

				//foreach (var subfolder in Directory.EnumerateDirectories(searchDir, "*", SearchOption.TopDirectoryOnly))
				//{
				//	string board = Path.GetFileName(subfolder);

				//	if (board == "hayden" || board == "server" || board == "temp")
				//		continue;

				//	static T ReadJson<T>(string filename)
				//	{
				//		using StreamReader streamReader = new StreamReader(System.IO.File.OpenRead(filename));
				//		using JsonReader reader = new JsonTextReader(streamReader);

				//		return JToken.Load(reader).ToObject<T>();
				//	}

				//	var boardObject = await dbContext.Boards.FirstOrDefaultAsync(x => x.ShortName == board);

				//	if (boardObject == null)
				//	{
				//		boardObject = new DBBoard
				//		{
				//			ShortName = board,
				//			LongName = board,
				//			Category = "tob",
				//			IsNSFW = true
				//		};

				//		dbContext.Add(boardObject);
				//		await dbContext.SaveChangesAsync();
				//	}

				//	var baseDirectory = Path.Combine(config.Value.FileLocation, board);
				//	Directory.CreateDirectory(Path.Combine(baseDirectory, "image"));
				//	Directory.CreateDirectory(Path.Combine(baseDirectory, "thumb"));
				//	Directory.CreateDirectory(Path.Combine(baseDirectory, "thread"));

				//	int totalCount = Directory.EnumerateFiles(subfolder, "thread.json", SearchOption.AllDirectories).Count();
				//	int currentCount = 0;

				//	int currentPosts;

				//	CurrentStatus = (FormattableString)$"Processing threads ({new Box<int>(currentCount)} / {totalCount}) (post {new Box<int>(0)} / {new Box<int>(0)})";

				//	foreach (var jsonFile in Directory.EnumerateFiles(subfolder, "thread.json", SearchOption.AllDirectories))
				//	{
				//		CurrentStatus.SetBox(0, ++currentCount);
				//		Progress = currentCount / (float)totalCount;

				//		currentPosts = 0;
				//		CurrentStatus.SetBox(2, currentPosts);

				//		//if (jsonFile.StartsWith(config.Value.FileLocation))
				//		//	continue;

				//		var thread = ReadJson<LynxChanThread>(jsonFile);

				//		CurrentStatus.SetBox(3, thread.Posts.Count);

				//		var existingThread = await dbContext.Threads.FirstOrDefaultAsync(x => x.BoardId == boardObject.Id && x.ThreadId == thread.OriginalPost.PostNumber);

				//		var lastModifiedTime = thread.Posts.Max(x => x.CreationDateTime).UtcDateTime;

				//		if (existingThread != null)
				//		{
				//			existingThread.IsDeleted = thread.IsDeleted == true;
				//			existingThread.IsArchived = thread.Archived;
				//			existingThread.LastModified = lastModifiedTime;
				//			existingThread.Title = thread.Subject;
				//		}
				//		else
				//		{
				//			existingThread = new DBThread()
				//			{
				//				BoardId = boardObject.Id,
				//				ThreadId = thread.OriginalPost.PostNumber,
				//				Title = thread.OriginalPost.Subject,
				//				IsArchived = thread.Archived == true,
				//				IsDeleted = thread.IsDeleted == true,
				//				LastModified = lastModifiedTime
				//			};

				//			dbContext.Add(existingThread);
				//		}

				//		var existingPosts = await dbContext.Posts.Where(x => x.BoardId == boardObject.Id && x.ThreadId == thread.OriginalPost.PostNumber).ToArrayAsync();


				//		foreach (var post in thread.Posts)
				//		{
				//			CurrentStatus.SetBox(2, ++currentPosts);

				//			if (post == null)
				//				System.Diagnostics.Debugger.Break();

				//			var existingPost = existingPosts.FirstOrDefault(x => x.BoardId == boardObject.Id && x.PostId == post.PostNumber);

				//			if (existingPost != null)
				//			{
				//				existingPost.Author = post.Name;
				//				existingPost.DateTime = post.CreationDateTime.UtcDateTime;
				//				existingPost.IsDeleted = post.ExtensionIsDeleted == true;
				//				existingPost.ContentHtml = post.Markdown;
				//				existingPost.ContentRaw = post.Message;
				//			}
				//			else
				//			{
				//				existingPost = new DBPost()
				//				{
				//					BoardId = boardObject.Id,
				//					PostId = post.PostNumber,
				//					ThreadId = thread.OriginalPost.PostNumber,
				//					ContentHtml = post.Markdown,
				//					ContentRaw = post.Message,
				//					Author = post.Name == "Anonymous" ? null : post.Name,
				//					DateTime = post.CreationDateTime.UtcDateTime,
				//					IsDeleted = post.ExtensionIsDeleted == true
				//				};

				//				// This block of logic is to fix a bug with JSON files specifying the same posts multiple times
				//				var trackedPost = dbContext.Posts.Local.FirstOrDefault(x => x.BoardId == boardObject.Id && x.PostId == post.PostNumber);

				//				if (trackedPost != null)
				//				{
				//					dbContext.Entry(trackedPost).State = EntityState.Detached;
				//				}

				//				dbContext.Add(existingPost);
				//				await dbContext.SaveChangesAsync();
				//			}

				//			int index = -1;

				//			foreach (var file in post.Files)
				//			{
				//				index++;

				//				string sourceFilename = Path.Combine(subfolder, existingThread.ThreadId.ToString(), file.DirectPath);

				//				var (newDbFile, _) = await FileImporterTools.UpdateDbFile(sourceFilename);

				//				var existingDbFile = await dbContext.Files.FirstOrDefaultAsync(x => x.BoardId == boardObject.Id && x.Sha256Hash == newDbFile.Sha256Hash);

				//				if (existingDbFile == null)
				//				{
				//					newDbFile.BoardId = boardObject.Id;
				//					newDbFile.Extension = sourceFilename.Substring(sourceFilename.LastIndexOf('.') + 1);

				//					dbContext.Add(newDbFile);
				//					await dbContext.SaveChangesAsync();

				//					existingDbFile = newDbFile;

				//					var base64Name = Utility.ConvertToBase(existingDbFile.Md5Hash);

				//					if (string.IsNullOrWhiteSpace(base64Name))
				//						System.Diagnostics.Debugger.Break();

				//					var destinationFilename = Path.Combine(config.Value.FileLocation, board, "image",
				//						$"{base64Name}.{existingDbFile.Extension}");

				//					if (!System.IO.File.Exists(destinationFilename))
				//					{
				//						System.IO.File.Copy(sourceFilename, destinationFilename);

				//						sourceFilename = Path.Combine(subfolder, existingPost.ThreadId.ToString(), "thumbs",
				//							file.DirectThumbPath);

				//						// this may not always result in .jpg....
				//						destinationFilename = Path.Combine(config.Value.FileLocation, board, "thumb",
				//							base64Name + ".jpg");

				//						System.IO.File.Copy(sourceFilename, destinationFilename);
				//					}
				//				}

				//				var existingFileMapping = await dbContext.FileMappings.FirstOrDefaultAsync(x =>
				//					x.BoardId == boardObject.Id && x.PostId == post.PostNumber && x.FileId == existingDbFile.Id);

				//				if (existingFileMapping == null)
				//				{
				//					existingFileMapping = new DBFileMapping
				//					{
				//						BoardId = boardObject.Id,
				//						FileId = existingDbFile.Id,
				//						PostId = post.PostNumber,
				//						Index = (byte)index,
				//						Filename = !file.OriginalName.Contains('.') ? file.OriginalName : file.OriginalName.Remove(file.OriginalName.LastIndexOf('.')),
				//						IsDeleted = file.IsDeleted == true,
				//						IsSpoiler = file.ThumbnailUrl.Contains("spoiler")
				//					};

				//					dbContext.FileMappings.Add(existingFileMapping);
				//				}
				//				else
				//				{
				//					existingFileMapping.IsSpoiler = file.ThumbnailUrl.Contains("spoiler");
				//				}
				//			}
				//		}

				//		await dbContext.SaveChangesAsync();
				//		dbContext.DetachAllEntities();

				//		System.IO.File.Copy(jsonFile, Path.Combine(config.Value.FileLocation, board, "thread", thread.OriginalPost.PostNumber + ".json"), true);
				//	}
				//}

				//CurrentStatus = "Done";
			}, serviceProvider);
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
								PostHtmlText = x.ContentHtml,
								PostRawText = x.ContentRaw,
								PostDateUtc = x.DateTime,
								Subject = threadSubjects.TryGetValue((x.BoardId, x.ThreadId), out var subject) ? subject : null,
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
