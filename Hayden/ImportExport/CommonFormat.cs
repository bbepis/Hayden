namespace Hayden.ImportExport
{
	public class CommonThread
	{
		public string board;
		public ulong threadId;
		
		public ulong? timestamp_expired;

		public string subject;

		public bool sticky;
		public bool locked;

		public string contentType;

		public CommonPost[] posts;
	}

	public class CommonPost
	{
		public ulong postId;

		public ulong timestamp;
		public ulong? timestamp_expired;

		public bool deleted;

		public string capcode;

		public string email;
		public string name;
		public string tripcode;

		public string contentHtml;
		public string contentRaw;

		public string posterHash;
		public string posterCountry;

		public string exif;

		public CommonMedia[] media;
	}

	public class CommonMedia
	{
		public string originalFilename;
		public bool spoiler;

		public string md5Hash;
		public uint? size;

		public bool banned;

		public int? imageWidth;
		public int? imageHeight;
	}
}
