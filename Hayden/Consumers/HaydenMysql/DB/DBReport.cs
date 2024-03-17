using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("reports")]
	public class DBReport
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public uint Id { get; set; }

		[Required]
		public ushort BoardId { get; set; }

		[Required]
		public ulong PostId { get; set; }

		[Required]
		public DateTime TimeReported { get; set; }

		[FixedLength(255)]
		public string IPAddress { get; set; }

		[Required]
		public ReportCategory Category { get; set; }

		[Column(TypeName = "TEXT")]
		public string Reason { get; set; }

		[Required]
		public bool Resolved { get; set; }
	}

	public enum ReportCategory
	{
		Immediate = 4,
		High = 3,
		Medium = 2,
		Low = 1
	}
}