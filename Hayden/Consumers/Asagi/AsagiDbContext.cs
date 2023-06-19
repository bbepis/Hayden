using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Hayden.Consumers.Asagi;

public class AsagiDbContext : DbContext
{
	private string ConnectionString { get; set; }

	public string[] Boards { get; private set; }
	public string[] AllTables { get; private set; }

	public AsagiDbContext(DbContextOptions<AsagiDbContext> options, AsagiDbContextOptions asagiDbContextOptions) : base(options)
	{
		ConnectionString = asagiDbContextOptions.ConnectionString;
	}

	public (DbSet<AsagiDbPost> posts, DbSet<AsagiDbImage> images, DbSet<AsagiDbThread> threads) GetSets(string board)
	{
		return (Set<AsagiDbPost>(board),
			Set<AsagiDbImage>($"{board}_images"),
			Set<AsagiDbThread>($"{board}_threads"));
	}

	public async Task<string[]> GetBoardTables()
	{
		await using var dbConnection = new MySqlConnection(ConnectionString);
		await dbConnection.OpenAsync();

		await using var dbCommand = dbConnection.CreateCommand();

		dbCommand.CommandText = "SHOW TABLES;";

		await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.Default);

		var tableNames = new List<string>();
		while (await reader.ReadAsync())
		{
			string tableName = (string)reader[0];

			if (!tableName.Contains('_'))
				tableNames.Add(tableName);
		}

		return tableNames.OrderBy(x => x).ToArray();
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		var boards = GetBoardTables().Result;

		foreach (var board in boards)
		{
			modelBuilder.SharedTypeEntity<AsagiDbPost>(board);
			modelBuilder.SharedTypeEntity<AsagiDbImage>($"{board}_images");
			modelBuilder.SharedTypeEntity<AsagiDbThread>($"{board}_threads");
		}
	}

	public class AsagiDbPost
	{
		[Key]
		public uint doc_id { get; set; }
		public uint media_id { get; set; }
		public uint num { get; set; }
		public uint subnum { get; set; }
		public uint thread_num { get; set; }
		public bool op { get; set; }
		public uint timestamp { get; set; }
		public uint timestamp_expired { get; set; }

		public string media_filename { get; set; }
		public ushort media_w { get; set; }
		public ushort media_h { get; set; }
		public uint media_size { get; set; }
		public string media_hash { get; set; }

		public bool spoiler { get; set; }
		public bool deleted { get; set; }
		public string capcode { get; set; }

		public string name { get; set; }
		public string trip { get; set; }
		public string email { get; set; }
		public string title { get; set; }
		public string comment { get; set; }

		public bool sticky { get; set; }
		public bool locked { get; set; }
		public string poster_hash { get; set; }
		public string poster_country { get; set; }

		public string exif { get; set; }
	}

	public class AsagiDbImage
	{
		[Key]
		public uint media_id { get; set; }
		public string media_hash { get; set; }
		public string media { get; set; }
		public string preview_op { get; set; }
		public string preview_reply { get; set; }

		public bool banned { get; set; }
	}

	public class AsagiDbThread
	{
		[Key]
		public uint thread_num { get; set; }
		public uint time_bump { get; set; }
	}

	public class AsagiDbContextOptions
	{
		public string ConnectionString { get; set; }
	}
}