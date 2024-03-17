using System.ComponentModel.DataAnnotations;
using Hayden.Consumers.HaydenMysql.DB;
using Microsoft.EntityFrameworkCore;

namespace Hayden.WebServer.Data;

public class AuxiliaryDbContext : DbContext
{
	public DbSet<BoardIndex> BoardIndexes { get; set; }
	public DbSet<DBModerator> Moderators { get; set; }

	public AuxiliaryDbContext(DbContextOptions<AuxiliaryDbContext> options) : base(options) { }

	public class BoardIndex
	{
		[Key]
		public ushort Id { get; set; }
		public string ShortName { get; set; }

		public ulong IndexPosition { get; set; }
	}
}