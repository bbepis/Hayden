using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace Hayden.Consumers.HaydenMysql.DB.Migrations
{
    internal static class MigrationExtensions
	{
        public const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

		public static OperationBuilder<AddColumnOperation> MarkAutoincrement(this OperationBuilder<AddColumnOperation> operation, string activeProvider)
		{
			if (activeProvider == SqliteProvider)
			{
				operation.Annotation("Sqlite:Autoincrement", true);
			}
			else
			{
				operation.Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
			}

			return operation;
		}

		public static OperationBuilder<AddColumnOperation> MarkUtf8(this OperationBuilder<AddColumnOperation> operation, string activeProvider)
		{
			if (activeProvider == SqliteProvider)
			{
				
			}
			else
			{
				operation.Annotation("MySql:CharSet", "utf8mb4");
			}

			return operation;
		}
	}
}
