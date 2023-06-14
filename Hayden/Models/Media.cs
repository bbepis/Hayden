using Newtonsoft.Json;
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
	public MediaAdditionalMetadata AdditionalMetadata { get; set; }

	public class MediaAdditionalMetadata
	{
		public ulong? YotsubaTimestamp { get; set; }

		public ulong? InfinityNextFileId { get; set; }
		public ulong? InfinityNextAttachmentId { get; set; }
		public string InfinityNextMetadata { get; set; }

		public string ExternalMediaUrl { get; set; }

		public byte? CustomSpoiler { get; set; }

		public string Serialize()
		{
			JObject jsonObject = new JObject();

			void addString(string key, string value)
			{
				if (!string.IsNullOrWhiteSpace(value))
					jsonObject[key] = value;
			}

			void addBool(string key, bool value)
			{
				if (value)
					jsonObject[key] = true;
			}

			if (YotsubaTimestamp.HasValue)
				jsonObject["yotsuba_tim"] = YotsubaTimestamp.Value;
			
			addString("infinitynext_metadata", InfinityNextMetadata);

			if (InfinityNextFileId.HasValue)
				jsonObject["infinitynext_fileid"] = InfinityNextFileId.Value;

			if (InfinityNextAttachmentId.HasValue)
				jsonObject["infinitynext_attachmentid"] = InfinityNextAttachmentId.Value;


			addString("externalMediaUrl", ExternalMediaUrl);

			if (CustomSpoiler.HasValue && CustomSpoiler.Value > 0)
				jsonObject["customSpoiler"] = CustomSpoiler.Value;

			return jsonObject.HasValues ? jsonObject.ToString(Formatting.None) : null;
		}
	}
}