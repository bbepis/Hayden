using Hayden.Consumers.HaydenMysql.DB;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shipwreck.Phash;
using Shipwreck.Phash.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Linq;

namespace Hayden.MediaInfo;

public class FfprobeMediaInspector : IMediaInspector
{
    public async Task<DBFile> DetermineMediaInfoAsync(string filename, DBFile file = null)
    {
        file ??= new DBFile();

        try
        {
            var result = await Common.RunJsonCommandAsync("ffprobe", $"-v quiet -hide_banner -show_streams -print_format json \"{filename}\"");

            var streamsArray = result["streams"] as JArray;
            if (streamsArray == null || streamsArray.Count == 0)
            {
                file.ImageWidth = null;
                file.ImageHeight = null;
                file.PerceptualHash = null;
                file.StreamHash = null;
                return file;
            }

            var videoStream = streamsArray.FirstOrDefault(x => x.Value<string>("codec_type") == "video");

            file.ImageWidth = videoStream?.Value<ushort>("width");
            file.ImageHeight = videoStream?.Value<ushort>("height");

            if (videoStream != null &&
                (filename.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || filename.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)))
            {
	            file.PerceptualHash = GetPHash(filename);
            }

            file.StreamHash = await GetStreamHash(filename);
        }
        catch (MagickException)
        {
            file.ImageWidth = null;
            file.ImageHeight = null;
            file.PerceptualHash = null;
            file.StreamHash = null;
        }
        
        return file;
    }

    private byte[] GetPHash(string filename)
    {
		using var luminanceImage = new ImageSharpLuminanceByteImage(filename);
	    return ImagePhash.ComputeDigest(luminanceImage).Coefficients;
    }

    private class ImageSharpLuminanceByteImage : IByteImage, IDisposable
    {
	    private Image<L8> Image { get; }
	    public int Width => Image.Width;
	    public int Height => Image.Height;

		public byte this[int x, int y] => Image[x, y].PackedValue;

		public ImageSharpLuminanceByteImage(string filename)
		{
			Image = SixLabors.ImageSharp.Image.Load<L8>(filename);
		}

		public void Dispose() => Image.Dispose();
    }

	private static readonly Regex StreamHashRegex = new(@"^(\d+),(\w+),\w+=([0-9a-fA-F]+)$", RegexOptions.Compiled);

    private async Task<byte[]> GetStreamHash(string filename)
    {
	    try
	    {
		    using var memoryStream = new MemoryStream();

		    await Common.RunStreamCommandAsync("ffmpeg", $"-v quiet -i \"{filename}\" -map 0:v:0 -f streamhash -hash murmur3 -", null, memoryStream);

		    var text = Encoding.ASCII.GetString(memoryStream.ToArray());
		    var match = StreamHashRegex.Match(text);

		    if (!match.Success)
			    return null;

		    return Convert.FromHexString(match.Groups[3].Value);
	    }
	    catch (MagickException ex)
	    {
		    //Console.WriteLine(ex.ToString());
		    return null;
	    }
	}

    public async Task<MediaStream[]> DetermineMediaTypeAsync(Stream inputStream, string extension)
    {
        try
        {
	        bool forceImageCheck = extension == "gif";

            var result = await Common.RunJsonCommandAsync("ffprobe",
	            $"{(forceImageCheck ? "-f image2pipe" : "")} -v quiet -hide_banner -show_streams -print_format json -", inputStream);

            //Console.WriteLine(result?.ToString() ?? "<null>");

            var streamsArray = result["streams"] as JArray;

            if (streamsArray == null || streamsArray.Count == 0)
                return null;

            return streamsArray.Select(x => new MediaStream
            {
                CodecName = x.Value<string>("codec_name"),
                CodecType = Enum.TryParse<CodecType>(x.Value<string>("codec_type"), true, out var codecType) ? codecType : CodecType.Other
            }).ToArray();
        }
        catch (MagickException ex)
        {
            //Console.WriteLine(ex.ToString());
            return null;
        }
    }
}
