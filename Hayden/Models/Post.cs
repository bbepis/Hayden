using System;
using Hayden.Consumers.HaydenMysql.DB;
using Newtonsoft.Json.Linq;

namespace Hayden.Models;

public class Post
{
	public string Author { get; set; }
	public string Tripcode { get; set; }
	public string Email { get; set; }

	public DateTimeOffset TimePosted { get; set; }

	public ulong PostNumber { get; set; }

	public string ContentRendered { get; set; }
	public string ContentRaw { get; set; }
	public ContentType ContentType { get; set; }

	public bool? IsDeleted { get; set; }

	public Media[] Media { get; set; }

	public object OriginalObject { get; set; }
	public JObject AdditionalMetadata { get; set; }
}