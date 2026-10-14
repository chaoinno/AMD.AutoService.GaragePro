using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Handover;
using AMD.AutoService.GaragePro.Application.Intake;
using AMD.AutoService.GaragePro.Application.JobChat;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Application.Pos;
using AMD.AutoService.GaragePro.Application.Qc;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.Reports;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Notifications;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Attachments;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Suppliers;
using AMD.AutoService.GaragePro.Application.Warehouses;
using AMD.AutoService.GaragePro.Application.Promotions;
using AMD.AutoService.GaragePro.Application.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGarageProInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LegacyShardOptions>(
            configuration.GetSection(LegacyShardOptions.SectionName));

        services.Configure<AttachmentOptions>(
            configuration.GetSection(AttachmentOptions.SectionName));

        services.Configure<FtpOptions>(
            configuration.GetSection(FtpOptions.SectionName));
        services.Configure<LineMessagingOptions>(
            configuration.GetSection(LineMessagingOptions.SectionName));
        services.AddSingleton<IContactNotifier, LineContactNotifier>();
        services.AddScoped<IContactRequestService, ContactRequestService>();

        services.Configure<VehicleImageOptions>(
            configuration.GetSection(VehicleImageOptions.SectionName));

        services.Configure<StaffImageOptions>(
            configuration.GetSection(StaffImageOptions.SectionName));

        // ฐานของ svc_* เลือกตาม shard ของผู้ใช้ใน scope นี้ (sp1 → GarageSelfServe · อื่นๆ → GarageService)
        // options เป็น Scoped (ค่าเริ่มต้นของ overload นี้) จึงอ่าน ICurrentUser ของ scope ได้ — รวม override จาก ShardScopeFactory
        services.AddDbContext<ServiceDbContext>((sp, options) =>
            options.UseSqlServer(
                sp.GetRequiredService<IServiceDbConnectionResolver>().Resolve(sp.GetRequiredService<ICurrentUser>().ShardKey),
                sql => sql.EnableRetryOnFailure(3)));

        services.AddScoped<IQuotationRepository, QuotationRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IMasterDataRepository, MasterDataRepository>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IQuotationTemplateRepository, QuotationTemplateRepository>();
        services.AddScoped<IQuotationTemplateService, QuotationTemplateService>();
        services.AddScoped<ICatalogCategoryService, CatalogCategoryService>();
        services.AddScoped<IBranchDefaultsSeeder, AMD.AutoService.GaragePro.Application.MasterData.BranchDefaultsSeeder>();
        services.AddScoped<IPurchasingRepository, PurchasingRepository>();
        services.AddScoped<IBranchPinVerifier, BranchPinVerifier>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Purchasing.PurchasingService>();
        services.AddSingleton(configuration.GetSection("Purchasing").Get<AMD.AutoService.GaragePro.Application.Purchasing.PurchasingOptions>()
            ?? new AMD.AutoService.GaragePro.Application.Purchasing.PurchasingOptions());
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IJobNumberGenerator, JobNumberGenerator>();
        services.AddScoped<IJobBoardRepository, JobBoardRepository>();
        services.AddScoped<JobBoardService>();
        services.AddScoped<IIntakeChecklistRepository, IntakeChecklistRepository>();
        services.AddScoped<IIntakeChecklistService, IntakeChecklistService>();
        services.AddScoped<IQcChecklistRepository, QcChecklistRepository>();
        services.AddScoped<IQcChecklistService, QcChecklistService>();
        services.AddScoped<IWorkIntervalRepository, WorkIntervalRepository>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Work.IWorkIntervalHook,
            AMD.AutoService.GaragePro.Application.Work.WorkIntervalHook>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Work.IWorkTimeService,
            AMD.AutoService.GaragePro.Application.Work.WorkTimeService>();
        services.AddSingleton(configuration.GetSection("Work").Get<AMD.AutoService.GaragePro.Application.Work.WorkTimeOptions>()
            ?? new AMD.AutoService.GaragePro.Application.Work.WorkTimeOptions());
        services.AddScoped<IPosRepository, PosRepository>();
        services.AddScoped<IReceiptNumberGenerator, ReceiptNumberGenerator>();
        services.AddScoped<IPosService, PosService>();
        services.AddScoped<ITaxInvoiceRepository, TaxInvoiceRepository>();
        services.AddScoped<ITaxInvoiceService, TaxInvoiceService>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IHandoverRepository, HandoverRepository>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<IJobChatRepository, JobChatRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Notifications.INotificationPublisher,
            AMD.AutoService.GaragePro.Infrastructure.Notifications.NotificationPublisher>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Notifications.INotificationService,
            AMD.AutoService.GaragePro.Application.Notifications.NotificationService>();
        services.AddScoped<IJobChatService, JobChatService>();
        services.AddScoped<IReportsRepository, ReportsRepository>();
        services.AddScoped<ReportsService>();
        services.AddScoped<ILegacyReader, LegacyReader>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IJobService, JobService>();

        services.AddScoped<ILegacyUserReader, LegacyUserReader>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILoginIdentityResolver>(SelfServeInfrastructure.CreateLoginIdentityResolver); // B5: อีเมล/SP0000.X00 → sp1 · อื่นๆ → db2
        services.AddSingleton<ILoginAttemptLimiter, AMD.AutoService.GaragePro.Infrastructure.Auth.MemoryLoginAttemptLimiter>();

        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IAttachmentStorage, FtpAttachmentStorage>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<ICustomerVehicleRepository, CustomerVehicleRepository>();
        services.AddScoped<ICustomerVehicleService, CustomerVehicleService>();
        services.AddSingleton<IVehicleImageStorage, VehicleImageStorage>();
        services.AddSingleton<IStaffImageStorage, StaffImageStorage>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddSingleton(TimeProvider.System);

        // self-serve (shard sp1) — ต้องอยู่ท้ายสุด เพื่อให้ WP ถัดไปแทนที่ implementation ได้ที่ไฟล์เดียว
        services.AddSelfServeInfrastructure(configuration);

        return services;
    }
}
