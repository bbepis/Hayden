using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Hayden.Consumers.HaydenMysql.DB
{
	public class HaydenDbContext : DbContext
	{
		public virtual DbSet<DBBoard> Boards { get; set; }
		public virtual DbSet<DBThread> Threads { get; set; }
		public virtual DbSet<DBPost> Posts { get; set; }
		public virtual DbSet<DBFileMapping> FileMappings { get; set; }
		public virtual DbSet<DBFile> Files { get; set; }

		// live imageboard only
		public virtual DbSet<DBBannedPoster> BannedPosters { get; set; }
		public virtual DbSet<DBModerator> Moderators { get; set; }

		private HaydenDbContext() { }
		public HaydenDbContext(DbContextOptions options) : base(options) { }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			bool isSqlite = Database.IsSqlite();

			void CheckFixedLength(IMutableProperty property)
			{
				var memberInfo = property.PropertyInfo ?? (MemberInfo)property.FieldInfo;
				if (memberInfo == null) return;
				var fixedLengthAttribute = memberInfo.GetCustomAttribute<FixedLengthAttribute>();

				if (fixedLengthAttribute != null)
				{
					
					property.SetIsFixedLength(true);
					property.SetMaxLength(fixedLengthAttribute.Length);
				}
			}

			void MapUlongToLong(IMutableProperty property)
			{
				var fieldType = property.PropertyInfo?.PropertyType ?? property.FieldInfo?.FieldType;
				if (fieldType == null || fieldType != typeof(ulong)) return;

				property.SetValueConverter(new ValueConverter<ulong, long>(v => (long)v, v => (ulong)v));
			}

			foreach (var entityType in modelBuilder.Model.GetEntityTypes())
				foreach (var property in entityType.GetProperties())
				{
					CheckFixedLength(property);

					if (isSqlite)
					{
						MapUlongToLong(property);
					}
				}

			modelBuilder.Entity<DBBoard>(x =>
			{
				x.Property(x => x.Id)
					.ValueGeneratedOnAdd();
			});

			modelBuilder.Entity<DBThread>(x =>
			{
				x.HasKey(x => new { x.BoardId, x.ThreadId });

				x.HasOne<DBBoard>()
					.WithMany()
					.HasForeignKey(x => x.BoardId);

				x.HasIndex(x => x.LastModified);
			});
			
			modelBuilder.Entity<DBPost>(x =>
			{
				x.HasKey(x => new { x.BoardId, x.PostId });

				if (isSqlite)
					x.Property(x => x.PostId)
						.HasConversion(
							v => (long)v,
							v => (ulong)v
						);
				
				x.HasOne<DBBoard>()
					.WithMany()
					.HasForeignKey(x => x.BoardId);

				x.HasIndex(x => new { x.BoardId, x.ThreadId, x.DateTime });

				if (Database.IsMySql())
				{
					x.Property(post => post.ContentType)
						.HasConversion<EnumToStringConverter<ContentType>>()
						.HasColumnType(
							"enum('Hayden','Yotsuba','Vichan','Meguca','InfinityNext','LynxChan','PonyChan','ASPNetChan')");
				}
			});

			modelBuilder.Entity<DBFileMapping>(x =>
			{
				x.HasKey(x => new { x.BoardId, x.PostId, x.Index });

				x.HasOne<DBFile>()
					.WithMany()
					.HasForeignKey(x => x.FileId);

				x.HasOne<DBBoard>()
					.WithMany()
					.HasForeignKey(x => x.BoardId);

				//HasJsonConversion(x.Property(x => x.AdditionalMetadata));
			});

			modelBuilder.Entity<DBFile>(x =>
			{
				x.Property(x => x.Id)
					.ValueGeneratedOnAdd();

				HasJsonConversion(x.Property(x => x.AdditionalMetadata));

				x.HasIndex(x => new { x.Sha256Hash, x.BoardId }).IsUnique();
				x.HasIndex(x => new { x.Md5Hash });
				x.HasIndex(x => new { x.Sha1Hash });
				x.HasIndex(x => new { x.PerceptualHash });
				x.HasIndex(x => new { x.StreamHash });

				x.Property(x => x.Md5Hash)
					.IsFixedLength();

				x.HasOne<DBBoard>()
					.WithMany()
					.HasForeignKey(x => x.BoardId);
			});

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

			modelBuilder.HasCharSet(CharSet.Utf8Mb4.Name, DelegationModes.ApplyToColumns);
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			base.OnConfiguring(optionsBuilder);

			optionsBuilder.ReplaceService<IMigrationsIdGenerator, VersionedMigrationIdGenerator>();
		}

		public async Task UpgradeOrCreateAsync()
		{
			var pendingMigrations = (await Database.GetPendingMigrationsAsync()).ToArray();

			if (pendingMigrations.Length == 0)
			{
				Console.WriteLine("Database is up to date.");
				return;
			}

			Console.WriteLine($"{pendingMigrations.Length} database upgrades are pending.");
			var migrator = Database.GetService<IMigrator>();

			for (int i = 0; i < pendingMigrations.Length; i++)
			{
				var migrationName = pendingMigrations[i];

				Console.WriteLine($"[{i + 1}/{pendingMigrations.Length}] Applying migration \"{migrationName}\"");

				var migrationBuilder = new MigrationBuilder(Database.ProviderName);
				var migration = new InitialCreate();
				

				await migrator.MigrateAsync(migrationName);
			}

			Console.WriteLine("Database upgrade complete.");
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, DBBoard boardObj, bool skipPostInfo = false)
		{
			var thread = await Threads.AsNoTracking().FirstOrDefaultAsync(x => x.BoardId == boardObj.Id && x.ThreadId == threadId);

			if (thread == null)
				return (boardObj, null, null, null);

			if (skipPostInfo)
				return (boardObj, thread, null, null);

			var posts = await Posts.AsNoTracking()
				.Where(x => x.BoardId == boardObj.Id && x.ThreadId == threadId)
				.Where(x => boardObj.ShowsDeletedPosts || !x.IsDeleted)
				.OrderBy(x => x.DateTime)
				.ToArrayAsync();

			var fileMappings = await (from mapping in FileMappings
				join post in Posts on new { mapping.BoardId, mapping.PostId } equals new { post.BoardId, post.PostId }
				from file in Files.Where(f => f.BoardId == mapping.BoardId && f.Id == mapping.FileId).DefaultIfEmpty()
				where post.BoardId == boardObj.Id && post.ThreadId == threadId
				select new { mapping, file }).ToArrayAsync();

			return (boardObj, thread, posts, fileMappings.Select(x => (x.mapping, x.file)).ToArray());
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, string board, bool skipPostInfo = false)
		{
			var boardObj = await Boards.FirstOrDefaultAsync(x => x.ShortName == board);

			if (boardObj == null)
				return default;

			return await GetThreadInfo(threadId, boardObj, skipPostInfo);
		}

		public async Task<(DBBoard, DBThread, DBPost[], (DBFileMapping, DBFile)[])> GetThreadInfo(ulong threadId, ushort boardId, bool skipPostInfo = false)
		{
			var boardObj = await Boards.FirstOrDefaultAsync(x => x.Id == boardId);

			if (boardObj == null)
				return default;

			return await GetThreadInfo(threadId, boardObj, skipPostInfo);
		}

		public void DetachAllEntities()
		{
			var changedEntriesCopy = ChangeTracker.Entries()
				//.Where(e => e.State == EntityState.Unchanged)
				.ToList();

			foreach (var entry in changedEntriesCopy)
				entry.State = EntityState.Detached;
		}

		private static readonly ValueConverter<JObject, string> JsonValueConverter = new(
			v => v == null ? null : v.ToString(Formatting.None),
			v => v == null ? null : JObject.Parse(v)
		);

		private static readonly ValueComparer<JObject> JsonValueComparer = new(
			(l, r) => JToken.DeepEquals(l, r),
			v => v == null ? 0 : v.GetHashCode(),
			v => v == null ? null : (JObject)v.DeepClone()
		);

		protected static PropertyBuilder HasJsonConversion(PropertyBuilder<JObject> propertyBuilder)
		{
			propertyBuilder.HasConversion(JsonValueConverter);
			propertyBuilder.Metadata.SetValueConverter(JsonValueConverter);
			propertyBuilder.Metadata.SetValueComparer(JsonValueComparer);
			propertyBuilder.HasColumnType("json");

			return propertyBuilder;
		}
	}

	class HaydenDbContextFactory : IDesignTimeDbContextFactory<HaydenDbContext>
	{
		public HaydenDbContext CreateDbContext(string[] args)
		{
			var optionsBuilder = new DbContextOptionsBuilder<HaydenDbContext>();
			optionsBuilder.UseMySql("Server=.", ServerVersion.Create(8, 0, 0, ServerType.MySql));

			return new HaydenDbContext(optionsBuilder.Options);
		}
	}


	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]

	public sealed class FixedLengthAttribute : Attribute
	{
		public int Length { get; set; }

		public FixedLengthAttribute(int length)
		{
			Length = length;
		}
	}

	class VersionedMigrationIdGenerator : IMigrationsIdGenerator
	{
		private static readonly Regex versionRegex = new (@"^v(\d+)_");

		public string GenerateId(string name)
		{
			var items
				= typeof(VersionedMigrationIdGenerator).Assembly.GetTypes()
					.Where(t => t.IsSubclassOf(typeof(Migration)) &&
					            t.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(HaydenDbContext))
					.Select(t => new { t, id = t.GetCustomAttribute<MigrationAttribute>()?.Id })
					.OrderBy(t1 => t1.id)
					.Select(t1 => (t1.id, t1.t))
					.ToArray();

			var currentId = int.Parse(versionRegex.Match(items.Last().id).Groups[1].Value);

			return $"v{currentId + 1}_{name}";
		}

		public string GetName(string id)
		{
			return id;
		}

		public bool IsValidId(string value)
		{
			return true;
		}
	}
}