using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.Contract;
using Hayden.WebServer.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.WebServer.Logic
{
	public abstract class BaseImporter<TThread, TPost, TPostFile> where TThread : IThread<TPost> where TPost : IPost
	{
		public async Task Import(string searchDir, HaydenDbContext dbContext, IOptions<Config> config, IProgress<(float, FormattedString)> progress)
		{
			float progressPercentage = 0;
			FormattedString progressString = "Initializing";

			void reportProgress()
			{
				progress.Report((progressPercentage, progressString));
			}

			reportProgress();

			//const string searchDir = "G:\\utg-archive\\anoncafe";
			//const string searchDir = "G:\\utg-archive\\tvch";

			foreach (var subfolder in Directory.EnumerateDirectories(searchDir, "*", SearchOption.TopDirectoryOnly))
			{
				string board = Path.GetFileName(subfolder);

				if (board is "hayden" or "server" or "temp")
					continue;

				static T ReadJson<T>(string filename)
				{
					using StreamReader streamReader = new StreamReader(File.OpenRead(filename));
					using JsonReader reader = new JsonTextReader(streamReader);

					return JToken.Load(reader).ToObject<T>();
				}

				var boardObject = await dbContext.Boards.FirstOrDefaultAsync(x => x.ShortName == board);

				if (boardObject == null)
				{
					boardObject = new DBBoard
					{
						ShortName = board,
						LongName = board,
						Category = "tob",
						IsNSFW = true
					};

					dbContext.Add(boardObject);
					await dbContext.SaveChangesAsync();
				}

				var baseDirectory = Path.Combine(config.Value.FileLocation, board);
				Directory.CreateDirectory(Path.Combine(baseDirectory, "image"));
				Directory.CreateDirectory(Path.Combine(baseDirectory, "thumb"));
				Directory.CreateDirectory(Path.Combine(baseDirectory, "thread"));

				int totalCount = Directory.EnumerateFiles(subfolder, "thread.json", SearchOption.AllDirectories).Count();
				int currentCount = 0;

				progressString = (FormattableString)$"Processing threads ({new Box<int>(currentCount)} / {totalCount}) (post {new Box<int>(0)} / {new Box<int>(0)})";

				foreach (var jsonFile in Directory.EnumerateFiles(subfolder, "thread.json", SearchOption.AllDirectories))
				{
					progressString.SetBox(0, ++currentCount);
					progressPercentage = currentCount / (float)totalCount;

					int currentPosts = 0;
					progressString.SetBox(2, currentPosts);

					reportProgress();

					//if (jsonFile.StartsWith(config.Value.FileLocation))
					//	continue;

					var thread = ReadJson<TThread>(jsonFile);

					progressString.SetBox(3, thread.Posts.Count);

					var existingThread = await dbContext.Threads.FirstOrDefaultAsync(x => x.BoardId == boardObject.Id && x.ThreadId == thread.OriginalPost.PostNumber);

					//var lastModifiedTime = thread.Posts.Max(x => x.CreationDateTime).UtcDateTime;

					if (existingThread != null)
					{
						//existingThread.IsDeleted = thread.IsDeleted == true;
						//existingThread.IsArchived = thread.Archived;
						//existingThread.LastModified = lastModifiedTime;
						//existingThread.Title = thread.Subject;

						SetThreadInfo(boardObject.Id, thread, ref existingThread, true);
					}
					else
					{
						SetThreadInfo(boardObject.Id, thread, ref existingThread, false);
						//existingThread = new DBThread
						//{
						//	BoardId = boardObject.Id,
						//	ThreadId = thread.OriginalPost.PostNumber,
						//	Title = thread.OriginalPost.Subject,
						//	IsArchived = thread.Archived,
						//	IsDeleted = thread.IsDeleted == true,
						//	LastModified = lastModifiedTime
						//};

						dbContext.Add(existingThread);
					}

					var existingPosts = await dbContext.Posts.Where(x => x.BoardId == boardObject.Id && x.ThreadId == thread.OriginalPost.PostNumber).ToArrayAsync();


					foreach (var post in thread.Posts)
					{
						progressString.SetBox(2, ++currentPosts);
						
						var existingPost = existingPosts.FirstOrDefault(x => x.BoardId == boardObject.Id && x.PostId == post.PostNumber);

						if (existingPost != null)
						{
							SetPostInfo(boardObject.Id, thread, post, ref existingPost, true);

							//existingPost.Author = post.Name;
							//existingPost.DateTime = post.CreationDateTime.UtcDateTime;
							//existingPost.IsDeleted = post.ExtensionIsDeleted == true;
							//existingPost.ContentHtml = post.Markdown;
							//existingPost.ContentRaw = post.Message;
						}
						else
						{
							SetPostInfo(boardObject.Id, thread, post, ref existingPost, false);

							//existingPost = new DBPost
							//{
							//	BoardId = boardObject.Id,
							//	PostId = post.PostNumber,
							//	ThreadId = thread.OriginalPost.PostNumber,
							//	ContentHtml = post.Markdown,
							//	ContentRaw = post.Message,
							//	Author = post.Name == "Anonymous" ? null : post.Name,
							//	DateTime = post.CreationDateTime.UtcDateTime,
							//	IsDeleted = post.ExtensionIsDeleted == true
							//};

							// This block of logic is to fix a bug with JSON files specifying the same posts multiple times
							var trackedPost = dbContext.Posts.Local.FirstOrDefault(x => x.BoardId == boardObject.Id && x.PostId == post.PostNumber);

							if (trackedPost != null)
							{
								dbContext.Entry(trackedPost).State = EntityState.Detached;
							}

							dbContext.Add(existingPost);
							await dbContext.SaveChangesAsync();
						}

						int index = -1;

						foreach (var file in GetPostFiles(post))
						{
							index++;

							string sourceFilename = Path.Combine(subfolder, GetFilesystemSourceFilePath(board, thread, post, file));
							//string sourceFilename = Path.Combine(subfolder, existingThread.ThreadId.ToString(), file.DirectPath);

							var (newDbFile, _) = await FileImporterTools.UpdateDbFile(sourceFilename);

							newDbFile.FileBanned = false;

							var existingDbFile = await dbContext.Files.FirstOrDefaultAsync(x => x.BoardId == boardObject.Id && x.Sha256Hash == newDbFile.Sha256Hash);

							if (existingDbFile == null)
							{
								newDbFile.BoardId = boardObject.Id;
								newDbFile.Extension = sourceFilename.Substring(sourceFilename.LastIndexOf('.') + 1);
								newDbFile.FileExists = true;

								dbContext.Add(newDbFile);
								await dbContext.SaveChangesAsync();

								existingDbFile = newDbFile;
								
								var destinationFilename = Common.CalculateFilename(config.Value.FileLocation, board,
									Common.MediaType.Image, existingDbFile.Sha256Hash, existingDbFile.Extension);

								if (!File.Exists(destinationFilename))
								{
									File.Copy(sourceFilename, destinationFilename);

									var thumbnailFragment = GetFilesystemThumbnailFilePath(board, thread, post, file);

									if (thumbnailFragment != null)
									{
										sourceFilename = Path.Combine(subfolder, thumbnailFragment);

										var thumbExtension = Path.GetExtension(sourceFilename).TrimStart('.');
										existingDbFile.ThumbnailExtension = thumbExtension;
										dbContext.Update(existingDbFile);
										
										destinationFilename = Common.CalculateFilename(config.Value.FileLocation, board,
											Common.MediaType.Thumbnail, existingDbFile.Sha256Hash, thumbExtension);

										File.Copy(sourceFilename, destinationFilename);
									}
								}
							}

							var existingFileMapping = await dbContext.FileMappings.FirstOrDefaultAsync(x =>
								x.BoardId == boardObject.Id && x.PostId == post.PostNumber && x.FileId == existingDbFile.Id);

							if (existingFileMapping == null)
							{
								SetFileInfo(boardObject.Id, thread, post, file, index, existingDbFile, ref existingFileMapping, false);
								//existingFileMapping = new DBFileMapping
								//{
								//	BoardId = boardObject.Id,
								//	FileId = existingDbFile.Id,
								//	PostId = post.PostNumber,
								//	Index = (byte)index,
								//	Filename = !file.OriginalName.Contains('.') ? file.OriginalName : file.OriginalName.Remove(file.OriginalName.LastIndexOf('.')),
								//	IsDeleted = file.IsDeleted == true,
								//	IsSpoiler = file.ThumbnailUrl.Contains("spoiler")
								//};

								dbContext.FileMappings.Add(existingFileMapping);
							}
							else
							{
								SetFileInfo(boardObject.Id, thread, post, file, index, existingDbFile, ref existingFileMapping, true);
								//existingFileMapping.IsSpoiler = file.ThumbnailUrl.Contains("spoiler");
							}
						}
					}

					await dbContext.SaveChangesAsync();
					dbContext.DetachAllEntities();

					File.Copy(jsonFile, Path.Combine(config.Value.FileLocation, board, "thread", thread.OriginalPost.PostNumber + ".json"), true);
				}
			}

			progressString = "Done";
			reportProgress();
		}

		protected abstract void SetThreadInfo(ushort boardId, TThread thread, ref DBThread dbThread, bool exists);
		protected abstract void SetPostInfo(ushort boardId, TThread thread, TPost post, ref DBPost dbPost, bool exists);
		protected abstract void SetFileInfo(ushort boardId, TThread thread, TPost post, TPostFile postFile, int index, DBFile dbFile, ref DBFileMapping dbFileMapping, bool exists);

		protected abstract IEnumerable<TPostFile> GetPostFiles(TPost post);

		protected abstract string GetFilesystemSourceFilePath(string board, TThread thread, TPost post, TPostFile postFile);
		protected abstract string GetFilesystemThumbnailFilePath(string board, TThread thread, TPost post, TPostFile postFile);
	}

	public static class FileImporterTools
	{
		internal static async Task<(DBFile file, bool md5Changed)> UpdateDbFile(string filename, DBFile file = null)
		{
			bool newFile = file == null;

			file ??= new DBFile();

			if (!filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
				await Common.DetermineMediaInfoAsync(filename, file);
			
			using var md5 = MD5.Create();
			using var sha1 = SHA1.Create();
			using var sha256 = SHA256.Create();

			await using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);

			long fileSize = fs.Length;

			await using var readBuffer = new RentedMemoryStream((int)fileSize, false);

			await fs.CopyToAsync(readBuffer);

			var oldMd5Hash = file.Md5Hash;

			readBuffer.Position = 0;
			file.Md5Hash = md5.ComputeHash(readBuffer);

			readBuffer.Position = 0;
			file.Sha1Hash = sha1.ComputeHash(readBuffer);

			readBuffer.Position = 0;
			file.Sha256Hash = sha256.ComputeHash(readBuffer);

			file.Size = (uint)fileSize;

			bool md5HashChanged = !oldMd5Hash.ByteArrayEquals(file.Md5Hash);

			if (md5HashChanged && !newFile)
			{
				var obj = file.AdditionalMetadata ?? new JObject();

				const string key = "md5ConflictHistory";

				JArray array = !obj.TryGetValue(key, out var rawArray) ? new JArray() : (JArray)rawArray;

				array.Add(JObject.FromObject(new Md5Conflict(oldMd5Hash, file.Md5Hash)));

				obj[key] = array;
				file.AdditionalMetadata = obj;
			}

			file.FileExists = true;

			return (file, md5HashChanged);
		}
	}
}
