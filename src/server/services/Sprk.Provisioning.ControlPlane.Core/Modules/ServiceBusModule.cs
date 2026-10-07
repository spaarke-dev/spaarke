// -----------------------------------------------------------------------------
// ServiceBusModule.cs
//
// L2 CONTROL-PLANE Service Bus composition (task 038).
//
// SPEC / ADR references:
//   - spec.md FR-22:      Fire-and-forget execution model. This module
//                         registers the enqueue side; the BFF's
//                         ServiceBusJobProcessor is the receive side.
//   - spec.md §4.2 R20:   L2 endpoints return 202 <100ms; enqueue is the
//                         critical-path operation.
//   - ADR-028:            DefaultAzureCredential (UAMI-pinned via
//                         ManagedIdentity:ClientId app-setting) — NEVER
//                         account-key credential. The BFF's classic
//                         ServiceBusOptions.ConnectionString path is
//                         DELIBERATELY NOT reused here — L2 is a fresh
//                         codebase; MI-outbound is canonical from day 1.
//   - ADR-010:            Feature-module extension method keeps Program.cs
//                         at ~15 non-framework DI lines (NFR-07 god-class
//                         ratchet).
//   - ADR-032:            UNCONDITIONAL registration — the L2 REST endpoint
//                         layer (Wave C5) + the reconciler both depend on
//                         IHandlerEnqueuer with no kill switch. If a future
//                         wave feature-gates a SPECIFIC handler, apply the
//                         Null-Object kill-switch pattern to THAT handler,
//                         never to the ServiceBusClient itself.
//   - ADR-036:            Reuse the IJobHandler infrastructure — L2 owns
//                         the enqueue path; BFF's ServiceBusJobProcessor
//                         drains + dispatches to IJobHandler impls per
//                         ApplicationProperties["JobType"] routing.
//
// PATTERN parity with BFF: mirrors JobProcessingModule's ServiceBusClient
// singleton registration + the JobSubmissionService's MessageId conventions.
// Deliberately does NOT reference the BFF assembly (project MUST rule — L2
// is a PEER service, not a BFF extension).
//
// SCOPE (task 038):
//   - ServiceBusModuleOptions bound from IConfiguration + fail-fast validation
//     on FullyQualifiedNamespace + QueueName (NFR-05).
//   - ServiceBusClient singleton — DefaultAzureCredential + FQN.
//   - IHandlerEnqueuer -> ServiceBusHandlerEnqueuer (Scoped).
//   - No receive-side wiring — the BFF's ServiceBusJobProcessor owns that
//     end.
//   - No queue provisioning — infrastructure/bicep/modules/service-bus.bicep
//     owns queue topology. Enqueue-side sets ApplicationProperties["JobType"]
//     so a future switch from a per-handler queue to a shared queue with
//     property-based routing (or vice-versa) does NOT require code changes.
//
// CO-EXISTENCE WITH platform-controlplane.bicep:
//   The bicep currently accepts a `serviceBusKeyVaultSecretName` parameter
//   (KV-referenced SB connection string) — that was authored before ADR-028
//   MI-outbound was extended to Service Bus for this project. L2 code
//   IGNORES that parameter and reads ServiceBus:FullyQualifiedNamespace
//   instead. A follow-on bicep tweak will add the FQN app-setting alongside
//   (bicep task, tracked separately) — this task's enqueue code intentionally
//   does not depend on that shipping first.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Sprk.Provisioning.ControlPlane.Enqueue;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for the L2 control-plane Service Bus client + the
/// <see cref="IHandlerEnqueuer"/> abstraction over the fleet-scoped queue
/// the BFF's <c>ServiceBusJobProcessor</c> drains.
/// </summary>
public static class ServiceBusModule
{
    /// <summary>Configuration section that carries FQN + queue name + TTLs.</summary>
    public const string ConfigSection = "ServiceBus";

    /// <summary>Default fleet-scoped queue name for L2-enqueued handler dispatches.</summary>
    public const string DefaultQueueName = "sprk-provisioning-jobs";

    /// <summary>
    /// Registers <see cref="ServiceBusModuleOptions"/>, <see cref="ServiceBusClient"/>,
    /// and <see cref="IHandlerEnqueuer"/> with the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound configuration (reads the <c>ServiceBus</c> section + optional <c>ManagedIdentity:ClientId</c>).</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup if <c>ServiceBus:FullyQualifiedNamespace</c> is missing or empty — NFR-05 fail-fast contract.
    /// </exception>
    public static IServiceCollection AddServiceBusModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail-fast on missing FQN (NFR-05). Queue name has a sensible default;
        // FQN never does — misconfiguration must not silently target localhost.
        var fqn = configuration[$"{ConfigSection}:FullyQualifiedNamespace"];
        if (string.IsNullOrWhiteSpace(fqn))
        {
            throw new InvalidOperationException(
                $"Configuration '{ConfigSection}:FullyQualifiedNamespace' is not set. " +
                "The L2 control-plane requires the fleet-scoped Service Bus namespace " +
                "(e.g. sb-spaarke-dev.servicebus.windows.net). In deployed environments " +
                "this is bound via App Service settings + Bicep; for local dev, copy " +
                "appsettings.template.json to appsettings.json and set the value.");
        }

