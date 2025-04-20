using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Hayden.WebServer.WebDb;

[Table("index_position")]
public class DBIndexPosition
{
	[Key]
	public ushort BoardId { get; set; }

	[Required]
	public ulong PostPosition { get; set; }
}