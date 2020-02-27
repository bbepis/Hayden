using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Hayden
{
	/// <summary>
	/// A <see cref="JsonConverter"/> for converting 4chan API results from integers into boolean values, and vice-versa.
	/// </summary>
	public class BoolIntConverter : JsonConverter<bool>
	{
		public override void WriteJson(JsonWriter writer, bool value, JsonSerializer serializer)
		{
			writer.WriteValue(value ? 1 : 0);
		}

		public override bool ReadJson(JsonReader reader, Type objectType, bool existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			return reader.Value.ToString() == "1";
		}
	}

	/// <summary>
	/// A <see cref="JsonConverter"/> for converting 4chan API results from a string date format into an actual <see cref="DateTime"/> object, and vice-versa.
	/// </summary>
	public class YotsubaDateConverter : JsonConverter<DateTime>
	{
		public override void WriteJson(JsonWriter writer, DateTime value, JsonSerializer serializer)
		{
			writer.WriteValue(value.ToString("MM/dd/yy(ddd)HH:mm:ss"));
		}

		private static string[] DateFormats { get; } =
		{
			"MM/dd/yy(ddd)HH:mm:ss",
			"MM/dd/yy(ddd)HH:mm"
		};

		public override DateTime ReadJson(JsonReader reader, Type objectType, DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			return DateTime.ParseExact(reader.Value.ToString(), DateFormats, CultureInfo.InvariantCulture);
		}
	}
}