        // Bind ServiceBus:{FullyQualifiedNamespace,QueueName,DefaultTimeToLive,HandlerTimeToLive}.
        services.Configure<ServiceBusModuleOptions>(configuration.GetSection(ConfigSection));

        // Nudge missing values into their defaults on top of the bind — the
        // Options pattern would leave them at their record defaults, but the
        // consumer expects non-empty QueueName + positive DefaultTimeToLive.
        services.PostConfigure<ServiceBusModuleOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.QueueName))
            {
                o.QueueName = DefaultQueueName;
            }
            if (o.DefaultTimeToLive <= TimeSpan.Zero)
            {
                o.DefaultTimeToLive = TimeSpan.FromMinutes(30);
            }
        });

        // ServiceBusClient — singleton per SDK guidance (thread-safe; owns
        // the AMQP connection pool; long-lived across the App Service lifetime).
        //
        // Credential: DefaultAzureCredential (RBAC data-plane) — matches the
        // task 030 grant of "Azure Service Bus Data Sender" to the L2 App
        // Service UAMI. NEVER account-key / connection-string per ADR-028
        // MI-outbound rule + POML acceptance criterion 5.
        //
        // Reuses the TokenCredential singleton registered by CosmosModule
        // when it is already present (single credential instance across
        // Cosmos + Service Bus -> token cache reuse). Falls back to
        // constructing a fresh DefaultAzureCredential when this module is
        // ever composed in isolation (e.g. an integration-test rig that
        // does not add CosmosModule).
        services.AddSingleton(sp =>
        {
            var credential = sp.GetService<TokenCredential>() ?? BuildDefaultCredential(configuration);
            return new ServiceBusClient(fqn, credential);
        });

        // Enqueuer — Scoped per ADR-010 default. The sender it owns is
        // created against the singleton ServiceBusClient; disposing the
        // scope disposes the sender via IAsyncDisposable.
        services.AddScoped<IHandlerEnqueuer, ServiceBusHandlerEnqueuer>();

        return services;
    }

    private static TokenCredential BuildDefaultCredential(IConfiguration configuration)
    {
        var miClientId = configuration["ManagedIdentity:ClientId"];
        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }
        return new DefaultAzureCredential(options);
    }
}

/// <summary>
/// Bound options for <see cref="ServiceBusModule"/>. Loaded from the
/// <c>ServiceBus</c> configuration section.
/// </summary>
public sealed class ServiceBusModuleOptions
{
    /// <summary>
    /// Fully-qualified Service Bus namespace, e.g. <c>sb-spaarke-dev.servicebus.windows.net</c>.
    /// Fail-fast validated by <see cref="ServiceBusModule.AddServiceBusModule"/>.
    /// </summary>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Fleet-scoped queue name — defaults to <c>sprk-provisioning-jobs</c>
    /// via <see cref="ServiceBusModule.DefaultQueueName"/>.
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Fallback TimeToLive when a handler-specific entry is not set.
    /// Defaults to 30 minutes — matches spec.md §4.2 handler tolerance.
    /// </summary>
    public TimeSpan DefaultTimeToLive { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Optional per-handler TTL overrides, keyed by <c>HandlerId</c>
    /// (e.g. <c>{ "H4": "01:00:00", "H8": "00:15:00" }</c>).
    /// </summary>
    public Dictionary<string, TimeSpan>? HandlerTimeToLive { get; set; }

    /// <summary>
    /// COMP-09 (customer-provisioning-orchestration-r1 Wave 7 completeness sweep,
    /// 2026-08-27) — per-message body cap enforced BEFORE
    /// <see cref="Azure.Messaging.ServiceBus.ServiceBusSender.SendMessageAsync(Azure.Messaging.ServiceBus.ServiceBusMessage, System.Threading.CancellationToken)"/>.
    /// The Service Bus Standard tier caps message size at 256 KB total
    /// (body + user + system properties). This option sets the BODY-ONLY
    /// ceiling — the enqueuer applies the cap to the serialized
    /// <see cref="Sprk.Provisioning.ControlPlane.Enqueue.HandlerEnvelope"/>
    /// JSON body BEFORE the AMQP framing overhead is added.
    ///
    /// <para>
    /// Default 229,376 bytes (224 KB) leaves ~32 KB of headroom for
    /// application-property + Service-Bus system-property overhead — the
    /// biggest observed application-property surface in this control plane
    /// is JobType + HandlerId + RunId + EnqueuedAt (&lt; 300 bytes total),
    /// so 32 KB is generous. Override via config only if the Service Bus
    /// namespace is Premium (1 MB per-message cap).
    /// </para>
    ///
    /// <para>
    /// Consequence-if-unfixed (from COMP-09 finding): H4b's BulkAppSettings
    /// envelope can balloon when a customer has many app-settings; a
    /// 300 KB envelope would silently 413 at enqueue time. Handler never
    /// dispatches; run silently stalls at WaitingOnGate-equivalent.
    /// </para>
    /// </summary>
    public int MaxEnvelopeBodyBytes { get; set; } = 224 * 1024;
}
