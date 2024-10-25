using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SQLitePCL;


#nullable disable

namespace Hayden.Consumers.HaydenMysql.DB.Migrations
{
	/// <inheritdoc />
	public partial class Version2 : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			bool isSqlite = ActiveProvider == MigrationExtensions.SqliteProvider;

			migrationBuilder.DropForeignKey(
				name: "FK_files_boards_BoardId",
				table: "files");

			migrationBuilder.DropIndex(
				name: "IX_files_BoardId",
				table: "files");

			migrationBuilder.DropIndex(
				name: "IX_files_Sha256Hash_BoardId",
				table: "files");

			migrationBuilder.DropColumn(
				name: "BoardId",
				table: "files");

			migrationBuilder.AddColumn<bool>(
				name: "IsPinned",
				table: "threads",
				type: isSqlite ? "INTEGER" : "tinyint(1)",
				nullable: false,
				defaultValue: false);

			if (!isSqlite)
			{
				migrationBuilder.AlterColumn<string>(
					name: "ThumbnailExtension",
					table: "files",
					type: "varchar(16)",
					maxLength: 16,
					nullable: true,
					oldClrType: typeof(string),
					oldType: "varchar(4)",
					oldMaxLength: 4,
					oldNullable: true)
					.Annotation("MySql:CharSet", "utf8mb4")
					.OldAnnotation("MySql:CharSet", "utf8mb4");

				migrationBuilder.AlterColumn<byte[]>(
					name: "Sha256Hash",
					table: "files",
					type: "binary(32)",
					fixedLength: true,
					maxLength: 32,
					nullable: true,
					oldClrType: typeof(byte[]),
					oldType: "binary(32)",
					oldFixedLength: true,
					oldMaxLength: 32);

				migrationBuilder.AlterColumn<byte[]>(
					name: "Sha1Hash",
					table: "files",
					type: "binary(20)",
					fixedLength: true,
					maxLength: 20,
					nullable: true,
					oldClrType: typeof(byte[]),
					oldType: "binary(20)",
					oldFixedLength: true,
					oldMaxLength: 20);

				migrationBuilder.AlterColumn<byte[]>(
					name: "Md5Hash",
					table: "files",
					type: "binary(16)",
					fixedLength: true,
					maxLength: 16,
					nullable: true,
					oldClrType: typeof(byte[]),
					oldType: "binary(16)",
					oldFixedLength: true,
					oldMaxLength: 16);

				migrationBuilder.AlterColumn<string>(
					name: "Extension",
					table: "files",
					type: "varchar(16)",
					maxLength: 16,
					nullable: false,
					oldClrType: typeof(string),
					oldType: "varchar(4)",
					oldMaxLength: 4)
					.Annotation("MySql:CharSet", "utf8mb4")
					.OldAnnotation("MySql:CharSet", "utf8mb4");
			}

			migrationBuilder.AddColumn<bool>(
				name: "ThumbnailExists",
				table: "files",
				type: isSqlite ? "INTEGER" : "tinyint(1)",
				nullable: false,
				defaultValue: false);

			migrationBuilder.AddColumn<string>(
				name: "AdditionalMetadata",
				table: "boards",
				type: isSqlite ? "TEXT" : "json",
				nullable: true)
				.MarkUtf8(ActiveProvider);

