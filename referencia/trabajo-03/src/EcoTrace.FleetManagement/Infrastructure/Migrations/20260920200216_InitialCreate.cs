using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.FleetManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conductores",
                columns: table => new
                {
                    ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegistradoPorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Licencia = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conductores", x => x.ConductorId);
                });

            migrationBuilder.CreateTable(
                name: "Vehiculos",
                columns: table => new
                {
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegistradoPorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Placa = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CapacidadKg = table.Column<int>(type: "INTEGER", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehiculos", x => x.VehiculoId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conductores_TenantId",
                table: "Conductores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Conductores_TenantId_Licencia",
                table: "Conductores",
                columns: new[] { "TenantId", "Licencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehiculos_Placa",
                table: "Vehiculos",
                column: "Placa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehiculos_TenantId",
                table: "Vehiculos",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conductores");

            migrationBuilder.DropTable(
                name: "Vehiculos");
        }
    }
}
