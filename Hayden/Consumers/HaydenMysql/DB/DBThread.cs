using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
{
	[Table("threads")]
	public class DBThread
	{
		public ushort BoardId { get; set; }

		public ulong ThreadId { get; set; }

		[MaxLength(255)]
		public string Title { get; set; }
		public DateTime LastModified { get; set; }

		public DateTime? TimeArchived { get; set; }
		public DateTime? TimeDeleted { get; set; }
		public bool IsPinned { get; set; }

		public uint PostCount { get; set; }
		public uint ImageCount { get; set; }

		[Column(TypeName = "json")]
		public string AdditionalMetadata { get; set; }
	}
}