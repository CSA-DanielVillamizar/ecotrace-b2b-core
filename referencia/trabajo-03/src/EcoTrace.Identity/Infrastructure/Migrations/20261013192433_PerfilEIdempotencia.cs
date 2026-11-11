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
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperacionesIdempotentes",
                columns: table => new
                {
                    OperacionId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Clave = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Alcance = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Huella = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EstadoHttp = table.Column<int>(type: "INTEGER", nullable: false),
                    Respuesta = table.Column<string>(type: "TEXT", nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesIdempotentes", x => x.OperacionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesIdempotentes_Clave_Alcance",
                table: "OperacionesIdempotentes",
                columns: new[] { "Clave", "Alcance" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesIdempotentes");

            migrationBuilder.DropColumn(
                name: "Telefono",
                table: "Users");
        }
    }
}
