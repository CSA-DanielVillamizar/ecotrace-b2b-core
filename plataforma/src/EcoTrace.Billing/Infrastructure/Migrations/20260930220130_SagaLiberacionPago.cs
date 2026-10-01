using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SagaLiberacionPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SagasLiberacionPago",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "EventosEntregaConfirmada",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RecibidoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosEntregaConfirmada", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_EventosEntregaConfirmada_SagasLiberacionPago_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagasLiberacionPago",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasosSaga",
                columns: table => new
                {
                    PasoSagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SagaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Orden = table.Column<int>(type: "INTEGER", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Compensable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentosCompensacion = table.Column<int>(type: "INTEGER", nullable: false),
                    Detalle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasosSaga", x => x.PasoSagaId);
                    table.ForeignKey(
                        name: "FK_PasosSaga_SagasLiberacionPago_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagasLiberacionPago",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosEntregaConfirmada_SagaId",
                table: "EventosEntregaConfirmada",
                column: "SagaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasosSaga_SagaId_Orden",
                table: "PasosSaga",
                columns: new[] { "SagaId", "Orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SagasLiberacionPago_Estado",
                table: "SagasLiberacionPago",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_SagasLiberacionPago_PagoId",
                table: "SagasLiberacionPago",
                column: "PagoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosEntregaConfirmada");

            migrationBuilder.DropTable(
                name: "PasosSaga");

            migrationBuilder.DropTable(
                name: "SagasLiberacionPago");
        }
    }
}
