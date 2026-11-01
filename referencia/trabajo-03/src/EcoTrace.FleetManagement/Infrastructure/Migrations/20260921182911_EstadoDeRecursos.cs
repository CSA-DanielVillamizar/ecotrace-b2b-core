using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.FleetManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EstadoDeRecursos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los vehiculos y conductores que ya existian quedan Disponibles.
            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Vehiculos",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Disponible");

            migrationBuilder.AddColumn<Guid>(
                name: "ReservadoParaCargaId",
                table: "Vehiculos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Conductores",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Disponible");

            migrationBuilder.AddColumn<Guid>(
                name: "ReservadoParaCargaId",
                table: "Conductores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehiculos_ReservadoParaCargaId",
                table: "Vehiculos",
                column: "ReservadoParaCargaId");

            migrationBuilder.CreateIndex(
                name: "IX_Conductores_ReservadoParaCargaId",
                table: "Conductores",
                column: "ReservadoParaCargaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehiculos_ReservadoParaCargaId",
                table: "Vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_Conductores_ReservadoParaCargaId",
                table: "Conductores");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Vehiculos");

            migrationBuilder.DropColumn(
                name: "ReservadoParaCargaId",
                table: "Vehiculos");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Conductores");

            migrationBuilder.DropColumn(
                name: "ReservadoParaCargaId",
                table: "Conductores");
        }
    }
}
