using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCustomMarkings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_markings",
                table: "profile",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "wolfgate_custom_marking_art",
                columns: table => new
                {
                    hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    png = table.Column<byte[]>(type: "BLOB", nullable: false),
                    uploader_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    blocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_custom_marking_art", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_custom_marking",
                columns: table => new
                {
                    wolfgate_custom_marking_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    art_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    placement = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_custom_marking", x => x.wolfgate_custom_marking_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_custom_marking_art",
                        column: x => x.art_hash,
                        principalTable: "wolfgate_custom_marking_art",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_art_hash",
                table: "wolfgate_custom_marking",
                column: "art_hash");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_player_user_id",
                table: "wolfgate_custom_marking",
                column: "player_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_custom_marking");

            migrationBuilder.DropTable(
                name: "wolfgate_custom_marking_art");

            migrationBuilder.DropColumn(
                name: "custom_markings",
                table: "profile");
        }
    }
}
