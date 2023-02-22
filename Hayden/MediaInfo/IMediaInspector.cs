using Hayden.Consumers.HaydenMysql.DB;
using System.IO;
using System.Threading.Tasks;

namespace Hayden.MediaInfo;

public interface IMediaInspector
{
    Task<DBFile> DetermineMediaInfoAsync(string filename, DBFile file = null);
    Task<MediaStream[]> DetermineMediaTypeAsync(Stream inputStream, string extension);
}

public class MediaStream
{
    public CodecType CodecType { get; set; }
    public string CodecName { get; set; }

    public MediaStream() { }
    public MediaStream(string codecName, CodecType codecType)
    {
        CodecType = codecType;
        CodecName = codecName;
    }

    public override bool Equals(object obj)
    {
        var b = obj as MediaStream;
        return CodecName == b.CodecName && CodecType == b.CodecType;
    }

    public override string ToString()
    {
        return $"{CodecName} ({CodecType})";
    }
}

public enum CodecType
{
    Video,
    Audio,
    Other
}