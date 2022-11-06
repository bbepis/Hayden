using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("bans_user")]
	public class DBBannedPoster
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public ushort ID { get; set; }

		[Column(TypeName = "varbinary(16)")]
		public byte[] IPAddress { get; set; }
		
		public string Reason { get; set; }
		public string PublicReason { get; set; }
		
		public DateTime TimeBannedUTC { get; set; }
		public DateTime? TimeUnbannedUTC { get; set; }
	}
}