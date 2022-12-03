using System;
using System.ComponentModel.DataAnnotations;
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

		[Required]
		public ContentType ContentType { get; set; }

		[MaxLength(255)]
		public string Author { get; set; }

		[MaxLength(255)]
		public string Tripcode { get; set; }

		[MaxLength(255)]
		public string Email { get; set; }

		public DateTime DateTime { get; set; }

		public bool IsDeleted { get; set; }

		[MaxLength(16)]
		public byte[] PosterIP { get; set; }

		[Column(TypeName = "json")]
		public string AdditionalMetadata { get; set; }
	}

	public enum ContentType
	{
		Hayden,
		Yotsuba,
		Vichan,
		Meguca,
		InfinityNext,
		LynxChan,
		Ponychan,
		ASPNetChan
	}
}