using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class HomeSectionItemIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand written. EF scaffolds this as a drop and an add, which throws away every
            // pinned collection and genre row on the server.
            migrationBuilder.AddColumn<string>(
                name: "ItemIds",
                table: "HomeSection",
                type: "TEXT",
                nullable: false,
                defaultValue: string.Empty);

            // Ids are stored as a comma separated list in the form Guid.ToString("N") writes,
            // which is the dashless lower case one.
            migrationBuilder.Sql(
                """
                UPDATE "HomeSection"
                SET "ItemIds" = CASE
                    WHEN "ItemId" IS NULL THEN ''
                    ELSE lower(replace("ItemId", '-', ''))
                END
                """);

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "HomeSection");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemId",
                table: "HomeSection",
                type: "TEXT",
                nullable: true);

            // Only the first binding survives going back, since the old column holds one id. The
            // dashes and the case are put back so it reads like every other stored id.
            migrationBuilder.Sql(
                """
                UPDATE "HomeSection"
                SET "ItemId" = CASE
                    WHEN "ItemIds" = '' THEN NULL
                    ELSE upper(
                        substr("ItemIds", 1, 8) || '-' ||
                        substr("ItemIds", 9, 4) || '-' ||
                        substr("ItemIds", 13, 4) || '-' ||
                        substr("ItemIds", 17, 4) || '-' ||
                        substr("ItemIds", 21, 12))
                END
                """);

            migrationBuilder.DropColumn(
                name: "ItemIds",
                table: "HomeSection");
        }
    }
}
