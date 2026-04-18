using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hayden.Consumers.HaydenMysql.DB.Migrations
{
    /// <inheritdoc />
    public partial class Version3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
		{
			bool isSqlite = ActiveProvider == MigrationExtensions.SqliteProvider;

			migrationBuilder.DropTable(
				name: "web.bans_user");

			migrationBuilder.DropTable(
				name: "web.moderators");

			migrationBuilder.DropTable(
				name: "web.reports");

			migrationBuilder.AddColumn<uint>(
				name: "ImageCount",
				table: "threads",
				type: isSqlite ? "INTEGER" : "int unsigned",
				nullable: false,
				defaultValue: 0u);

			migrationBuilder.AddColumn<uint>(
				name: "PostCount",
				table: "threads",
				type: isSqlite ? "INTEGER" : "int unsigned",
				nullable: false,
				defaultValue: 0u);

			migrationBuilder.AddColumn<string>(
				name: "TimestampedFilename",
				table: "file_mappings",
				type: isSqlite ? "TEXT" : "varchar(255)",
				maxLength: 255,
				nullable: true)
				.MarkUtf8(ActiveProvider);

			migrationBuilder.AddColumn<DateTime>(
				name: "TimeArchived",
				table: "threads",
				type: isSqlite ? "TEXT" : "datetime(6)",
				nullable: true);

			migrationBuilder.Sql("UPDATE threads SET TimeArchived = '0001-01-01 00:00:00' WHERE IsArchived = 1;");

			migrationBuilder.AddColumn<DateTime>(
				name: "TimeDeleted",
				table: "threads",
				type: isSqlite ? "TEXT" : "datetime(6)",
				nullable: true);

			migrationBuilder.Sql("UPDATE threads SET TimeDeleted = '0001-01-01 00:00:00' WHERE IsDeleted = 1;");

			migrationBuilder.AddColumn<DateTime>(
				name: "TimeDeleted",
				table: "posts",
				type: isSqlite ? "TEXT" : "datetime(6)",
				nullable: true);

			migrationBuilder.Sql("UPDATE posts SET TimeDeleted = '0001-01-01 00:00:00' WHERE IsDeleted = 1;");

			migrationBuilder.Sql("ALTER TABLE threads DROP COLUMN IsArchived;");
			migrationBuilder.Sql("ALTER TABLE threads DROP COLUMN IsDeleted;");
			migrationBuilder.Sql("ALTER TABLE posts DROP COLUMN IsDeleted;");

			migrationBuilder.AddColumn<bool>(
				name: "IsBanned",
				table: "posts",
				type: isSqlite ? "INTEGER" : "tinyint(1)",
				nullable: false,
				defaultValue: false);

			migrationBuilder.AddColumn<byte>(
				name: "Source",
				table: "posts",
				type: isSqlite ? "INTEGER" : "tinyint unsigned",
				nullable: true,
				defaultValue: null);

			migrationBuilder.AddColumn<ushort>(
				name: "Ordering",
				table: "boards",
				type: isSqlite ? "INTEGER" : "smallint unsigned",
				nullable: false,
				defaultValue: (ushort)0);

			migrationBuilder.CreateTable(
				name: "sources",
				columns: table => new
				{
					Id = table.Column<byte>(type: "tinyint unsigned", nullable: false)
						.Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
					Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
						.Annotation("MySql:CharSet", "utf8mb4"),
					Notes = table.Column<string>(type: "longtext", nullable: true)
						.Annotation("MySql:CharSet", "utf8mb4")
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_sources", x => x.Id);
				});
		}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}