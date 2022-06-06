using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.WebServer.DB
{
	[Table("boards")]
	public class DBBoard
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public ushort Id { get; set; }

		[Column(TypeName = "varchar(16)")]
		public string ShortName { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string LongName { get; set; }

		[Column(TypeName = "varchar(16)")]
		public string Category { get; set; }
		
		[Column(TypeName = "tinyint")]
		public bool IsNSFW { get; set; }

		[Column(TypeName = "tinyint")]
		public bool IsReadOnly { get; set; }
	}
}