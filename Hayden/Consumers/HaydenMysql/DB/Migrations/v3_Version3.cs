using System;
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
		}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
