using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Demo Registration and Provisioning feature (ADR-010: feature module pattern).
/// Registers registration services and configuration.
/// </summary>
public static class RegistrationModule
{
    public static IServiceCollection AddRegistrationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind DemoProvisioningOptions from "DemoProvisioning" section
        services.AddOptions<DemoProvisioningOptions>()
            .Bind(configuration.GetSection(DemoProvisioningOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ADR-010: Concrete registrations (no interfaces)
        services.AddSingleton<TrackingIdGenerator>();
        services.AddSingleton<RegistrationDataverseService>();
        services.AddSingleton<RegistrationEmailService>();

        // Services to be registered in subsequent tasks:
        // - DemoProvisioningService (Task 020)
        // - EmailDomainValidator (Task 021)
        // - DemoExpirationService (Task 030) — hosted service

        return services;
    }
}
