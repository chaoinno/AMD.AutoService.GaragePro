using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItemMinMax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxStock",
                table: "svc_CatalogItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinStock",
                table: "svc_CatalogItem",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxStock",
                table: "svc_CatalogItem");

            migrationBuilder.DropColumn(
                name: "MinStock",
                table: "svc_CatalogItem");
        }
    }
}
