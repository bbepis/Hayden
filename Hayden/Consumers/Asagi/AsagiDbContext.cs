using System;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Hayden.Consumers.Asagi.AsagiDbContext;

namespace Hayden.Consumers.Asagi;

public class AsagiDbContext : DbContext
{
	private string ConnectionString { get; set; }

	public string[] Boards { get; private set; }
	public string[] AllTables { get; private set; }

	public AsagiDbContext(DbContextOptions<AsagiDbContext> options) : base(options)
	{
		var extension = options.FindExtension<AsagiDbExtension>();

		if (extension == null)
			throw new InvalidOperationException("AsagiDbContext requires an attached AsagiDbExtension for connection metadata");

		ConnectionString = extension.ConnectionString;
	}
	private DbSet<TEntity> TryRetrieveDbSet<TEntity>(string tableName) where TEntity : class
	{
		if (AllTables == null)
			throw new Exception("Table list has not been determined yet");

		if (AllTables.Contains(tableName))
			return Set<TEntity>(tableName);

		return null;
	}

	public (DbSet<AsagiDbPost> posts, DbSet<AsagiDbImage> images, DbSet<AsagiDbThread> threads, DbSet<AsagiDbPost> deleted) GetSets(string board)
	{
		return (TryRetrieveDbSet<AsagiDbPost>(board),
			TryRetrieveDbSet<AsagiDbImage>($"{board}_images"),
			TryRetrieveDbSet<AsagiDbThread>($"{board}_threads"),
			TryRetrieveDbSet<AsagiDbPost>($"{board}_deleted"));
	}

	public async Task<string[]> GetBoardTables()
	{
		if (Boards != null)
			return Boards;

		await RetrieveTables();

		return Boards;
	}

	private async Task RetrieveTables()
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

			tableNames.Add(tableName);
		}

		tableNames.Sort();

		AllTables = tableNames.ToArray();
		Boards = tableNames.Where(x => !x.Contains('_')).ToArray();
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		var boards = GetBoardTables().Result;

		foreach (var board in boards)
		{
			modelBuilder.SharedTypeEntity<AsagiDbPost>(board);
			modelBuilder.SharedTypeEntity<AsagiDbImage>($"{board}_images");
			modelBuilder.SharedTypeEntity<AsagiDbThread>($"{board}_threads");
			modelBuilder.SharedTypeEntity<AsagiDbPost>($"{board}_deleted");
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

		private uint backing_timestamp;
		[BackingField(nameof(backing_timestamp))]
		public uint? timestamp { get => backing_timestamp; set => backing_timestamp = value.GetValueOrDefault(); }
		private uint backing_timestamp_expired;
		[BackingField(nameof(backing_timestamp_expired))]
		public uint? timestamp_expired { get => backing_timestamp_expired; set => backing_timestamp_expired = value.GetValueOrDefault(); }

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

	public class AsagiDbExtension : IDbContextOptionsExtension
	{
		public string ConnectionString { get; set; }
		public void ApplyServices(IServiceCollection services) { }

		public void Validate(IDbContextOptions options)	{ }

		public DbContextOptionsExtensionInfo Info { get; }

		public AsagiDbExtension(string connectionString)
		{
			Info = new ExtensionInfo(this);

			ConnectionString = connectionString;
		}

		public class ExtensionInfo : DbContextOptionsExtensionInfo
		{
			public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }

			public override int GetServiceProviderHashCode() => 0;

			public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
				=> string.Equals(LogFragment, other.LogFragment, StringComparison.Ordinal);

			public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) {	}

			public override bool IsDatabaseProvider { get; } = false;
			public override string LogFragment { get; } = "AsagiDbContextExtension";
		}
	}
}

public static class AsagiDbContextExtensions
{
	public static DbContextOptionsBuilder AddAsagiConfig(this DbContextOptionsBuilder builder, string connectionString)
	{
		((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new AsagiDbExtension(connectionString));
		return builder;
	}
}