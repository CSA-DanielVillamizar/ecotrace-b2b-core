using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.CargoTracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CargaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConductorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneradorTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransportistaTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    UltimoError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProximoIntentoEn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Cargas_CargaId",
                        column: x => x.CargaId,
                        principalTable: "Cargas",
                        principalColumn: "CargaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CargaId",
                table: "OutboxMessages",
                column: "CargaId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Estado",
                table: "OutboxMessages",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProximoIntentoEn",
                table: "OutboxMessages",
                column: "ProximoIntentoEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
