using System;
using Nest;

namespace Hayden.WebServer.DB.Elasticsearch
{
	[ElasticsearchType(RelationName = IndexName)]
	public class PostIndex
	{
		public const string IndexName = "post_index";

		[Number(NumberType.Integer, Name = "postId", Index = false, Store = true, DocValues = false)]
		public ulong PostId { get; set; }

		[Number(NumberType.Integer, Name = "threadId", Index = true, Store = true, DocValues = false)]
		public ulong ThreadId { get; set; }

		[Number(NumberType.Short, Name = "boardId", Index = true, Store = true, DocValues = false)]
		public ushort BoardId { get; set; }

		[Text(Name = "postHtmlText", Index = true, Store = false, Similarity = "boolean")]
		public string PostHtmlText { get; set; }

		[Text(Name = "postRawText", Index = true, Store = false, Similarity = "boolean")]
		public string PostRawText { get; set; }

		[Text(Name = "subject", Index = true, Store = false, Similarity = "boolean")]
		public string Subject { get; set; }

		[Date(Name = "postDateUtc", Index = true, Store = true)]
		public DateTime PostDateUtc { get; set; }

		[Boolean(Name = "isOp", Index = true, Store = true)]
		public bool IsOp { get; set; }
	}
}
