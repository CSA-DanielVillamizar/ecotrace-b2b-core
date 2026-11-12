using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarOperacionesPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacionesPago",
                columns: table => new
                {
                    OperacionPagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Operacion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesPago", x => x.OperacionPagoId);
                    table.ForeignKey(
                        name: "FK_OperacionesPago_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "PagoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesPago_IdempotencyKey",
                table: "OperacionesPago",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesPago_PagoId",
                table: "OperacionesPago",
                column: "PagoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesPago");
        }
    }
}
