using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

// ---------- อ่านจาก legacy (read-only) ----------

public sealed record LegacyJobDto(
    long JobId,
    string JobNo,
    int BranchId,
    string BranchName,
    long? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    long? CarId,
    string VehicleRegistration,
    string? VehicleModel,
    string? VehicleVin,
    DateTime CreatedDate,
    DateTime? PromiseAt,
    string? LegacyStatusName);

public sealed record LegacyBranchDto(
    int BranchId, string Name, string? Address, string? TaxId, string? Phone);

public sealed record LegacyTechnicianDto(long StaffId, string Name, string? SkillLevel);

// ---------- แคตตาล็อก ----------

public sealed record CatalogItemDto(
    string Code,
    string Type,
    string Name,
    string? Compatibility,
    string Unit,
    decimal Price,
    decimal? Cost,          // null เมื่อ role ไม่มีสิทธิ์เห็นต้นทุน
    decimal? StandardHours,
    int OnHand,
    int Reserved,
    int OnOrder,
    int Available,
    string? EtaNote);

// ---------- ใบเสนอราคา ----------

public sealed record QuotationLineDto(
    Guid Id,
    int Sequence,
    string CatalogCode,
    string Name,
    string Type,
    string Source,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal? UnitCost,      // null เมื่อ role ไม่มีสิทธิ์
    decimal DiscountPercent,
    int Promotion,
    string? PromotionLabel,
    long? AssignedTechnicianId,
    string? AssignedTechnicianName,
    string? Note,
    decimal? StandardHours,
    string ApprovalStatus,
    string? RejectReason,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal PromotionAmount,
    decimal NetAmount,
    decimal? MarginAmount);  // null เมื่อ role ไม่มีสิทธิ์

public sealed record QuotationDto(
    Guid Id,
    string Code,
    int Version,
    string Status,
    string StatusLabelTh,
    string ShardKey,
    int BranchId,
    long JobId,
    string JobNo,
    QuotationPartyDto Customer,
    QuotationVehicleDto Vehicle,
    QuotationBranchDto Branch,
    IReadOnlyList<QuotationLineDto> Lines,
    QuotationTotalsDto Totals,
    QuotationApprovalDto? Approval,
    Guid? SupersedesQuotationId,
    Guid? SupersededByQuotationId,
    string? RevisionReason,
    DateTime? ValidUntil,
    bool IsExpired,
    DateTime? SentAt,
    string CreatedByUserName,
    DateTime CreatedAt,
    QuotationLockDto? Lock);

public sealed record QuotationPartyDto(string Name, string? Phone, string? TaxId, string? Address);

public sealed record QuotationVehicleDto(string Registration, string? Model, string? Vin, int? Mileage);

public sealed record QuotationBranchDto(string Name, string? Address, string? TaxId, string? Phone);

public sealed record QuotationLockDto(long UserId, string UserName, DateTime LockedAt);

public sealed record QuotationTotalsDto(
    decimal Gross,
    decimal LineDiscount,
    decimal Promotion,
    decimal Net,
    decimal VatRate,
    decimal Vat,
    decimal Total,
    decimal Deposit,
    decimal GrandTotal,
    decimal? TotalCost,      // null เมื่อ role ไม่มีสิทธิ์
    decimal? MarginAmount,
    decimal? MarginPercent,
    decimal PartsNet,
    decimal LaborNet,
    decimal LaborHours,
    QuotationApprovedTotalsDto? Approved);

public sealed record QuotationApprovedTotalsDto(
    int ApprovedCount, int RejectedCount, int PendingCount,
    decimal Net, decimal Vat, decimal Total, decimal GrandTotal);

public sealed record QuotationApprovalDto(
    int QuotationVersion,
    string SignatureImagePath,
    DateTime SignedAt,
    string ConsentText,
    string? DeviceInfo,
    string WitnessEmployeeName,
    int ApprovedLineCount,
    int RejectedLineCount,
    decimal ApprovedNetAmount);

public sealed record QuotationSummaryDto(
    Guid Id,
    string Code,
    int Version,
    string Status,
    string StatusLabelTh,
    long JobId,
    string JobNo,
    string CustomerName,
    string VehicleRegistration,
    string? VehicleModel,
    decimal Total,
    DateTime CreatedAt,
    DateTime? SentAt,
    string? AgeLabelTh);

public sealed record QuotationIssueDto(string Code, string MessageTh, Guid? LineId);

public sealed record QuotationValidationDto(
    bool IsValid,
    IReadOnlyList<QuotationIssueDto> Errors,
    IReadOnlyList<QuotationIssueDto> Warnings);

// ---------- คำสั่ง ----------

public sealed record CreateQuotationRequest(long JobId, DateTime? ValidUntil, decimal DepositAmount = 0m);

public sealed record UpsertLineRequest(
    string CatalogCode,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountPercent,
    PromotionKind Promotion,
    LineSource Source,
    long? AssignedTechnicianId,
    string? Note);

public sealed record ReviseQuotationRequest(string RevisionReason);

public sealed record LineDecisionRequest(LineApprovalStatus Decision, string? RejectReason);

public sealed record SignQuotationRequest(
    string SignatureImagePath,
    string ConsentText,
    string? DeviceInfo,
    long WitnessEmployeeId,
    string WitnessEmployeeName);
