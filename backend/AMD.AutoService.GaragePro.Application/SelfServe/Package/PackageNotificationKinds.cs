using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>สัญญาแจ้งเตือนในระบบของการชำระเงิน (docs/17 §5.4) · เว็บ/แอปดู entityType/linkHint แล้วพาไปหน้าแพ็กเกจ</summary>
public static class PackageNotificationKinds
{
    public const string PaymentApproved = NotificationKinds.PackagePaymentApproved;
    public const string PaymentRejected = NotificationKinds.PackagePaymentRejected;
    public const string EntityType = "PackagePayment";
    public const string LinkHint = "package";
}
