using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.WebServer.DB
{
	[Table("files")]
	public class DBFile
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public uint Id { get; set; }

		public ushort BoardId { get; set; }

		[Column(TypeName = "binary(16)")]
		public byte[] Md5Hash { get; set; }

		[Column(TypeName = "binary(20)")]
		public byte[] Sha1Hash { get; set; }

		[Column(TypeName = "binary(32)")]
		public byte[] Sha256Hash { get; set; }

		[Column(TypeName = "binary(16)")]
		public byte[] OriginalMd5Hash { get; set; }

		[Column(TypeName = "varchar(4)")]
		public string Extension { get; set; }
		
		public ushort? ImageWidth { get; set; }
		
		public ushort? ImageHeight { get; set; }
		
		public uint Size { get; set; }

		[Column(TypeName = "json")]
		public string Md5ConflictHistory { get; set; }
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