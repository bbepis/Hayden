using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Hayden.Consumers.HaydenMysql.DB;

namespace Hayden.WebServer.WebDb;

[Table("moderators")]
public class DBModerator
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
	public ushort Id { get; set; }

	[Required, MaxLength(255)]
	public string Username { get; set; }

	[Required, FixedLength(64)]
	public byte[] PasswordHash { get; set; }

	[Required, FixedLength(32)]
	public byte[] PasswordSalt { get; set; }

	public ModeratorRole Role { get; set; }

	[NotMapped]
	public bool CanDeletePosts => Role is ModeratorRole.Janitor or ModeratorRole.Moderator or ModeratorRole.Admin;

	[NotMapped]
	public bool CanDeleteThreads => Role is ModeratorRole.Moderator or ModeratorRole.Admin;

	[NotMapped]
	public bool CanAccessAdminPage => Role is ModeratorRole.Developer or ModeratorRole.Admin;
}

public enum ModeratorRole : byte
{
	Janitor = 1,
	Moderator = 2,
	Developer = 3,
	Admin = 4
}