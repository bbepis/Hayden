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

		[Required, MaxLength(16)]
		public byte[] IPAddress { get; set; }

		[Required]
		public string Reason { get; set; }
		[Required]
		public string PublicReason { get; set; }

		[Required]
		public DateTime TimeBannedUTC { get; set; }
		public DateTime? TimeUnbannedUTC { get; set; }
	}
}