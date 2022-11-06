using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hayden.Consumers.HaydenMysql.DB
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
		public ContentType ContentType { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Author { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Tripcode { get; set; }

		[Column(TypeName = "varchar(255)")]
		public string Email { get; set; }

		public DateTime DateTime { get; set; }

		public bool IsDeleted { get; set; }

		public byte[] PosterIP { get; set; }

		[Column(TypeName = "json")]
		public string AdditionalMetadata { get; set; }
	}

	public enum ContentType
	{
		Hayden,
		Yotsuba
	}
}