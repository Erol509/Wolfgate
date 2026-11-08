using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateGameScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_game_score",
                columns: table => new
                {
                    wolfgate_game_score_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    game = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_game_score", x => x.wolfgate_game_score_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_game_score_game",
                table: "wolfgate_game_score",
                column: "game");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_game_score_player_user_id_game_level",
                table: "wolfgate_game_score",
                columns: new[] { "player_user_id", "game", "level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_game_score");
        }
    }
}
