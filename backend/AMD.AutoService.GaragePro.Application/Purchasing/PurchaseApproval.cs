using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Purchasing;

internal static class PurchaseApproval
{
    internal sealed record Line(Guid CatalogItemId, string Code, string Name, int Quantity, decimal UnitCost);
    internal static Line Value(PurchaseLine line) => new(line.CatalogItemId, line.Code, line.Name, line.Quantity, line.UnitCost);
    internal static List<Line> Baseline(PurchaseDocument doc) => doc.ApprovedLinesJson is not null
        ? JsonSerializer.Deserialize<List<Line>>(doc.ApprovedLinesJson)!
        : doc.Status is "approved" or "converted" or "sent" or "partial" or "complete"
            ? doc.Lines.Select(Value).ToList() : [];
    internal static void Preserve(PurchaseDocument doc)
    {
        if (doc.ApprovedLinesJson is null && doc.Status is "approved" or "converted" or "sent" or "partial" or "complete")
            doc.ApprovedLinesJson = JsonSerializer.Serialize(Baseline(doc));
        if (doc.ApprovedLinesJson is not null && doc.ApprovedHasVat is null)
        {
            doc.ApprovedHasVat = doc.HasVat;
            doc.ApprovedVatRate = doc.VatRate;
        }
    }
    internal static void Approve(PurchaseDocument doc)
    {
        doc.ApprovedLinesJson = JsonSerializer.Serialize(doc.Lines.Select(Value));
        doc.ApprovedHasVat = doc.HasVat;
        doc.ApprovedVatRate = doc.VatRate;
    }
    internal static PurchaseTaxChangeDto? TaxChange(PurchaseDocument doc) => doc.ApprovedHasVat.HasValue
        && (doc.ApprovedHasVat.Value != doc.HasVat || doc.ApprovedVatRate != doc.VatRate)
        ? new(doc.ApprovedHasVat.Value, doc.ApprovedVatRate ?? doc.VatRate, doc.HasVat, doc.VatRate) : null;
    internal static bool NeedsApproval(PurchaseDocument doc) => Changes(doc).Count > 0 || TaxChange(doc) is not null;
    internal static bool Matches(Line before, PurchaseLine after) => before.Quantity == after.Quantity && before.UnitCost == after.UnitCost;
    internal static List<PurchaseApprovalChangeDto> Changes(PurchaseDocument doc, IReadOnlyDictionary<Guid, Line>? approved = null)
    {
        var baseline = approved ?? Baseline(doc).ToDictionary(x => x.CatalogItemId);
        var current = doc.Lines.ToDictionary(x => x.CatalogItemId);
        var changes = new List<PurchaseApprovalChangeDto>();
        foreach (var line in doc.Lines)
        {
            baseline.TryGetValue(line.CatalogItemId, out var before);
            if (before is null || !Matches(before, line)) changes.Add(new(line.CatalogItemId, line.Code, line.Name,
                before is null ? "added" : "modified", before?.Quantity, before?.UnitCost, line.Quantity, line.UnitCost));
        }
        foreach (var before in baseline.Values.Where(x => !current.ContainsKey(x.CatalogItemId)))
            changes.Add(new(before.CatalogItemId, before.Code, before.Name, "removed", before.Quantity, before.UnitCost, null, null));
        return changes.OrderBy(x => x.Code).ToList();
    }
    // A PR approval grants only its changed values; unrelated PO revisions still need approval.
    internal static void InheritChanges(PurchaseDocument po, PurchaseDocument pr, IReadOnlyList<PurchaseApprovalChangeDto> changes)
    {
        var baseline = Baseline(po).ToDictionary(x => x.CatalogItemId);
        foreach (var change in changes)
        {
            var requestLine = pr.Lines.SingleOrDefault(x => x.CatalogItemId == change.CatalogItemId);
            var orderLine = po.Lines.SingleOrDefault(x => x.CatalogItemId == change.CatalogItemId);
            if (requestLine is null && orderLine is null) baseline.Remove(change.CatalogItemId);
            else if (requestLine is not null && orderLine is not null && Matches(Value(requestLine), orderLine))
                baseline[change.CatalogItemId] = Value(orderLine);
        }
        po.ApprovedLinesJson = JsonSerializer.Serialize(baseline.Values);
        if (TaxChange(pr) is not null && po.HasVat == pr.HasVat && po.VatRate == pr.VatRate)
        {
            po.ApprovedHasVat = pr.HasVat;
            po.ApprovedVatRate = pr.VatRate;
        }
    }
}
