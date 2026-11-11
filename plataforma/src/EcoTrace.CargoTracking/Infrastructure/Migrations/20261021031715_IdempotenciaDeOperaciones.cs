using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.CargoTracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdempotenciaDeOperaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacionesProcesadas",
                columns: table => new
                {
                    OperacionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Resultado = table.Column<string>(type: "TEXT", nullable: false),
                    ProcesadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesProcesadas", x => x.OperacionId);
                    table.ForeignKey(
                        name: "FK_OperacionesProcesadas_Cargas_CargaId",
                        column: x => x.CargaId,
                        principalTable: "Cargas",
                        principalColumn: "CargaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesProcesadas_CargaId",
                table: "OperacionesProcesadas",
                column: "CargaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesProcesadas");
        }
    }
}
