using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitMoneyTg.Migrations
{
    /// <inheritdoc />
    public partial class AddStandaloneGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION "EnforceStandaloneGroupOwnerAccess"()
                RETURNS trigger AS $$
                BEGIN
                    IF NEW."IsActive" AND EXISTS (
                        SELECT 1
                        FROM "Groups" g
                        WHERE g."Id" = NEW."GroupId"
                          AND g."Type" = 1
                          AND g."OwnerId" <> NEW."UserId"
                    ) THEN
                        RAISE EXCEPTION 'Only the owner can be a member of a standalone group';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_GroupMembers_StandaloneOwnerOnly"
                BEFORE INSERT OR UPDATE ON "GroupMembers"
                FOR EACH ROW EXECUTE FUNCTION "EnforceStandaloneGroupOwnerAccess"();
                """);

            migrationBuilder.CreateTable(
                name: "GroupParticipants",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentDetails = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupParticipants", x => new { x.GroupId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_GroupParticipants_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupParticipants_Users_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "GroupParticipants"
                    ("GroupId", "ParticipantId", "TelegramUserId", "DisplayName", "PaymentDetails", "IsActive", "CreatedAt")
                SELECT
                    "GroupId", "UserId", "UserId", NULL, NULL, "IsActive", "JoinedAt"
                FROM "GroupMembers";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GroupParticipants_GroupId_TelegramUserId",
                table: "GroupParticipants",
                columns: new[] { "GroupId", "TelegramUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupParticipants_TelegramUserId",
                table: "GroupParticipants",
                column: "TelegramUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Groups" WHERE "Type" = 1) THEN
                        RAISE EXCEPTION 'Cannot roll back standalone groups because participant identities would be lost';
                    END IF;
                END $$;

                DROP TRIGGER "TR_GroupMembers_StandaloneOwnerOnly" ON "GroupMembers";
                DROP FUNCTION "EnforceStandaloneGroupOwnerAccess"();
                """);

            migrationBuilder.DropTable(
                name: "GroupParticipants");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Groups");
        }
    }
}
