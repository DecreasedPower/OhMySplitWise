using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SplitMoneyTg.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseOneDatabaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseShares_Expenses_ExpenseId",
                table: "ExpenseShares");

            migrationBuilder.AlterColumn<long>(
                name: "TelegramId",
                table: "Users",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Transfers",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "Sessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "UpdateId",
                table: "ProcessedUpdates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Invitations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP + INTERVAL '7 days'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "Invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Invitations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Groups",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "GroupParticipants",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "ExpenseShares",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Expenses",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql(
                """
                UPDATE "ExpenseShares" s
                SET "GroupId" = e."GroupId"
                FROM "Expenses" e
                WHERE e."Id" = s."ExpenseId";

                UPDATE "Invitations"
                SET "IsActive" = FALSE,
                    "RevokedAt" = CURRENT_TIMESTAMP,
                    "Version" = "Version" + 1;

                WITH duplicates AS (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "GroupId", "FromUserId", "ToUserId"
                        ORDER BY "CreatedAt", "Id") AS position
                    FROM "Transfers"
                    WHERE "Status" = 0
                )
                UPDATE "Transfers" t
                SET "Status" = 3, "Version" = "Version" + 1
                FROM duplicates d
                WHERE t."Id" = d."Id" AND d.position > 1;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Expenses_GroupId_Id",
                table: "Expenses",
                columns: new[] { "GroupId", "Id" });

            migrationBuilder.CreateTable(
                name: "ApiIdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiIdempotencyRecords", x => new { x.UserId, x.Key });
                    table.ForeignKey(
                        name: "FK_ApiIdempotencyRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_GroupId_FromUserId",
                table: "Transfers",
                columns: new[] { "GroupId", "FromUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_GroupId_Status",
                table: "Transfers",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_GroupId_ToUserId",
                table: "Transfers",
                columns: new[] { "GroupId", "ToUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_Amount",
                table: "Transfers",
                sql: "\"AmountKopecks\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_Participants",
                table: "Transfers",
                sql: "\"FromUserId\" <> \"ToUserId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_Resolution",
                table: "Transfers",
                sql: "(\"Status\" = 0 AND \"ResolvedAt\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ResolvedAt\" IS NOT NULL) OR \"Status\" = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_Status",
                table: "Transfers",
                sql: "\"Status\" IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sessions_DataJson",
                table: "Sessions",
                sql: "\"DataJson\"::jsonb IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_CreatedById",
                table: "Invitations",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_GroupId_CreatedById",
                table: "Invitations",
                columns: new[] { "GroupId", "CreatedById" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Token",
                table: "Invitations",
                sql: "\"Token\" ~ '^[0-9a-f]{32}$'");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups",
                column: "OwnerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Groups_Name",
                table: "Groups",
                sql: "length(btrim(\"Name\")) BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Groups_Type",
                table: "Groups",
                sql: "\"Type\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupParticipants_Identity",
                table: "GroupParticipants",
                sql: "(\"TelegramUserId\" IS NOT NULL AND \"ParticipantId\" = \"TelegramUserId\" AND \"ParticipantId\" > 0) OR (\"TelegramUserId\" IS NULL AND \"ParticipantId\" < 0 AND length(btrim(\"DisplayName\")) BETWEEN 1 AND 100)");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseShares_GroupId_ExpenseId",
                table: "ExpenseShares",
                columns: new[] { "GroupId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseShares_GroupId_UserId",
                table: "ExpenseShares",
                columns: new[] { "GroupId", "UserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpenseShares_Amount",
                table: "ExpenseShares",
                sql: "\"AmountKopecks\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_GroupId_AuthorId",
                table: "Expenses",
                columns: new[] { "GroupId", "AuthorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_GroupId_CreatedAt",
                table: "Expenses",
                columns: new[] { "GroupId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_GroupId_PayerId",
                table: "Expenses",
                columns: new[] { "GroupId", "PayerId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Expenses_Amount",
                table: "Expenses",
                sql: "\"AmountKopecks\" BETWEEN 1 AND 1000000000000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Expenses_Description",
                table: "Expenses",
                sql: "length(btrim(\"Description\")) BETWEEN 1 AND 200");

            migrationBuilder.CreateIndex(
                name: "IX_ApiIdempotencyRecords_ExpiresAt",
                table: "ApiIdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_GroupMembers_GroupId_AuthorId",
                table: "Expenses",
                columns: new[] { "GroupId", "AuthorId" },
                principalTable: "GroupMembers",
                principalColumns: new[] { "GroupId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_GroupParticipants_GroupId_PayerId",
                table: "Expenses",
                columns: new[] { "GroupId", "PayerId" },
                principalTable: "GroupParticipants",
                principalColumns: new[] { "GroupId", "ParticipantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Groups_GroupId",
                table: "Expenses",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseShares_Expenses_GroupId_ExpenseId",
                table: "ExpenseShares",
                columns: new[] { "GroupId", "ExpenseId" },
                principalTable: "Expenses",
                principalColumns: new[] { "GroupId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseShares_GroupParticipants_GroupId_UserId",
                table: "ExpenseShares",
                columns: new[] { "GroupId", "UserId" },
                principalTable: "GroupParticipants",
                principalColumns: new[] { "GroupId", "ParticipantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Users_OwnerId",
                table: "Groups",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "TelegramId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Groups_GroupId",
                table: "Invitations",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_CreatedById",
                table: "Invitations",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "TelegramId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Users_UserId",
                table: "Sessions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "TelegramId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_GroupParticipants_GroupId_FromUserId",
                table: "Transfers",
                columns: new[] { "GroupId", "FromUserId" },
                principalTable: "GroupParticipants",
                principalColumns: new[] { "GroupId", "ParticipantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_GroupParticipants_GroupId_ToUserId",
                table: "Transfers",
                columns: new[] { "GroupId", "ToUserId" },
                principalTable: "GroupParticipants",
                principalColumns: new[] { "GroupId", "ParticipantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Groups_GroupId",
                table: "Transfers",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_Transfers_OnePendingPair"
                ON "Transfers" ("GroupId", "FromUserId", "ToUserId")
                WHERE "Status" = 0;

                CREATE UNIQUE INDEX "IX_GroupParticipants_ActiveManagedName"
                ON "GroupParticipants" ("GroupId", lower(btrim("DisplayName")))
                WHERE "IsActive" AND "TelegramUserId" IS NULL;

                CREATE FUNCTION "SetExpenseShareGroupId"()
                RETURNS trigger AS $$
                DECLARE expected_group_id uuid;
                BEGIN
                    SELECT "GroupId" INTO expected_group_id
                    FROM "Expenses" WHERE "Id" = NEW."ExpenseId";
                    IF expected_group_id IS NULL THEN
                        RAISE EXCEPTION 'Expense % does not exist', NEW."ExpenseId";
                    END IF;
                    IF NEW."GroupId" IS NULL THEN
                        NEW."GroupId" := expected_group_id;
                    ELSIF NEW."GroupId" <> expected_group_id THEN
                        RAISE EXCEPTION 'Expense share group does not match its expense';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_ExpenseShares_SetGroupId"
                BEFORE INSERT OR UPDATE ON "ExpenseShares"
                FOR EACH ROW EXECUTE FUNCTION "SetExpenseShareGroupId"();

                CREATE FUNCTION "ValidateExpenseShareTotal"()
                RETURNS trigger AS $$
                DECLARE target_expense_id uuid;
                DECLARE expected_amount bigint;
                DECLARE actual_amount numeric;
                BEGIN
                    IF TG_TABLE_NAME = 'Expenses' THEN
                        target_expense_id := CASE WHEN TG_OP = 'DELETE' THEN OLD."Id" ELSE NEW."Id" END;
                    ELSE
                        target_expense_id := CASE WHEN TG_OP = 'DELETE' THEN OLD."ExpenseId" ELSE NEW."ExpenseId" END;
                    END IF;
                    SELECT "AmountKopecks" INTO expected_amount FROM "Expenses" WHERE "Id" = target_expense_id;
                    IF NOT FOUND THEN RETURN NULL; END IF;
                    SELECT COALESCE(sum("AmountKopecks"), 0) INTO actual_amount
                    FROM "ExpenseShares" WHERE "ExpenseId" = target_expense_id;
                    IF actual_amount <> expected_amount THEN
                        RAISE EXCEPTION 'Expense % share total % does not equal amount %', target_expense_id, actual_amount, expected_amount;
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER "TR_ExpenseShares_ValidateTotal"
                AFTER INSERT OR UPDATE OR DELETE ON "ExpenseShares"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION "ValidateExpenseShareTotal"();

                CREATE CONSTRAINT TRIGGER "TR_Expenses_ValidateShareTotal"
                AFTER INSERT OR UPDATE ON "Expenses"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION "ValidateExpenseShareTotal"();

                CREATE FUNCTION "EnforceParticipantGroupType"()
                RETURNS trigger AS $$
                DECLARE group_type integer;
                DECLARE owner_id bigint;
                BEGIN
                    SELECT "Type", "OwnerId" INTO group_type, owner_id FROM "Groups" WHERE "Id" = NEW."GroupId";
                    IF NEW."TelegramUserId" IS NULL AND group_type <> 1 THEN
                        RAISE EXCEPTION 'Managed participants are only allowed in standalone groups';
                    END IF;
                    IF NEW."TelegramUserId" IS NOT NULL AND group_type = 1 AND NEW."ParticipantId" <> owner_id THEN
                        RAISE EXCEPTION 'Only the owner can be a Telegram participant in a standalone group';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_GroupParticipants_GroupType"
                BEFORE INSERT OR UPDATE ON "GroupParticipants"
                FOR EACH ROW EXECUTE FUNCTION "EnforceParticipantGroupType"();

                CREATE FUNCTION "EnforceTransferGroupType"()
                RETURNS trigger AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Groups" WHERE "Id" = NEW."GroupId" AND "Type" <> 0) THEN
                        RAISE EXCEPTION 'Transfers are only allowed in collective groups';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_Transfers_GroupType"
                BEFORE INSERT OR UPDATE ON "Transfers"
                FOR EACH ROW EXECUTE FUNCTION "EnforceTransferGroupType"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_Transfers_GroupType" ON "Transfers";
                DROP FUNCTION IF EXISTS "EnforceTransferGroupType"();
                DROP TRIGGER IF EXISTS "TR_GroupParticipants_GroupType" ON "GroupParticipants";
                DROP FUNCTION IF EXISTS "EnforceParticipantGroupType"();
                DROP TRIGGER IF EXISTS "TR_Expenses_ValidateShareTotal" ON "Expenses";
                DROP TRIGGER IF EXISTS "TR_ExpenseShares_ValidateTotal" ON "ExpenseShares";
                DROP FUNCTION IF EXISTS "ValidateExpenseShareTotal"();
                DROP TRIGGER IF EXISTS "TR_ExpenseShares_SetGroupId" ON "ExpenseShares";
                DROP FUNCTION IF EXISTS "SetExpenseShareGroupId"();
                DROP INDEX IF EXISTS "IX_GroupParticipants_ActiveManagedName";
                DROP INDEX IF EXISTS "IX_Transfers_OnePendingPair";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_GroupMembers_GroupId_AuthorId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_GroupParticipants_GroupId_PayerId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Groups_GroupId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseShares_Expenses_GroupId_ExpenseId",
                table: "ExpenseShares");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseShares_GroupParticipants_GroupId_UserId",
                table: "ExpenseShares");

            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Users_OwnerId",
                table: "Groups");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Groups_GroupId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Users_CreatedById",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Users_UserId",
                table: "Sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_GroupParticipants_GroupId_FromUserId",
                table: "Transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_GroupParticipants_GroupId_ToUserId",
                table: "Transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Groups_GroupId",
                table: "Transfers");

            migrationBuilder.DropTable(
                name: "ApiIdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_GroupId_FromUserId",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_GroupId_Status",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_GroupId_ToUserId",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_Amount",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_Participants",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_Resolution",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_Status",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_DataJson",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_CreatedById",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_GroupId_CreatedById",
                table: "Invitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Token",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Groups_Name",
                table: "Groups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Groups_Type",
                table: "Groups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupParticipants_Identity",
                table: "GroupParticipants");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseShares_GroupId_ExpenseId",
                table: "ExpenseShares");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseShares_GroupId_UserId",
                table: "ExpenseShares");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpenseShares_Amount",
                table: "ExpenseShares");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Expenses_GroupId_Id",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_GroupId_AuthorId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_GroupId_CreatedAt",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_GroupId_PayerId",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Expenses_Amount",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Expenses_Description",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "GroupParticipants");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "ExpenseShares");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Expenses");

            migrationBuilder.AlterColumn<long>(
                name: "TelegramId",
                table: "Users",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "Sessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "UpdateId",
                table: "ProcessedUpdates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseShares_Expenses_ExpenseId",
                table: "ExpenseShares",
                column: "ExpenseId",
                principalTable: "Expenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
