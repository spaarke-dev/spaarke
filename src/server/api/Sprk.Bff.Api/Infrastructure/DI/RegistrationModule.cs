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
        // Bind without ValidateOnStart — config validated at runtime to avoid
        // crashing the entire API if DemoProvisioning section is missing/incomplete.
        // Registration endpoints return ProblemDetails if config is invalid.
        services.AddOptions<DemoProvisioningOptions>()
            .Bind(configuration.GetSection(DemoProvisioningOptions.SectionName))
            .ValidateDataAnnotations();

        // ADR-010: Concrete registrations (no interfaces)
        services.AddSingleton<PasswordGenerator>();
        services.AddSingleton<GraphUserService>();
        services.AddSingleton<TrackingIdGenerator>();
        services.AddSingleton<RegistrationDataverseService>();
        services.AddSingleton<RegistrationEmailService>();
        services.AddSingleton<DemoProvisioningService>();
        services.AddSingleton<EmailDomainValidator>();
        services.AddSingleton<DataverseEnvironmentService>();

        // Background services (ADR-001: BackgroundService pattern)
        services.AddHostedService<DemoExpirationService>();

        return services;
    }
}
