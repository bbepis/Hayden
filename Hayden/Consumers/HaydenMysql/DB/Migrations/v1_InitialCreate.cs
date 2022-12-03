using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hayden.Consumers.HaydenMysql.DB.Migrations
{

    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
	        bool isSqlite = ActiveProvider == MigrationExtensions.SqliteProvider;
            
			migrationBuilder.CreateTable(
                name: "bans_user",
                columns: table => new
                {
                    ID = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false)
                        .MarkAutoincrement(ActiveProvider),
                    IPAddress = table.Column<byte[]>(type: isSqlite ? "BLOB" : "varbinary(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: isSqlite ? "TEXT" : "text", nullable: false)
                        .MarkUtf8(ActiveProvider),
                    PublicReason = table.Column<string>(type: isSqlite ? "TEXT" : "text", nullable: false)
						.MarkUtf8(ActiveProvider),
					TimeBannedUTC = table.Column<DateTime>(type: isSqlite ? "TEXT" : "datetime(6)", nullable: false),
                    TimeUnbannedUTC = table.Column<DateTime>(type: isSqlite ? "TEXT" : "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bans_user", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "boards",
                columns: table => new
                {
                    Id = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false)
						.MarkAutoincrement(ActiveProvider),
					ShortName = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(16)", maxLength: 16, nullable: false)
						.MarkUtf8(ActiveProvider),
					LongName = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: false)
						.MarkUtf8(ActiveProvider),
					Category = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: false)
						.MarkUtf8(ActiveProvider),
					IsNSFW = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    MultiImageLimit = table.Column<byte>(type: isSqlite ? "INTEGER" : "tinyint unsigned", nullable: false),
                    IsReadOnly = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    ShowsDeletedPosts = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "moderators",
                columns: table => new
                {
                    Id = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false)
						.MarkAutoincrement(ActiveProvider),
					Username = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: false)
						.MarkUtf8(ActiveProvider),
					PasswordHash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Role = table.Column<ModeratorRole>(type: isSqlite ? "INTEGER" : "enum('Janitor','Moderator','Developer','Admin')", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    Id = table.Column<uint>(type: isSqlite ? "INTEGER" : "int unsigned", nullable: false)
						.MarkAutoincrement(ActiveProvider),
					BoardId = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false),
                    Md5Hash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                    Sha1Hash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(20)", fixedLength: true, maxLength: 20, nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    PerceptualHash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(20)", nullable: true),
                    StreamHash = table.Column<byte[]>(type: isSqlite ? "BLOB" : "binary(16)", fixedLength: true, maxLength: 16, nullable: true),
                    Extension = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(4)", maxLength: 4, nullable: false)
						.MarkUtf8(ActiveProvider),
					ThumbnailExtension = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(4)", maxLength: 4, nullable: true)
						.MarkUtf8(ActiveProvider),
					FileExists = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    FileBanned = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    ImageWidth = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: true),
                    ImageHeight = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: true),
                    Size = table.Column<uint>(type: isSqlite ? "INTEGER" : "int unsigned", nullable: false),
                    AdditionalMetadata = table.Column<string>(type: isSqlite ? "TEXT" : "json", nullable: true)
						.MarkUtf8(ActiveProvider)
				},
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_files_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "posts",
                columns: table => new
                {
                    BoardId = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false),
                    PostId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ThreadId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ContentHtml = table.Column<string>(type: isSqlite ? "TEXT" : "text", nullable: true)
						.MarkUtf8(ActiveProvider),
					ContentRaw = table.Column<string>(type: isSqlite ? "TEXT" : "text", nullable: true)
						.MarkUtf8(ActiveProvider),
					ContentType = table.Column<ContentType>(type: isSqlite ? "INTEGER" : "enum('Hayden','Yotsuba','Vichan','Meguca','InfinityNext','LynxChan','PonyChan','ASPNetChan')", nullable: false)
						.MarkUtf8(ActiveProvider),
					Author = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: true)
						.MarkUtf8(ActiveProvider),
					Tripcode = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: true)
						.MarkUtf8(ActiveProvider),
					Email = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: true)
						.MarkUtf8(ActiveProvider),
					DateTime = table.Column<DateTime>(type: isSqlite ? "TEXT" : "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    PosterIP = table.Column<byte[]>(type: isSqlite ? "BLOB" : "varbinary(16)", maxLength: 16, nullable: true),
                    AdditionalMetadata = table.Column<string>(type: isSqlite ? "TEXT" : "json", nullable: true)
						.MarkUtf8(ActiveProvider)
				},
                constraints: table =>
                {
                    table.PrimaryKey("PK_posts", x => new { x.BoardId, x.PostId });
                    table.ForeignKey(
                        name: "FK_posts_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "threads",
                columns: table => new
                {
                    BoardId = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false),
                    ThreadId = table.Column<ulong>(type: isSqlite ? "INTEGER" : "bigint unsigned", nullable: false),
                    Title = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: true)
						.MarkUtf8(ActiveProvider),
					LastModified = table.Column<DateTime>(type: isSqlite ? "TEXT" : "datetime(6)", nullable: false),
                    IsArchived = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    IsDeleted = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    AdditionalMetadata = table.Column<string>(type: isSqlite ? "TEXT" : "json", nullable: true)
						.MarkUtf8(ActiveProvider),
				},
                constraints: table =>
                {
                    table.PrimaryKey("PK_threads", x => new { x.BoardId, x.ThreadId });
                    table.ForeignKey(
                        name: "FK_threads_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_mappings",
                columns: table => new
                {
                    BoardId = table.Column<ushort>(type: isSqlite ? "INTEGER" : "smallint unsigned", nullable: false),
                    PostId = table.Column<ulong>(type: isSqlite ? "INTEGER" : "bigint unsigned", nullable: false),
                    Index = table.Column<byte>(type: isSqlite ? "INTEGER" : "tinyint unsigned", nullable: false),
                    FileId = table.Column<uint>(type: isSqlite ? "INTEGER" : "int unsigned", nullable: true),
                    Filename = table.Column<string>(type: isSqlite ? "TEXT" : "varchar(255)", maxLength: 255, nullable: false)
						.MarkUtf8(ActiveProvider),
					IsSpoiler = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    IsDeleted = table.Column<bool>(type: isSqlite ? "INTEGER" : "tinyint(1)", nullable: false),
                    AdditionalMetadata = table.Column<string>(type: isSqlite ? "TEXT" : "json", nullable: true)
						.MarkUtf8(ActiveProvider),
				},
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_mappings", x => new { x.BoardId, x.PostId, x.Index });
                    table.ForeignKey(
                        name: "FK_file_mappings_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_file_mappings_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_mappings_FileId",
                table: "file_mappings",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_files_BoardId",
                table: "files",
                column: "BoardId");

            migrationBuilder.CreateIndex(
	            name: "IX_files_Md5Hash",
	            table: "files",
	            column: "Md5Hash");

            migrationBuilder.CreateIndex(
	            name: "IX_files_PerceptualHash",
	            table: "files",
	            column: "PerceptualHash");

            migrationBuilder.CreateIndex(
	            name: "IX_files_Sha1Hash",
	            table: "files",
	            column: "Sha1Hash");

            migrationBuilder.CreateIndex(
	            name: "IX_files_Sha256Hash_BoardId",
	            table: "files",
	            columns: new[] { "Sha256Hash", "BoardId" },
	            unique: true);

            migrationBuilder.CreateIndex(
	            name: "IX_files_StreamHash",
	            table: "files",
	            column: "StreamHash");

			migrationBuilder.CreateIndex(
                name: "IX_posts_BoardId_ThreadId_DateTime",
                table: "posts",
                columns: new[] { "BoardId", "ThreadId", "DateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_threads_LastModified",
                table: "threads",
                column: "LastModified");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bans_user");

            migrationBuilder.DropTable(
                name: "file_mappings");

            migrationBuilder.DropTable(
                name: "moderators");

            migrationBuilder.DropTable(
                name: "posts");

            migrationBuilder.DropTable(
                name: "threads");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "boards");
        }
    }
}
