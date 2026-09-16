using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameTenantRoleProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GeneradorTenantId",
                table: "payments",
                newName: "GeneratorTenantId");

            migrationBuilder.RenameColumn(
                name: "TransportistaTenantId",
                table: "payments",
                newName: "CarrierTenantId");

            migrationBuilder.RenameColumn(
                name: "GeneradorTenantId",
                table: "invoices",
                newName: "GeneratorTenantId");

            migrationBuilder.RenameColumn(
                name: "TransportistaTenantId",
                table: "invoices",
                newName: "CarrierTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GeneratorTenantId",
                table: "payments",
                newName: "GeneradorTenantId");

            migrationBuilder.RenameColumn(
                name: "CarrierTenantId",
                table: "payments",
                newName: "TransportistaTenantId");

            migrationBuilder.RenameColumn(
                name: "GeneratorTenantId",
                table: "invoices",
                newName: "GeneradorTenantId");

            migrationBuilder.RenameColumn(
                name: "CarrierTenantId",
                table: "invoices",
                newName: "TransportistaTenantId");
        }
    }
}
