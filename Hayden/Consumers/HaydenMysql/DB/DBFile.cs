using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("files")]
	public class DBFile
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public uint Id { get; set; }

		public ushort BoardId { get; set; }

		[Required, FixedLength(16)]
		public byte[] Md5Hash { get; set; }

		[Required, FixedLength(20)]
		public byte[] Sha1Hash { get; set; }

		[Required, FixedLength(32)]
		public byte[] Sha256Hash { get; set; }

		[FixedLength(40)]
		public byte[] PerceptualHash { get; set; }

		[FixedLength(16)]
		public byte[] StreamHash { get; set; }

		[Required, MaxLength(4)]
		public string Extension { get; set; }

		[MaxLength(4)]
		public string ThumbnailExtension { get; set; }
		
		public bool FileExists { get; set; }
		
		public bool FileBanned { get; set; }

		public ushort? ImageWidth { get; set; }

		public ushort? ImageHeight { get; set; }

		public uint Size { get; set; }

		[Column(TypeName = "json")]
		public JObject AdditionalMetadata { get; set; }
	}

	public class Md5Conflict
	{
		public byte[] OldHash { get; set; }
		public byte[] NewHash { get; set; }

		public Md5Conflict() { }

		public Md5Conflict(byte[] oldHash, byte[] newHash)
		{
			OldHash = oldHash;
			NewHash = newHash;
		}
	}
}