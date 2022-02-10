using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.WebServer.DB
{
	[Table("posts")]
	public class DBPost
	{
		public ushort BoardId { get; set; }

		public ulong PostId { get; set; }
		public ulong ThreadId { get; set; }

		public string ContentHtml { get; set; }
		public string ContentRaw { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Author { get; set; }

		public DateTime DateTime { get; set; }
		
		public bool IsDeleted { get; set; }
	}
}