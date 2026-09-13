using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class HomeSectionKeyAndOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sections that already exist are ones the user chose to have, so they stay on.
            // Defaulting this to false would silently empty every existing home screen.
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "HomeSection",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemId",
                table: "HomeSection",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxItems",
                table: "HomeSection",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "HomeSection",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: string.Empty);

            // The provider key of each stored type is its lower-cased name, which is also what the
            // legacy display preferences API has always emitted for it.
            migrationBuilder.Sql(
                """
                UPDATE "HomeSection" SET "Key" = CASE "Type"
                    WHEN 1 THEN 'smalllibrarytiles'
                    WHEN 2 THEN 'librarybuttons'
                    WHEN 3 THEN 'activerecordings'
                    WHEN 4 THEN 'resume'
                    WHEN 5 THEN 'resumeaudio'
                    WHEN 6 THEN 'latestmedia'
                    WHEN 7 THEN 'nextup'
                    WHEN 8 THEN 'livetv'
                    WHEN 9 THEN 'resumebook'
                    ELSE 'none'
                END
                """);

            migrationBuilder.DropColumn(
                name: "Type",
                table: "HomeSection");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "HomeSection",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "HomeSection" SET "Type" = CASE "Key"
                    WHEN 'smalllibrarytiles' THEN 1
                    WHEN 'librarybuttons' THEN 2
                    WHEN 'activerecordings' THEN 3
                    WHEN 'resume' THEN 4
                    WHEN 'resumeaudio' THEN 5
                    WHEN 'latestmedia' THEN 6
                    WHEN 'nextup' THEN 7
                    WHEN 'livetv' THEN 8
                    WHEN 'resumebook' THEN 9
                    ELSE 0
                END
                """);

            migrationBuilder.DropColumn(
                name: "Active",
                table: "HomeSection");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "HomeSection");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "HomeSection");

            migrationBuilder.DropColumn(
                name: "MaxItems",
                table: "HomeSection");
        }
    }
}
