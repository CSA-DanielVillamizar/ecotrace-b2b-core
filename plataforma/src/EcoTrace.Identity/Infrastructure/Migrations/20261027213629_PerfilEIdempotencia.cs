using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerfilEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Telefono",
                table: "Users",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperacionesProcesadas",
                columns: table => new
                {
                    OperacionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Resultado = table.Column<string>(type: "TEXT", nullable: false),
                    ProcesadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesProcesadas", x => x.OperacionId);
                    table.ForeignKey(
                        name: "FK_OperacionesProcesadas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesProcesadas_UserId",
                table: "OperacionesProcesadas",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesProcesadas");

            migrationBuilder.DropColumn(
                name: "Telefono",
                table: "Users");
        }
    }
}
