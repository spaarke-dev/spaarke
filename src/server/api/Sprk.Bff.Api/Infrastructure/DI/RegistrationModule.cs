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
    /// <summary>
    /// The demo self-service registration feature (approve a demo request → create a Spaarke-tenant workforce user,
    /// license it, add it to the demo group; expire it daily) runs only where <c>DemoProvisioning:AccountDomain</c> is
    /// configured — Spaarke's own platform/demo BFF. No stamp channel writes <c>DemoProvisioning:*</c> (task 261 test
    /// <c>StampGraphAppRoleEvidenceTests</c>), so on every customer stamp the feature is not registered and its
    /// <c>/api/registration/*</c> routes are not mapped.
    /// </summary>
    /// <remarks>
    /// Task 261 (G31): this is what keeps directory WRITE roles (<c>User.ReadWrite.All</c>, <c>GroupMember.ReadWrite.All</c>,
    /// <c>Directory.ReadWrite.All</c>) off stamp identities — the feature is the BFF's only app-only caller of
    /// <c>/users</c> and <c>/groups</c>. It also stops a stamp from failing at start: <see cref="DemoExpirationService"/>
    /// (a hosted service) reads <c>IOptions&lt;DemoProvisioningOptions&gt;.Value</c> in its constructor, and the
    /// options' <c>[Required]</c> keys are absent on a stamp. The feature's own required key is the switch, so the
    /// platform BFF that has it configured keeps working with no setting change.
    /// </remarks>
    public static bool IsDemoProvisioningEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[$"{DemoProvisioningOptions.SectionName}:AccountDomain"]);
    }

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

        // ADR-010: Concrete registrations (no interfaces). These four are shared with other modules
        // (external access, communication, identity-link reconciliation) and are registered everywhere.
        services.AddSingleton<PasswordGenerator>();
        services.AddSingleton<TrackingIdGenerator>();
        services.AddSingleton<RegistrationDataverseService>();
        services.AddSingleton<RegistrationEmailService>();
        services.AddSingleton<DataverseEnvironmentService>();

        // Demo self-service registration — Spaarke's platform/demo BFF only (task 261; see IsDemoProvisioningEnabled).
        if (IsDemoProvisioningEnabled(configuration))
        {
            services.AddSingleton<GraphUserService>();
            services.AddSingleton<DemoProvisioningService>();
            services.AddSingleton<EmailDomainValidator>();

            // Hand-rolled timer BackgroundService (existing debt — migrates to an IScheduledJob when next touched, ADR-052 §1)
            services.AddHostedService<DemoExpirationService>();
        }

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
