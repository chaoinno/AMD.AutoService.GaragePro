using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseApprovalSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedLinesJson",
                table: "svc_PurchaseDocument",
                type: "nvarchar(max)",
                nullable: true);

            // Keep existing approvals as the baseline for future item revisions.
            migrationBuilder.Sql("""
                UPDATE d SET ApprovedLinesJson = (
                    SELECT l.CatalogItemId, l.Code, l.Name, l.Quantity, l.UnitCost
                    FROM svc_PurchaseLine l WHERE l.DocumentId = d.Id ORDER BY l.Code FOR JSON PATH)
                FROM svc_PurchaseDocument d
                WHERE d.Status IN ('approved', 'converted', 'sent', 'partial', 'complete');

                -- Orders already converted by the old flow inherit their PR approval.
                -- Changed order values stay pending; only identical orders become approved.
                UPDATE po SET ApprovedLinesJson = pr.ApprovedLinesJson
                FROM svc_PurchaseDocument po JOIN svc_PurchaseDocument pr ON pr.Id = po.SourceRequestId
                WHERE po.Kind = 'PO' AND po.Status IN ('draft', 'pending')
                  AND po.LegacyShardKey = pr.LegacyShardKey AND po.LegacyBranchId = pr.LegacyBranchId
                  AND pr.Status = 'converted';

                UPDATE po SET Status = 'approved', ApprovedBy = pr.ApprovedBy, ApprovedAt = pr.ApprovedAt
                FROM svc_PurchaseDocument po JOIN svc_PurchaseDocument pr ON pr.Id = po.SourceRequestId
                WHERE po.Kind = 'PO' AND po.Status IN ('draft', 'pending') AND pr.Status = 'converted'
                  AND po.LegacyShardKey = pr.LegacyShardKey AND po.LegacyBranchId = pr.LegacyBranchId
                  AND NOT EXISTS (
                    SELECT CatalogItemId, Quantity, UnitCost FROM svc_PurchaseLine WHERE DocumentId = po.Id
                    EXCEPT SELECT CatalogItemId, Quantity, UnitCost FROM svc_PurchaseLine WHERE DocumentId = pr.Id)
                  AND NOT EXISTS (
                    SELECT CatalogItemId, Quantity, UnitCost FROM svc_PurchaseLine WHERE DocumentId = pr.Id
                    EXCEPT SELECT CatalogItemId, Quantity, UnitCost FROM svc_PurchaseLine WHERE DocumentId = po.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedLinesJson",
                table: "svc_PurchaseDocument");
        }
    }
}
