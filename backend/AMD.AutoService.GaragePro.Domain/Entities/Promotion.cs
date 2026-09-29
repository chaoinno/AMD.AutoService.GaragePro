using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

public class Promotion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PromotionValueKind Kind { get; set; }
    public decimal Value { get; set; }
    public decimal? MaxAmount { get; set; }
    public PromotionScope Scope { get; set; }
    public decimal? MinSubtotal { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public long UpdatedByUserId { get; set; }
    public string UpdatedByName { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}