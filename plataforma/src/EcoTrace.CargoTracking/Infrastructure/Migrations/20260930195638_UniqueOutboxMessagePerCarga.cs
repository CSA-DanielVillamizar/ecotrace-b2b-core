using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoTrace.CargoTracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueOutboxMessagePerCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CargaId",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CargaId",
                table: "OutboxMessages",
                column: "CargaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CargaId",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CargaId",
                table: "OutboxMessages",
                column: "CargaId");
        }
    }
}
