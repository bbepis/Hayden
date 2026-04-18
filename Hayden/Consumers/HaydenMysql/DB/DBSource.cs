using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("sources")]
	public class DBSource
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public byte Id { get; set; }

		[Required, MaxLength(255)]
		public string Name { get; set; }

		public string Notes { get; set; }
	}
}