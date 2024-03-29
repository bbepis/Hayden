using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("file_mappings")]
	public class DBFileMapping
	{
		public ushort BoardId { get; set; }
		public ulong PostId { get; set; }
		public byte Index { get; set; }

		public uint? FileId { get; set; }
		
		[Required, MaxLength(255)]
		public string Filename { get; set; }

		public bool IsSpoiler { get; set; }
		public bool IsDeleted { get; set; }

		[Column(TypeName = "json")]
		public string AdditionalMetadata { get; set; }
	}
}