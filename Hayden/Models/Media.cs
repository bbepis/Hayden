using Newtonsoft.Json.Linq;

namespace Hayden.Models;

public class Media
{
	public string FileUrl { get; set; }
	public string ThumbnailUrl { get; set; }

	public string Filename { get; set; }

	public bool? IsSpoiler { get; set; }
	public bool IsDeleted { get; set; }

	public string FileExtension { get; set; }
	public string ThumbnailExtension { get; set; }

	public byte Index { get; set; }
	public uint? FileSize { get; set; }

	public byte[] Md5Hash { get; set; }
	public byte[] Sha1Hash { get; set; }
	public byte[] Sha256Hash { get; set; }

	public object OriginalObject { get; set; }
	public JObject AdditionalMetadata { get; set; }
}