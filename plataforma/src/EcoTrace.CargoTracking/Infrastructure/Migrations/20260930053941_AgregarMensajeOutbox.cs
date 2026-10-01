using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.CargoTracking.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AgregarMensajeOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MensajesOutbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                OcurrioEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                ProximoIntentoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                UltimoError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MensajesOutbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MensajesOutbox_Estado_ProximoIntentoEn",
            table: "MensajesOutbox",
            columns: new[] { "Estado", "ProximoIntentoEn" });

        migrationBuilder.CreateIndex(
            name: "IX_MensajesOutbox_EventId",
            table: "MensajesOutbox",
            column: "EventId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MensajesOutbox");
    }
}
