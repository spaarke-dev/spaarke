using Sprk.Bff.Api.Configuration;

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

        // Services will be registered here as they are created in subsequent tasks:
        // - GraphUserService (Task 010)
        // - RegistrationDataverseService (Task 011)
        // - RegistrationEmailService (Task 014)
        // - DemoProvisioningService (Task 020)
        // - EmailDomainValidator (Task 021)
        // - DemoExpirationService (Task 030) — hosted service

        return services;
    }
}
