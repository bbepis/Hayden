using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("boards")]
	public class DBBoard
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public ushort Id { get; set; }

		[Required, MaxLength(16)]
		public string ShortName { get; set; }

		[Required, MaxLength(255)]
		public string LongName { get; set; }

		[Required, MaxLength(255)]
		public string Category { get; set; }
		
		public bool IsNSFW { get; set; }
		
		public byte MultiImageLimit { get; set; }

		public bool IsReadOnly { get; set; }
		
		public bool ShowsDeletedPosts { get; set; }
	}
}