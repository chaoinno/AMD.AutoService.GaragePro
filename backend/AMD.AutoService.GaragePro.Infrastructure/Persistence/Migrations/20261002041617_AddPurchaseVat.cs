using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovedHasVat",
                table: "svc_PurchaseDocument",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedVatRate",
                table: "svc_PurchaseDocument",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasVat",
                table: "svc_PurchaseDocument",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "svc_PurchaseDocument",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.07m);

            // Preserve the tax baseline of previously approved documents, including revisions.
            migrationBuilder.Sql("""
                UPDATE svc_PurchaseDocument SET ApprovedHasVat = HasVat, ApprovedVatRate = VatRate
                WHERE ApprovedLinesJson IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedHasVat",
                table: "svc_PurchaseDocument");

            migrationBuilder.DropColumn(
                name: "ApprovedVatRate",
                table: "svc_PurchaseDocument");

            migrationBuilder.DropColumn(
                name: "HasVat",
                table: "svc_PurchaseDocument");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "svc_PurchaseDocument");
        }
    }
}
