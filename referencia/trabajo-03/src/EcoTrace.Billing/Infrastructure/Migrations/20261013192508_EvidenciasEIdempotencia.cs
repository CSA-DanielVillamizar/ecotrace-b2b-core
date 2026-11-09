using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvidenciasEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenciasEntrega",
                columns: table => new
                {
                    EvidenciaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegistradaPorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Referencia = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RegistradaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenciasEntrega", x => x.EvidenciaId);
                    table.ForeignKey(
                        name: "FK_EvidenciasEntrega_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "PagoId",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "IX_EvidenciasEntrega_PagoId",
                table: "EvidenciasEntrega",
                column: "PagoId");

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
                name: "EvidenciasEntrega");

            migrationBuilder.DropTable(
                name: "OperacionesIdempotentes");
        }
    }
}
