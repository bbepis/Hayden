using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hayden.Models;

public class Thread
{
	public ulong ThreadId { get; set; }

	public string Title { get; set; }
	public bool IsArchived { get; set; }

	public Post[] Posts { get; set; }
	
	public object OriginalObject { get; set; }
	public ThreadAdditionalMetadata AdditionalMetadata { get; set; }

	public class ThreadAdditionalMetadata
	{
		public bool Deleted { get; set; }
		public bool Sticky { get; set; }
		public bool Locked { get; set; }

		public ulong? TimeExpired { get; set; }

		public string Serialize()
		{
			JObject jsonObject = new JObject();

			void addBool(string key, bool value)
			{
				if (value)
					jsonObject[key] = true;
			}

			void addString(string key, string value)
			{
				if (!string.IsNullOrWhiteSpace(value))
					jsonObject[key] = value;
			}

			addBool("sticky", Sticky);
			addBool("locked", Locked);

			if (TimeExpired.HasValue && TimeExpired > 0)
				jsonObject["time_expired"] = TimeExpired.Value;

			return jsonObject.HasValues ? jsonObject.ToString(Formatting.None) : null;
		}
	}
}