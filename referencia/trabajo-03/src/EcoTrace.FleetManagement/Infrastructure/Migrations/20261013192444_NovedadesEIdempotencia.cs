using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.FleetManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NovedadesEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NovedadesVehiculo",
                columns: table => new
                {
                    NovedadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportadaPorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ReportadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovedadesVehiculo", x => x.NovedadId);
                    table.ForeignKey(
                        name: "FK_NovedadesVehiculo_Vehiculos_VehiculoId",
                        column: x => x.VehiculoId,
                        principalTable: "Vehiculos",
                        principalColumn: "VehiculoId",
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
                name: "IX_NovedadesVehiculo_VehiculoId",
                table: "NovedadesVehiculo",
                column: "VehiculoId");

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
                name: "NovedadesVehiculo");

            migrationBuilder.DropTable(
                name: "OperacionesIdempotentes");
        }
    }
}
