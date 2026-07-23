using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;

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

        // ADR-010: pooled HttpClient via IHttpClientFactory (no ad-hoc new HttpClient()).
        // BaseAddress/default headers are set per-instance in the service ctor because the
        // Dataverse URL is resolved from runtime config, not known at registration time.
        services.AddHttpClient(RegistrationDataverseService.HttpClientName);

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

        // ── ACS + Event Grid per-boundary provisioning (messaging-communication-app-r1, task 012, FR-18) ──
        // Registered HERE (registration/provisioning module) rather than CommunicationModule to avoid a
        // parallel-task (010) ownership conflict on CommunicationModule + Services/Communication/Acs/**.
        // Integration note for main session: when task 010's Acs identity/endpoint types land, a live
        // IAcsBoundaryProvisioner (ARM management SDK) MAY be registered in CommunicationModule and will
        // supersede this Null-Object without changing AcsBoundaryProvisioningService or its callers.
        // ADR-032: registration is UNCONDITIONAL; DeferredAcsBoundaryProvisioner is the Null-Object
        // (live ACS provisioning is Bicep/operator-driven in R1 — see the task-012 runbook).
        // ADR-028/NFR-05: DeferredAcsBoundaryProvisioner consumes the DI-registered TokenCredential
        // (Program.cs → ManagedIdentityCredentialFactory); no credential is constructed inline.
        services.AddOptions<AcsProvisioningOptions>()
            .Bind(configuration.GetSection(AcsProvisioningOptions.SectionName));
        services.AddSingleton<IAcsBoundaryProvisioner, DeferredAcsBoundaryProvisioner>();
        services.AddSingleton<AcsBoundaryProvisioningService>();

        return services;
    }
}
