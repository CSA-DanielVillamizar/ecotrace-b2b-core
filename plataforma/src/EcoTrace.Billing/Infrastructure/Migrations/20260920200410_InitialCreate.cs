using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Facturas",
                columns: table => new
                {
                    FacturaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Monto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmitidaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facturas", x => x.FacturaId);
                });

            migrationBuilder.CreateTable(
                name: "Pagos",
                columns: table => new
                {
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Monto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    EstadoEscrow = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagos", x => x.PagoId);
                });

            migrationBuilder.CreateTable(
                name: "AuditoriaFinanciera",
                columns: table => new
                {
                    AuditoriaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Accion = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    EstadoResultante = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OcurridoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriaFinanciera", x => x.AuditoriaId);
                    table.ForeignKey(
                        name: "FK_AuditoriaFinanciera_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "PagoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaFinanciera_PagoId",
                table: "AuditoriaFinanciera",
                column: "PagoId");

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_CargaId",
                table: "Facturas",
                column: "CargaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_GeneradorTenantId",
                table: "Facturas",
                column: "GeneradorTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_Numero",
                table: "Facturas",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_TransportistaTenantId",
                table: "Facturas",
                column: "TransportistaTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_CargaId",
                table: "Pagos",
                column: "CargaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_GeneradorTenantId",
                table: "Pagos",
                column: "GeneradorTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_TransportistaTenantId",
                table: "Pagos",
                column: "TransportistaTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriaFinanciera");

            migrationBuilder.DropTable(
                name: "Facturas");

            migrationBuilder.DropTable(
                name: "Pagos");
        }
    }
}
