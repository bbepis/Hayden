using Hayden.Consumers.HaydenMysql.DB;
using System.IO;
using System.Threading.Tasks;

namespace Hayden.MediaInfo;

public interface IMediaInspector
{
    Task<DBFile> DetermineMediaInfoAsync(string filename, DBFile file = null);
    Task<string> DetermineMediaTypeAsync(Stream inputStream);
}
