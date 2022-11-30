using Hayden.Consumers.HaydenMysql.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Hayden.MediaInfo;

public class FfprobeMediaInspector : IMediaInspector
{
    public async Task<DBFile> DetermineMediaInfoAsync(string filename, DBFile file = null)
    {
        file ??= new DBFile();

        try
        {
            var result = await Common.RunJsonCommandAsync("ffprobe", $"-v quiet -hide_banner -show_streams -print_format json \"{filename}\"");

            file.ImageWidth = result["streams"][0].Value<ushort>("width");
            file.ImageHeight = result["streams"][0].Value<ushort>("height");
        }
        catch (MagickException)
        {
            file.ImageWidth = null;
            file.ImageHeight = null;
        }

        return file;
    }


    public async Task<string> DetermineMediaTypeAsync(Stream inputStream)
    {
        try
        {
            var result = await Common.RunJsonCommandAsync("ffprobe", $"-v quiet -hide_banner -show_streams -print_format json -", inputStream);

            Console.WriteLine(result?.ToString() ?? "<null>");

            var streamsArray = result["streams"] as JArray;

            if (streamsArray == null || streamsArray.Count != 1)
                return null;

            return streamsArray[0].Value<string>("codec_name");
        }
        catch (MagickException ex)
        {
            Console.WriteLine(ex.ToString());
            return null;
        }
    }
}
