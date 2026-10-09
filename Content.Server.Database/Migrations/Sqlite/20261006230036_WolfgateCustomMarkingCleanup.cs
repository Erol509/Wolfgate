using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCustomMarkingCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "unused_since",
                table: "wolfgate_custom_marking_art",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_art_uploader_user_id_uploaded_at",
                table: "wolfgate_custom_marking_art",
                columns: new[] { "uploader_user_id", "uploaded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wolfgate_custom_marking_art_uploader_user_id_uploaded_at",
                table: "wolfgate_custom_marking_art");

            migrationBuilder.DropColumn(
                name: "unused_since",
                table: "wolfgate_custom_marking_art");
        }
    }
}
