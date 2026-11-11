using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TokensYContrasenas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContrasenaHash",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TokensRefresco",
                columns: table => new
                {
                    TokenRefrescoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiraEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevocadoEn = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensRefresco", x => x.TokenRefrescoId);
                    table.ForeignKey(
                        name: "FK_TokensRefresco_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokensRefresco_Hash",
                table: "TokensRefresco",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokensRefresco_UserId",
                table: "TokensRefresco",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokensRefresco");

            migrationBuilder.DropColumn(
                name: "ContrasenaHash",
                table: "Users");
        }
    }
}
