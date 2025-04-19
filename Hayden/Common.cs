using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden
{
    public static class Common
    {
	    public static readonly JsonSerializer JsonSerializer = JsonSerializer.Create(new JsonSerializerSettings
	    {
		    NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None
	    });

	    public static readonly JsonSerializer LeanSerializer = JsonSerializer.Create(new JsonSerializerSettings
	    {
		    NullValueHandling = NullValueHandling.Ignore,
			DefaultValueHandling = DefaultValueHandling.Ignore,
			Formatting = Formatting.None
	    });

	    public static JObject SerializeObject(object o)
	    {
			return JObject.FromObject(o, JsonSerializer);
	    }

	    public static string SerializeAdditionalMetadata(object o)
	    {
			if (o == null)
				return null;

			var writer = new StringBuilderWriter();
			LeanSerializer.Serialize(writer, o);
			return writer.ToString();
	    }

		public enum MediaType
		{
            FullImage,
            Thumbnail
		}
		
		public static async Task<JObject> RunJsonCommandAsync(string executable, string arguments, Stream inputStream = null)
		{
			using var process = new Process
			{
				StartInfo = new ProcessStartInfo(executable, arguments)
				{
					UseShellExecute = false,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
					RedirectStandardInput = inputStream != null
				}
			};

			var tcs = new TaskCompletionSource<object>();

			process.Exited += (sender, e) => tcs.SetResult(null);

			process.EnableRaisingEvents = true;

			process.Start();

			if (inputStream != null)
			{
				var inputTask = Task.Run(async () =>
				{
					await inputStream.CopyToAsync(process.StandardInput.BaseStream);
					process.StandardInput.Close();
				});
			}

			var errorTask = process.StandardError.ReadToEndAsync();

			using var jsonReader = new JsonTextReader(process.StandardOutput);


			//_ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

			var result = await JObject.LoadAsync(jsonReader);
			var error = await errorTask;

			await tcs.Task;

			if (process.ExitCode > 0)
			{
				throw new MagickException(process.ExitCode, error);
			}

			return result;
		}

		public static async Task RunStreamCommandAsync(string executable, string arguments, Stream inputStream, Stream outputStream)
		{
			using var process = new Process
			{
				StartInfo = new ProcessStartInfo(executable, arguments)
				{
					UseShellExecute = false,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
					RedirectStandardInput = inputStream != null
				}
			};

			var tcs = new TaskCompletionSource<object>();

			process.Exited += (sender, e) => tcs.SetResult(null);

			process.EnableRaisingEvents = true;

			process.Start();

			var errorTask = process.StandardError.ReadToEndAsync();
			var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);

			if (inputStream != null)
			{
				var inputTask = Task.Run(async () =>
				{
					await inputStream.CopyToAsync(process.StandardInput.BaseStream);
					process.StandardInput.Close();
				});
			}

			await tcs.Task;

			if (process.ExitCode > 0)
			{
				var error = await errorTask;

				throw new MagickException(process.ExitCode, error);
			}
		}
	}

    public class MagickException : Exception
    {
		public int ExitCode { get; }
		public string Error { get; }

		public MagickException(int exitCode, string error)
		{
			ExitCode = exitCode;
			Error = error;
		}

		public override string Message => $"magick returned {ExitCode}: {Error}";
    }
}
