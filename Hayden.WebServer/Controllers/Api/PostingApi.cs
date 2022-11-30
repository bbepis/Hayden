using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Hayden.WebServer.Controllers.Api
{
	public partial class ApiController
	{
		private static readonly SemaphoreSlim PostSemaphore = new(1);

		public class PostForm
		{
			public string name { get; set; }
			public string text { get; set; }
			public IFormFile file { get; set; }
			public string captcha { get; set; }
			public string board { get; set; }
			public ulong threadId { get; set; }
		}

		[RequestSizeLimit((int)(4.1 * 1024 * 1024))]
		[HttpPost("makepost")]
		public async Task<IActionResult> MakePost(
			[FromServices] HaydenDbContext dbContext,
			[FromServices] IMediaInspector mediaInspector,
			[FromForm] PostForm form)
		{
			if (form == null || form.board == null || form.threadId == 0)
				return BadRequest();

			if (string.IsNullOrWhiteSpace(form.text) && form.file == null)
				return BadRequest();
			
			var banResult = await CheckBanAsync(dbContext);
			if (banResult != null)
				return banResult;

			if (!await VerifyCaptchaAsync(form.captcha))
				return BadRequest(new { message = "Invalid captcha" });

			var threadInfo = await dbContext.GetThreadInfo(form.threadId, form.board, true);

			if (threadInfo.Item2 == null)
				return UnprocessableEntity(new { message = "Thread does not exist" });

			uint? fileId = null;

			if (form.file != null)
			{
				(var result, fileId) = await ProcessUploadedFileInternal(dbContext, mediaInspector, form.file, threadInfo.Item1);

				if (result != null)
					return result;
			}

			await PostSemaphore.WaitAsync();

			try
			{
				var nextPostId =
					await dbContext.Posts.Where(x => x.BoardId == threadInfo.Item1.Id).MaxAsync(x => x.PostId) + 1;

				var newPost = new DBPost()
				{
					Author = form.name.TrimAndNullify(),
					BoardId = threadInfo.Item1.Id,
					ContentRaw = form.text.TrimAndNullify(),
					ContentHtml = null,
					ContentType = ContentType.Hayden,
					DateTime = DateTime.UtcNow,
					Email = null,
					IsDeleted = false,
					PostId = nextPostId,
					ThreadId = form.threadId,
					Tripcode = null
				};

				dbContext.Add(newPost);

				threadInfo.Item2.LastModified = newPost.DateTime;
				dbContext.Update(threadInfo.Item2);

				await dbContext.SaveChangesAsync();

				if (fileId.HasValue)
				{
					var fileMapping = new DBFileMapping
					{
						BoardId = threadInfo.Item1.Id,
						PostId = nextPostId,
						FileId = fileId.Value,
						Filename = Path.GetFileNameWithoutExtension(form.file.FileName),
						Index = 0,
						IsDeleted = false,
						IsSpoiler = true
					};

					dbContext.Add(fileMapping);

					await dbContext.SaveChangesAsync();
				}
			}
			finally
			{
				PostSemaphore.Release();
			}

			return NoContent();
		}

		public class NewThreadForm
		{
			public string name { get; set; }
			public string text { get; set; }
			public string subject { get; set; }
			public IFormFile file { get; set; }
			public string board { get; set; }
			public string captcha { get; set; }
		}

		[RequestSizeLimit((int)(4.1 * 1024 * 1024))]
		[HttpPost("makethread")]
		public async Task<IActionResult> MakeThread(
			[FromServices] HaydenDbContext dbContext,
			[FromServices] IMediaInspector mediaInspector,
			[FromForm] NewThreadForm form)
		{
			if (form == null || form.file == null)
				return BadRequest();

			var board = await dbContext.Boards.FirstOrDefaultAsync(x => x.ShortName == form.board);

			if (board == null)
				return BadRequest();

			var banResult = await CheckBanAsync(dbContext);
			if (banResult != null)
				return banResult;

			if (!await VerifyCaptchaAsync(form.captcha))
				return BadRequest(new { message = "Invalid captcha" });

			(var result, var fileId) = await ProcessUploadedFileInternal(dbContext, mediaInspector, form.file, board);

			if (result != null)
				return result;

			await PostSemaphore.WaitAsync();

			ulong nextPostId;

			try
			{
				var lastPost = await dbContext.Posts.Where(x => x.BoardId == board.Id)
					.OrderByDescending(x => x.PostId)
					.FirstOrDefaultAsync();		  

				nextPostId = (lastPost?.PostId ?? 0) + 1;

				var newThread = new DBThread
				{
					BoardId = board.Id,
					IsArchived = false,
					IsDeleted = false,
					LastModified = DateTime.UtcNow,
					ThreadId = nextPostId,
					Title = form.subject.TrimAndNullify()
				};

				dbContext.Add(newThread);

				var newPost = new DBPost()
				{
					Author = form.name.TrimAndNullify(),
					BoardId = board.Id,
					ContentRaw = form.text.TrimAndNullify(),
					ContentHtml = null,
					ContentType = ContentType.Hayden,
					DateTime = DateTime.UtcNow,
					Email = null,
					IsDeleted = false,
					PostId = nextPostId,
					ThreadId = nextPostId,
					Tripcode = null
				};

				dbContext.Add(newPost);

				await dbContext.SaveChangesAsync();

				var fileMapping = new DBFileMapping
				{
					BoardId = board.Id,
					PostId = nextPostId,
					FileId = fileId,
					Filename = Path.GetFileNameWithoutExtension(form.file.FileName),
					Index = 0,
					IsDeleted = false,
					IsSpoiler = true
				};

				dbContext.Add(fileMapping);

				await dbContext.SaveChangesAsync();
			}
			finally
			{
				PostSemaphore.Release();
			}

			return Json(new { threadId = nextPostId });
		}

		#region Helpers

		private static readonly HttpClient CaptchaHttpClient = new();

		[NonAction]
		private async Task<bool> VerifyCaptchaAsync(string captchaResponse)
		{
			var response = await CaptchaHttpClient.PostAsync("https://hcaptcha.com/siteverify", new FormUrlEncodedContent(new[]
			{
				new KeyValuePair<string, string>("response", captchaResponse),
				new KeyValuePair<string, string>("secret", "0x0000000000000000000000000000000000000000"),
				//new KeyValuePair<string, string>("sitekey", "0x0000000000000000000000000000000000000000"),
			}));

			if (!response.IsSuccessStatusCode)
				return false;

			var responseString = await response.Content.ReadAsStringAsync();
			var jObject = JObject.Parse(responseString);
			
			return jObject.Value<bool>("success");
		}

		[NonAction]
		private async Task<IActionResult> CheckBanAsync(HaydenDbContext dbContext)
		{
			var ipAddress = HttpContext.Connection.RemoteIpAddress.GetAddressBytes();

			var banObject = await dbContext.BannedPosters
				.Where(x => x.IPAddress == ipAddress && (x.TimeUnbannedUTC == null || x.TimeUnbannedUTC > DateTime.UtcNow))
				.OrderByDescending(x => x.TimeBannedUTC)
				.FirstOrDefaultAsync();

			if (banObject != null)
				return UnprocessableEntity(new { message = $"You have been banned. Reason: {banObject.PublicReason}" });

			return null;
		}

		[NonAction]
		private async Task<(IActionResult result, uint id)> ProcessUploadedFileInternal(HaydenDbContext dbContext, IMediaInspector mediaInspector,
			IFormFile file, DBBoard boardInfo)
		{
			var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLower();

			if (extension != "png" && extension != "jpg" && extension != "jpeg" && extension != "gif")
				return (UnprocessableEntity(new { message = "File type not allowed" }), 0);

			(uint fileId, var fileBanned, bool badFile) = await ProcessUploadedFileInternal(dbContext, mediaInspector, file, boardInfo, extension);

			if (fileBanned)
				return (UnprocessableEntity(new { message = "File is banned" }), 0);

			if (badFile)
				return (UnprocessableEntity(new { message = "Corrupt or invalid file" }), 0);

			return (null, fileId);
		}

		[NonAction]
		private async Task<(uint id, bool banned, bool badFile)> ProcessUploadedFileInternal(HaydenDbContext dbContext, IMediaInspector mediaInspector,
			IFormFile file, DBBoard boardInfo, string extension)
		{
			byte[] sha256Hash;
			byte[] md5Hash;
			byte[] sha1Hash;
			byte[] fileData;

			await using (var readStream = file.OpenReadStream())
			{
				fileData = new byte[file.Length];

				await readStream.ReadAsync(fileData);
			}

			using (var sha256 = SHA256.Create())
				sha256Hash = sha256.ComputeHash(fileData);

			var dbFile =
				await dbContext.Files.FirstOrDefaultAsync(x => x.BoardId == boardInfo.Id && x.Sha256Hash == sha256Hash);

			if (dbFile != null && (dbFile.FileExists || dbFile.FileBanned))
				return (dbFile.Id, dbFile.FileBanned, false);

			using (var md5 = MD5.Create())
				md5Hash = md5.ComputeHash(fileData);

			using (var sha1 = SHA1.Create())
				sha1Hash = sha1.ComputeHash(fileData);

			var destinationFilename = Common.CalculateFilename(Config.Value.FileLocation, boardInfo.ShortName,
				Common.MediaType.Image, sha256Hash, extension);

			var thumbnailFilename = Common.CalculateFilename(Config.Value.FileLocation, boardInfo.ShortName,
				Common.MediaType.Thumbnail, sha256Hash, "jpg");

			if (!System.IO.File.Exists(destinationFilename))
			{
				using var dataStream = new MemoryStream(fileData);
				using var thumbStream = new MemoryStream();

				try
				{
					var mediaType = await mediaInspector.DetermineMediaTypeAsync(dataStream);

					if (mediaType != "png" && mediaType != "gif" && mediaType != "mjpeg")
						return (0, false, true);

					dataStream.Position = 0;

					await Common.RunStreamCommandAsync("convert",
						$"- -resize 125x125 -background grey -flatten jpg:-", dataStream, thumbStream);
				}
				//catch (MagickException ex)
				catch (Exception ex)
				{
					return (0, false, true);
				}

				await System.IO.File.WriteAllBytesAsync(destinationFilename, fileData);
				await System.IO.File.WriteAllBytesAsync(thumbnailFilename, thumbStream.ToArray());
			}

			if (dbFile != null)
			{
				dbFile.FileExists = true;
				await dbContext.SaveChangesAsync();

				return (dbFile.Id, dbFile.FileBanned, false);
			}

			dbFile = new DBFile
			{
				BoardId = boardInfo.Id,
				Extension = extension,
				Md5Hash = md5Hash,
				Sha1Hash = sha1Hash,
				Sha256Hash = sha256Hash,
				Size = (uint)fileData.Length
			};

			try
			{
				var result = await Common.RunJsonCommandAsync("ffprobe",
					$"-v quiet -hide_banner -show_streams -print_format json \"{destinationFilename}\"");

				dbFile.ImageWidth = result["streams"][0].Value<ushort>("width");
				dbFile.ImageHeight = result["streams"][0].Value<ushort>("height");
			}
			catch (Exception ex) when (ex.Message.Contains("magick"))
			{
				dbFile.ImageWidth = null;
				dbFile.ImageHeight = null;
			}

			dbContext.Files.Add(dbFile);

			await dbContext.SaveChangesAsync();

			return (dbFile.Id, false, false);
		}

		#endregion
	}
}