			migrationBuilder.CreateTable(
				name: "reports",
				columns: table => new
				{
					Id = table.Column<uint>(type: isSqlite ? "INTEGER" : "int unsigned", nullable: false)
						.MarkAutoincrement(ActiveProvider),
					BoardId = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false),
					PostId = table.Column<ulong>(type: isSqlite ? "INTEGER" : "bigint unsigned", nullable: false),
					TimeReported = table.Column<DateTime>(type: isSqlite ? "TEXT" : "datetime(6)", nullable: false),
					IPAddress = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", fixedLength: false, maxLength: 255, nullable: true)
						.MarkUtf8(ActiveProvider),
					Category = table.Column<byte>(type: isSqlite ? "INTEGER" : "tinyint unsigned", nullable: false),
					Reason = table.Column<string>(type: "TEXT", nullable: true)
						.MarkUtf8(ActiveProvider),
					Resolved = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_reports", x => x.Id);
				});

			migrationBuilder.CreateIndex(
				name: "IX_files_Sha256Hash",
				table: "files",
				column: "Sha256Hash");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "reports");

			migrationBuilder.DropIndex(
				name: "IX_files_Sha256Hash",
				table: "files");

			migrationBuilder.DropColumn(
				name: "IsPinned",
				table: "threads");

			migrationBuilder.DropColumn(
				name: "ThumbnailExists",
				table: "files");

			migrationBuilder.DropColumn(
				name: "AdditionalMetadata",
				table: "boards");

			migrationBuilder.AlterColumn<byte>(
				name: "Role",
				table: "moderators",
				type: "tinyint unsigned",
				nullable: false,
				oldClrType: typeof(string),
				oldType: "enum('Janitor','Moderator','Developer','Admin')")
				.OldAnnotation("MySql:CharSet", "utf8mb4");

			migrationBuilder.AlterColumn<string>(
				name: "ThumbnailExtension",
				table: "files",
				type: "varchar(4)",
				maxLength: 4,
				nullable: true,
				oldClrType: typeof(string),
				oldType: "varchar(16)",
				oldMaxLength: 16,
				oldNullable: true)
				.Annotation("MySql:CharSet", "utf8mb4")
				.OldAnnotation("MySql:CharSet", "utf8mb4");

			migrationBuilder.AlterColumn<byte[]>(
				name: "Sha256Hash",
				table: "files",
				type: "binary(32)",
				fixedLength: true,
				maxLength: 32,
				nullable: false,
				defaultValue: new byte[0],
				oldClrType: typeof(byte[]),
				oldType: "binary(32)",
				oldFixedLength: true,
				oldMaxLength: 32,
				oldNullable: true);

			migrationBuilder.AlterColumn<byte[]>(
				name: "Sha1Hash",
				table: "files",
				type: "binary(20)",
				fixedLength: true,
				maxLength: 20,
				nullable: false,
				defaultValue: new byte[0],
				oldClrType: typeof(byte[]),
				oldType: "binary(20)",
				oldFixedLength: true,
				oldMaxLength: 20,
				oldNullable: true);

			migrationBuilder.AlterColumn<byte[]>(
				name: "Md5Hash",
				table: "files",
				type: "binary(16)",
				fixedLength: true,
				maxLength: 16,
				nullable: false,
				defaultValue: new byte[0],
				oldClrType: typeof(byte[]),
				oldType: "binary(16)",
				oldFixedLength: true,
				oldMaxLength: 16,
				oldNullable: true);

			migrationBuilder.AlterColumn<string>(
				name: "Extension",
				table: "files",
				type: "varchar(4)",
				maxLength: 4,
				nullable: false,
				oldClrType: typeof(string),
				oldType: "varchar(16)",
				oldMaxLength: 16)
				.Annotation("MySql:CharSet", "utf8mb4")
				.OldAnnotation("MySql:CharSet", "utf8mb4");

			migrationBuilder.AddColumn<ushort>(
				name: "BoardId",
				table: "files",
				type: "smallint unsigned",
				nullable: false,
				defaultValue: (ushort)0);

			migrationBuilder.CreateIndex(
				name: "IX_files_BoardId",
				table: "files",
				column: "BoardId");

			migrationBuilder.CreateIndex(
				name: "IX_files_Sha256Hash_BoardId",
				table: "files",
				columns: new[] { "Sha256Hash", "BoardId" },
				unique: true);

			migrationBuilder.AddForeignKey(
				name: "FK_files_boards_BoardId",
				table: "files",
				column: "BoardId",
				principalTable: "boards",
				principalColumn: "Id",
				onDelete: ReferentialAction.Cascade);
		}
	}
}
