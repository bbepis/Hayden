using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Text;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.MediaInfo;
using Hayden.WebServer.Routing;
using Hayden.WebServer.Services.Captcha;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hayden.WebServer.Controllers.Api
{
	public partial class ApiController
	{
		private static readonly ConcurrentDictionary<IPAddress, DateTimeOffset> LastPostTimes = new();

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

		public class ReportForm
		{
			public ushort boardId { get; set; }
			public ulong postId { get; set; }
			public byte categoryLevel { get; set; }
			public string additionalInfo { get; set; }
			// public string captcha { get; set; }
		}

		[ConfigRequestSizeFilter]
		[HttpPost("makepost")]
		public async Task<IActionResult> MakePost(
			[FromServices] HaydenDbContext dbContext,
			[FromServices] ICaptchaProvider captchaProvider,
			[FromServices] IMediaInspector mediaInspector,
			[FromForm] PostForm form)
		{
			if (form == null || form.board == null || form.threadId == 0)
				return BadRequest(new { message = "Request is malformed" });

			if (string.IsNullOrWhiteSpace(form.text) && form.file == null)
				return BadRequest(new { message = "You must have text or an attached file." });

			var banResult = await CheckBanAsync(dbContext);
			if (banResult != null)
				return banResult;

			if (!await captchaProvider.VerifyCaptchaAsync(form.captcha))
				return BadRequest(new { message = "Invalid captcha" });

			if (HttpContext.Connection.RemoteIpAddress != null
				&& LastPostTimes.TryGetValue(HttpContext.Connection.RemoteIpAddress, out var lastPostTime))
			{
				var lastTimePosting = DateTimeOffset.Now - lastPostTime;

				if (lastTimePosting < TimeSpan.FromSeconds(60))
				{
					var timeRemaining = TimeSpan.FromSeconds(60) - lastTimePosting;

					return UnprocessableEntity(new { message = $"Please wait {timeRemaining.TotalSeconds:N0} seconds before posting" });
				}
			}

			var threadInfo = await dbContext.GetThreadInfo(form.threadId, form.board, true);

			if (threadInfo.Item2 == null)
				return UnprocessableEntity(new { message = "Thread does not exist" });

			var moderator = await HttpContext.GetModeratorAsync();

			if (form.file != null && threadInfo.Item1.MultiImageLimit == 0 && moderator == null)
				return BadRequest(new { message = "You must not have an attached file; images have been turned off for this board." });

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
					Tripcode = null,
					PosterIP = HttpContext.Connection.RemoteIpAddress?.GetAddressBytes()
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

			LastPostTimes[HttpContext.Connection.RemoteIpAddress] = DateTimeOffset.Now;

			return NoContent();
		}
		
		[HttpPost("makereport")]
		public async Task<IActionResult> MakeReport(
			[FromServices] HaydenDbContext dbContext,
			[FromServices] ICaptchaProvider captchaProvider,
			[FromForm] ReportForm form)
		{
			if (form == null || form.boardId == 0 || form.postId == 0)
				return BadRequest(new { message = "Request is malformed" });
			
			//var banResult = await CheckBanAsync(dbContext);
			//if (banResult != null)
			//	return banResult;

			//if (!await captchaProvider.VerifyCaptchaAsync(form.captcha))
			//	return BadRequest(new { message = "Invalid captcha" });

			var reportCategory = (ReportCategory)form.categoryLevel;

			if (!Enum.IsDefined(reportCategory))
				return BadRequest(new { message = "Invalid category level" });

			if (!dbContext.Posts.Any(x => x.BoardId == form.boardId && x.PostId == form.postId))
				return BadRequest(new { message = "Could not find post" });

			dbContext.Add(new DBReport()
			{
				BoardId = form.boardId,
				PostId = form.postId,
				Category = reportCategory,
				IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
				Reason = form.additionalInfo,
				Resolved = false,
				TimeReported = DateTime.UtcNow
			});

			await dbContext.SaveChangesAsync();
			

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

		[ConfigRequestSizeFilter]
		[HttpPost("makethread")]
		public async Task<IActionResult> MakeThread(
			[FromServices] HaydenDbContext dbContext,
			[FromServices] ICaptchaProvider captchaProvider,
			[FromServices] IMediaInspector mediaInspector,
			[FromForm] NewThreadForm form)
		{
			if (form == null || form.board == null)
				return BadRequest(new { message = "Request is malformed" });

			var board = await dbContext.Boards.FirstOrDefaultAsync(x => x.ShortName == form.board);

			if (board == null)
				return BadRequest(new { message = "Board does not exist" });

			if (form.file == null && board.MultiImageLimit > 0)
				return BadRequest(new { message = "You must have an attached file." });

			var moderator = await HttpContext.GetModeratorAsync();

			if (form.file != null && board.MultiImageLimit == 0 && moderator == null)
				return BadRequest(new { message = "You must not have an attached file; images have been turned off for this board." });

			var banResult = await CheckBanAsync(dbContext);
			if (banResult != null)
				return banResult;

			if (!await captchaProvider.VerifyCaptchaAsync(form.captcha))
				return BadRequest(new { message = "Invalid captcha" });

			if (HttpContext.Connection.RemoteIpAddress != null
				&& LastPostTimes.TryGetValue(HttpContext.Connection.RemoteIpAddress, out var lastPostTime))
			{
				var lastTimePosting = DateTimeOffset.Now - lastPostTime;

				if (lastTimePosting < TimeSpan.FromSeconds(60))
				{
					return UnprocessableEntity(new { message = $"Please wait {lastTimePosting.TotalSeconds:N0} seconds before posting" });
				}
			}

			uint? fileId = null;

			if (form.file != null)
			{
				(var result, fileId) = await ProcessUploadedFileInternal(dbContext, mediaInspector, form.file, board);

				if (result != null)
					return result;
			}


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
					Tripcode = null,
					PosterIP = HttpContext.Connection.RemoteIpAddress?.GetAddressBytes()
				};

				dbContext.Add(newPost);

				await dbContext.SaveChangesAsync();

				if (fileId.HasValue)
				{
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
			}
			finally
			{
				PostSemaphore.Release();
			}

			LastPostTimes[HttpContext.Connection.RemoteIpAddress] = DateTimeOffset.Now;


			return Json(new { threadId = nextPostId });
		}

		#region Helpers

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

			if (extension != "png" && extension != "jpg" && extension != "jpeg" && extension != "gif" && extension != "webm")
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

			var destinationFilename = Common.CalculateFilename(Config.Value.Data.FileLocation, boardInfo.ShortName,
				Common.MediaType.Image, sha256Hash, extension);

			var thumbnailFilename = Common.CalculateFilename(Config.Value.Data.FileLocation, boardInfo.ShortName,
				Common.MediaType.Thumbnail, sha256Hash, "jpg");

			if (!System.IO.File.Exists(destinationFilename))
			{
				using var dataStream = new MemoryStream(fileData);
				using var thumbStream = new MemoryStream();
                string tempWrittenFile = null;

                try
                {
                    var mediaStreams = await mediaInspector.DetermineMediaTypeAsync(dataStream, extension);

                    if (!ValidateFile(extension, mediaStreams))
                        return (0, false, true);

                    dataStream.Position = 0;

                    if (extension == "webm") // needs to be updated to check all video codecs
                    {
                        int width, height;

                        // this really needs to be refactored so it doesn't require this call.
                        // this entire thing here is disgusting
                        tempWrittenFile = Path.GetTempFileName();
                        await System.IO.File.WriteAllBytesAsync(tempWrittenFile, fileData);


                        var tempFile = await mediaInspector.DetermineMediaInfoAsync(tempWrittenFile, dbFile);

                        if (tempFile.ImageWidth > tempFile.ImageHeight)
                        {
                            width = 125;
                            height = (int)(((double)tempFile.ImageHeight / tempFile.ImageWidth) * 125);
                        }
                        else
                        {
                            height = 125;
                            width = (int)(((double)tempFile.ImageWidth / tempFile.ImageHeight) * 125);
                        }

                        await Common.RunStreamCommandAsync("ffmpeg",
                            $"-i - -frames:v 1 -vf scale={width}x{height} -c:v mjpeg -f image2pipe -", dataStream,
                            thumbStream);
                    }
                    else if (OperatingSystem.IsWindows())
                        await Common.RunStreamCommandAsync("magick",
                            $"convert - -resize 125x125> -background grey -flatten jpg:-", dataStream, thumbStream);
                    else
                        await Common.RunStreamCommandAsync("convert",
                            $"- -resize 125x125> -background grey -flatten jpg:-", dataStream, thumbStream);
                }
                //catch (MagickException ex)
                catch (Exception ex)
                {
                    return (0, false, true);
                }
                finally
                {
					if (tempWrittenFile != null)
						System.IO.File.Delete(tempWrittenFile);
                }

				Directory.CreateDirectory(Path.GetDirectoryName(destinationFilename));
				Directory.CreateDirectory(Path.GetDirectoryName(thumbnailFilename));

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

			await mediaInspector.DetermineMediaInfoAsync(destinationFilename, dbFile);

			dbContext.Files.Add(dbFile);

			await dbContext.SaveChangesAsync();

			return (dbFile.Id, false, false);
		}

		public static bool ValidateFile(string extension, MediaStream[] mediaStreams)
		{
			if (!AllowedCodecs.TryGetValue(extension, out var streamInfo))
				return false;

			if (streamInfo.maxVideoStreams.HasValue && mediaStreams.Count(x => x.CodecType == CodecType.Video) > streamInfo.maxVideoStreams.Value)
				return false;

            if (streamInfo.maxAudioStreams.HasValue && mediaStreams.Count(x => x.CodecType == CodecType.Audio) > streamInfo.maxAudioStreams.Value)
                return false;

			if (mediaStreams.Where(x => x.CodecType == CodecType.Video).Any(x => !streamInfo.allowedVideoCodecs.Contains(x.CodecName)))
				return false;

			if (mediaStreams.Where(x => x.CodecType == CodecType.Audio).Any(x => !streamInfo.allowedAudioCodecs.Contains(x.CodecName)))
				return false;

			return true;
        }

		public static readonly Dictionary<string, (int? maxVideoStreams, int? maxAudioStreams, string[] allowedVideoCodecs, string[] allowedAudioCodecs)> AllowedCodecs = new()
		{
			["png"] = (1, 0, new[] { "png" }, new string[0]),
            ["jpeg"] = (1, 0, new[] { "mjpeg" }, new string[0]),
            ["jpg"] = (1, 0, new[] { "mjpeg" }, new string[0]),
            ["gif"] = (1, 0, new[] { "gif" }, new string[0]),
            ["webp"] = (1, 0, new[] { "webp" }, new string[0]),
            ["webm"] = (1, 1, new[] { "vp8", "vp9" }, new[] { "vorbis", "opus" }),
            //["mp4"] = (1, 1, new[] { "libx264" }, new[] { "vorbis", "opus" }),
        };

		#endregion
	}
}