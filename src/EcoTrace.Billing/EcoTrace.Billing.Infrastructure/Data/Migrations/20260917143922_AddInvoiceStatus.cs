using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "invoices");
        }
    }
}
