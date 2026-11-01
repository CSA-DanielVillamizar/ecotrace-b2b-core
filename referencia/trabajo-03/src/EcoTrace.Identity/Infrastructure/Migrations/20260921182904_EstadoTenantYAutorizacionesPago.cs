using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EstadoTenantYAutorizacionesPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las organizaciones que ya existian quedan Activas y en la version 1.
            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Tenants",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Activo");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Tenants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AutorizacionesPago",
                columns: table => new
                {
                    AutorizacionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PagoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutorizacionesPago", x => x.AutorizacionId);
                    table.ForeignKey(
                        name: "FK_AutorizacionesPago_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionesPago_PagoId",
                table: "AutorizacionesPago",
                column: "PagoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionesPago_TenantId",
                table: "AutorizacionesPago",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutorizacionesPago");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tenants");
        }
    }
}
