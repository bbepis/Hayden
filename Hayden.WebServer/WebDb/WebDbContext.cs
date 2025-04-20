using Hayden.Consumers.HaydenMysql.DB;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Threading.Tasks;
using System;

namespace Hayden.WebServer.WebDb;

public class WebDbContext : DbContext
{
	public virtual DbSet<DBBannedPoster> BannedPosters { get; set; }
	public virtual DbSet<DBModerator> Moderators { get; set; }
	public virtual DbSet<DBReport> Reports { get; set; }

	public virtual DbSet<DBConfigEntry> ConfigEntries { get; set; }
	public virtual DbSet<DBIndexPosition> IndexPositions { get; set; }

	protected WebDbContext() { }

	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VersionedMigrationIdGenerator))]
	public WebDbContext(DbContextOptions options) : base(options) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<DBModerator>(x =>
		{
			if (Database.IsMySql())
			{
				x.Property(post => post.Role)
					.HasConversion<EnumToStringConverter<ModeratorRole>>()
					.HasColumnType(
						"enum('Janitor','Moderator','Developer','Admin')");
			}
		});

		modelBuilder.Entity<DBReport>(x =>
		{
			x.Property(x => x.Category).HasConversion<byte>();
		});
	}
	public async Task<DBModerator> GetModerator(ushort userId) => await Moderators.FirstOrDefaultAsync(x => x.Id == userId);

	public async Task<DBModerator> GetModerator(string username) => await Moderators.FirstOrDefaultAsync(x => x.Username == username);

	public async Task<bool> RegisterModerator(DBModerator moderator)
	{
		if (await Moderators.AnyAsync(x => x.Username == moderator.Username))
			return false;

		Add(moderator);
		await SaveChangesAsync();
		return true;
	}
}