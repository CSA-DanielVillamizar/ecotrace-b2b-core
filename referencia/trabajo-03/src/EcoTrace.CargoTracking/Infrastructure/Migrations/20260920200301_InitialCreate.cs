using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.CargoTracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cargas",
                columns: table => new
                {
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Origen = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Destino = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PesoKg = table.Column<int>(type: "INTEGER", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cargas", x => x.CargaId);
                });

            migrationBuilder.CreateTable(
                name: "Asignaciones",
                columns: table => new
                {
                    AsignacionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AsignadaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asignaciones", x => x.AsignacionId);
                    table.ForeignKey(
                        name: "FK_Asignaciones_Cargas_CargaId",
                        column: x => x.CargaId,
                        principalTable: "Cargas",
                        principalColumn: "CargaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seguimientos",
                columns: table => new
                {
                    SeguimientoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Ubicacion = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Nota = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RegistradoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seguimientos", x => x.SeguimientoId);
                    table.ForeignKey(
                        name: "FK_Seguimientos_Cargas_CargaId",
                        column: x => x.CargaId,
                        principalTable: "Cargas",
                        principalColumn: "CargaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Asignaciones_CargaId",
                table: "Asignaciones",
                column: "CargaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asignaciones_ConductorId",
                table: "Asignaciones",
                column: "ConductorId");

            migrationBuilder.CreateIndex(
                name: "IX_Asignaciones_VehiculoId",
                table: "Asignaciones",
                column: "VehiculoId");

            migrationBuilder.CreateIndex(
                name: "IX_Cargas_Estado",
                table: "Cargas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Cargas_GeneradorTenantId",
                table: "Cargas",
                column: "GeneradorTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Cargas_TransportistaTenantId",
                table: "Cargas",
                column: "TransportistaTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Seguimientos_CargaId",
                table: "Seguimientos",
                column: "CargaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Asignaciones");

            migrationBuilder.DropTable(
                name: "Seguimientos");

            migrationBuilder.DropTable(
                name: "Cargas");
        }
    }
}
