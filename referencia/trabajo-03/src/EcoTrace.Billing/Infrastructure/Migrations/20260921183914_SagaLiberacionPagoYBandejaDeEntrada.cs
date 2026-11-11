using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SagaLiberacionPagoYBandejaDeEntrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosRecibidos",
                columns: table => new
                {
                    EventoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    RecibidoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosRecibidos", x => x.EventoId);
                });

            migrationBuilder.CreateTable(
                name: "SagasLiberacionPago",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventoOrigen = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProximoIntentoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagasLiberacionPago", x => x.SagaId);
                    table.ForeignKey(
                        name: "FK_SagasLiberacionPago_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "PagoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SagasPasos",
                columns: table => new
                {
                    PasoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Orden = table.Column<int>(type: "INTEGER", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Compensable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentosCompensacion = table.Column<int>(type: "INTEGER", nullable: false),
                    Detalle = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagasPasos", x => x.PasoId);
                    table.ForeignKey(
                        name: "FK_SagasPasos_SagasLiberacionPago_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagasLiberacionPago",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SagasLiberacionPago_Estado_ProximoIntentoEn",
                table: "SagasLiberacionPago",
                columns: new[] { "Estado", "ProximoIntentoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_SagasLiberacionPago_PagoId",
                table: "SagasLiberacionPago",
                column: "PagoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SagasPasos_SagaId_Orden",
                table: "SagasPasos",
                columns: new[] { "SagaId", "Orden" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosRecibidos");

            migrationBuilder.DropTable(
                name: "SagasPasos");

            migrationBuilder.DropTable(
                name: "SagasLiberacionPago");
        }
    }
}
