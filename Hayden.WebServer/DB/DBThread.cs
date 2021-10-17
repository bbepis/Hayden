using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.WebServer.DB
{
	[Table("threads")]
	public class DBThread
	{
		[Column(TypeName = "varchar(4)")]
		public string Board { get; set; }

		public ulong ThreadId { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Title { get; set; }
		public DateTime LastModified { get; set; }

		public bool IsArchived { get; set; }
		public bool IsDeleted { get; set; }
	}
}