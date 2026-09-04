using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Intake;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Attachments;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Suppliers;
using AMD.AutoService.GaragePro.Application.Warehouses;
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

        services.Configure<VehicleImageOptions>(
            configuration.GetSection(VehicleImageOptions.SectionName));

        services.Configure<StaffImageOptions>(
            configuration.GetSection(StaffImageOptions.SectionName));

        services.AddDbContext<ServiceDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("ServiceDb"),
                sql => sql.EnableRetryOnFailure(3)));

        services.AddScoped<IQuotationRepository, QuotationRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IMasterDataRepository, MasterDataRepository>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ICatalogCategoryService, CatalogCategoryService>();
        services.AddScoped<IPurchasingRepository, PurchasingRepository>();
        services.AddScoped<AMD.AutoService.GaragePro.Application.Purchasing.PurchasingService>();
        services.AddSingleton(configuration.GetSection("Purchasing").Get<AMD.AutoService.GaragePro.Application.Purchasing.PurchasingOptions>()
            ?? new AMD.AutoService.GaragePro.Application.Purchasing.PurchasingOptions());
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IJobNumberGenerator, JobNumberGenerator>();
        services.AddScoped<IIntakeChecklistRepository, IntakeChecklistRepository>();
        services.AddScoped<IIntakeChecklistService, IntakeChecklistService>();
        services.AddScoped<ILegacyReader, LegacyReader>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IJobService, JobService>();

        services.AddScoped<ILegacyUserReader, LegacyUserReader>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IAttachmentStorage, AttachmentStorage>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<ICustomerVehicleRepository, CustomerVehicleRepository>();
        services.AddScoped<ICustomerVehicleService, CustomerVehicleService>();
        services.AddSingleton<IVehicleImageStorage, VehicleImageStorage>();
        services.AddSingleton<IStaffImageStorage, StaffImageStorage>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
