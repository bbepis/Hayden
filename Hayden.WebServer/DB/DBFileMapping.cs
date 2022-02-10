using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.WebServer.DB
{
	[Table("file_mappings")]
	public class DBFileMapping
	{
		public ushort BoardId { get; set; }
		public ulong PostId { get; set; }
		public uint FileId { get; set; }
		
		public byte Index { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Filename { get; set; }
		
		public bool IsSpoiler { get; set; }
		public bool IsDeleted { get; set; }
	}
}