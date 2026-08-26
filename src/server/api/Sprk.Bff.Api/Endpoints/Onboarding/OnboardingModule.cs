using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// DI composition for the BFF Onboarding surface (task 042 — customer-
/// provisioning-orchestration-r1). Registers:
/// <list type="bullet">
/// <item><see cref="OnboardingOptions"/> bound from the <c>Onboarding</c> config section with fail-fast validation.</item>
/// <item><see cref="HmacSignatureVerifier"/> (singleton — thread-safe, options-driven).</item>
/// <item><see cref="IProvisioningEnqueuer"/> → <see cref="ServiceBusProvisioningEnqueuer"/> (singleton — owns a single <see cref="Azure.Messaging.ServiceBus.ServiceBusSender"/> for the L2 queue).</item>
/// </list>
///
/// <para>
/// Fail-fast validation (spec.md NFR-05 + r3 task 061 config-validation
/// classification): <see cref="OnboardingOptions"/> is <b>Tier 1</b> —
/// missing / empty <see cref="OnboardingOptions.HmacSigningKey"/> throws at
/// start-up in deployed envs (Production / Staging / Demo). Development +
/// Testing envs allow empty when <see cref="OnboardingOptions.EnableDevBypass"/>
/// is set OR the section is absent entirely (dev-scaffold parity — unit
/// tests substitute the verifier directly).
/// </para>
///
/// <para>
/// Placement Justification (CLAUDE.md §10): the Onboarding folder is a
/// NEW top-level Endpoints/ folder (per ADR-010 endpoint organization).
/// Additions strictly bounded per spec.md ADR-010 row: single endpoint +
/// HMAC verifier + Service Bus enqueuer (~3 registrations total). No new
/// AI dependencies (ADR-013 forcing-function rule).
/// </para>
/// </summary>
public static class OnboardingModule
{
    /// <summary>
    /// Registers the Onboarding surface (H0.5 consent-callback) with the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Root configuration (reads the <c>Onboarding</c> section).</param>
    /// <param name="environment">Host environment — used to decide whether Tier-1 fail-fast applies.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddOnboardingModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<OnboardingOptions>()
            .Bind(configuration.GetSection(OnboardingOptions.SectionName))
            .Validate(
                opts =>
                {
                    // Fail-fast in deployed envs when the signing key is missing.
                    // Development + Testing preserve dev-convenience pass-through —
                    // parity with CacheModule §F.2.1 env-name allow-list rule.
                    var isLocalLike = environment.IsDevelopment()
                        || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
                    if (isLocalLike || opts.EnableDevBypass)
                    {
                        return true;
                    }
                    return !string.IsNullOrEmpty(opts.HmacSigningKey);
                },
                "Onboarding:HmacSigningKey is required in deployed environments (Development/Testing only bypass via " +
                "Onboarding:EnableDevBypass). Bind it via App Service settings + Key Vault reference — see task 042 notes.")
            .ValidateOnStart();

        services.AddSingleton<HmacSignatureVerifier>();

        // Enqueuer — reuses the ServiceBusClient singleton registered by
        // JobProcessingModule (Program.cs earlier line). No new SB client
        // instance; no new connection string; no new NuGet package. Singleton
        // because the underlying ServiceBusSender is thread-safe + long-lived.
        services.AddSingleton<IProvisioningEnqueuer, ServiceBusProvisioningEnqueuer>();

        return services;
    }
}
