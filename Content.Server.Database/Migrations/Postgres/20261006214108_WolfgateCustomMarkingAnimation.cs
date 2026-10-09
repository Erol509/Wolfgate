using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateCustomMarkingAnimation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "erase",
                table: "wolfgate_custom_marking_art",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "frame_times",
                table: "wolfgate_custom_marking_art",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "erase",
                table: "wolfgate_custom_marking_art");

            migrationBuilder.DropColumn(
                name: "frame_times",
                table: "wolfgate_custom_marking_art");
        }
    }
}
