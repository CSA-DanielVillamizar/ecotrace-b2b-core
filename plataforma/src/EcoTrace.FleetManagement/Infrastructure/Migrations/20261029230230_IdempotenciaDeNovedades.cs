using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.FleetManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdempotenciaDeNovedades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaveIdempotencia",
                table: "Novedades",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Novedades_ClaveIdempotencia",
                table: "Novedades",
                column: "ClaveIdempotencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Novedades_ClaveIdempotencia",
                table: "Novedades");

            migrationBuilder.DropColumn(
                name: "ClaveIdempotencia",
                table: "Novedades");
        }
    }
}
