using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Attachments;
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

        services.AddDbContext<ServiceDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("ServiceDb"),
                sql => sql.EnableRetryOnFailure(3)));

        services.AddScoped<IQuotationRepository, QuotationRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ILegacyReader, LegacyReader>();
        services.AddScoped<ILegacyJobWriter, LegacyJobWriter>();
        services.AddScoped<IQuotationService, QuotationService>();

        services.AddScoped<ILegacyUserReader, LegacyUserReader>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IAttachmentStorage, AttachmentStorage>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
