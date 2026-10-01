using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SagaLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseHasta",
                table: "SagasLiberacionPago",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseToken",
                table: "SagasLiberacionPago",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseHasta",
                table: "SagasLiberacionPago");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "SagasLiberacionPago");
        }
    }
}
