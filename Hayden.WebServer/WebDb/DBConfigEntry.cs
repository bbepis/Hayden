using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Hayden.WebServer.WebDb;

[Table("config")]
public class DBConfigEntry
{
	public DBConfigEntry() { }

	public DBConfigEntry(string key, string value)
	{
		Key = key;
		Value = value;
	}

	[Key]
	public string Key { get; set; }

	[Required]
	public string Value { get; set; }
}