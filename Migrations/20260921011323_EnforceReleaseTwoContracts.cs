using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitMoneyTg.Migrations
{
    /// <inheritdoc />
    public partial class EnforceReleaseTwoContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_GroupId_CreatedById",
                table: "Invitations");

            migrationBuilder.Sql(
                """
                UPDATE "ExpenseShares" s
                SET "GroupId" = e."GroupId"
                FROM "Expenses" e
                WHERE e."Id" = s."ExpenseId"
                  AND s."GroupId" IS NULL;

                WITH duplicates AS (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "GroupId", "CreatedById"
                        ORDER BY "CreatedAt" DESC, "Id" DESC) AS position
                    FROM "Invitations"
                    WHERE "IsActive"
                )
                UPDATE "Invitations" i
                SET "IsActive" = FALSE,
                    "RevokedAt" = COALESCE(i."RevokedAt", CURRENT_TIMESTAMP),
                    "Version" = i."Version" + 1
                FROM duplicates d
                WHERE i."Id" = d."Id" AND d.position > 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "ExpenseShares",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_OneActivePerCreatorGroup",
                table: "Invitations",
                columns: new[] { "GroupId", "CreatedById" },
                unique: true,
                filter: "\"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_OneActivePerCreatorGroup",
                table: "Invitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "ExpenseShares",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_GroupId_CreatedById",
                table: "Invitations",
                columns: new[] { "GroupId", "CreatedById" });
        }
    }
}
