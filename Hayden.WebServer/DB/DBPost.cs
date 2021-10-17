using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.WebServer.DB
{
	[Table("posts")]
	public class DBPost
	{
		[Column(TypeName = "varchar(4)")]
		public string Board { get; set; }

		public ulong PostId { get; set; }
		public ulong ThreadId { get; set; }

		public string Html { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Author { get; set; }

		[Column(TypeName = "binary(16)")]
		public byte[] MediaHash { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string MediaFilename { get; set; }

		public DateTime DateTime { get; set; }

		public bool IsSpoiler { get; set; }
		public bool IsDeleted { get; set; }
		public bool IsImageDeleted { get; set; }
	}
}