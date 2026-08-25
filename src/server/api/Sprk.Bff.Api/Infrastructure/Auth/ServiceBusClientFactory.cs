using Azure.Core;
using Azure.Messaging.ServiceBus;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Single place where the BFF decides how a <see cref="ServiceBusClient"/> authenticates:
/// namespace + managed identity (the target state) or a SAS connection string (transitional).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — <see cref="ManagedIdentityCredentialFactory"/> builds the
/// <see cref="TokenCredential"/>, but cannot help here: <c>ServiceBusClient</c> exposes two mutually
/// exclusive constructor overloads (connection string vs <c>fullyQualifiedNamespace</c> +
/// <see cref="TokenCredential"/>), so the choice must be made where the client is built.
/// (2) <i>Extension</i> — no natural existing host: the call sites live in three separate DI modules
/// and one hosted service, sharing no base type. Mirrors the sibling
/// <see cref="SearchClientFactory"/> (auth-v4 task 053) so the platform has one shape for
/// "credential decision at client construction".
/// (3) <i>Cost-of-doing-nothing</i> — the SAS string was resolved from <b>two different config
/// keys</b> across <b>three duplicate registrations</b> (see the census below). That is the exact
/// fan-out that made <c>BFF-API-ClientSecret</c> unfixable in one place (ADR-028 A4), and a partial
/// migration would leave some queues on SAS while the project reported the key retired.
/// </para>
/// <para>
/// <b>The census this replaces</b> (auth-v4 task 051, 2026-08-23). Three singleton registrations of
/// <see cref="ServiceBusClient"/> existed, and .NET DI resolves <i>last registration wins</i>:
/// <list type="number">
///   <item><c>WorkersModule</c> (Program.cs:75) — from <c>ServiceBus:ConnectionString</c>, gated on
///   the string being non-empty.</item>
///   <item><c>OfficeWorkersModule.AddOfficeServiceBus</c> (Program.cs:124) — from
///   <c>ServiceBusOptions.ConnectionString</c>.</item>
///   <item><c>JobProcessingModule</c> (Program.cs:196) — from <c>ConnectionStrings:ServiceBus</c>.
///   <b>This one won</b>; the first two were shadowed and never resolved by anything.</item>
/// </list>
/// Both keys were live in the estate simultaneously — the Bicep stacks set
/// <c>ConnectionStrings__ServiceBus</c> while <c>scripts/Configure-ProductionAppSettings.ps1</c> set
/// <c>ServiceBus__ConnectionString</c>. Registrations 1 and 2 are now deleted; this factory is the
/// only construction path.
/// </para>
/// <para>
/// <b>Selection rule.</b> <see cref="ServiceBusOptions.FullyQualifiedNamespace"/> set → namespace +
/// injected <see cref="TokenCredential"/>. Otherwise the connection string. Namespace wins when both
/// are present, so an environment can cut over to managed identity <b>while the SAS string is still
/// in place as a rollback</b> (NFR-06: rollback is config-only at every phase). Removing the
/// namespace setting reverts to SAS with no redeploy.
/// </para>
/// <para>
/// <b>Why the DI-injected credential and not an inline one.</b> The task 051 constraint is explicit:
/// <c>MembershipJunctionUpdaterHost.cs:120</c> constructs <c>new DefaultAzureCredential()</c> inline,
/// and that is a deviation not to propagate. Inline <see cref="Azure.Identity.DefaultAzureCredential"/>
/// resolves the wrong identity when several UAMIs are attached — five exist in the dev subscription,
/// and one (<c>spaarke-bff-identity</c>) is named like the BFF's but is not attached to it. The
/// central singleton is pinned to the UAMI ClientId (Program.cs:46 →
/// <see cref="ManagedIdentityCredentialFactory"/>).
/// </para>
/// <para>
/// <b>RBAC prerequisite.</b> The namespace path needs <c>Azure Service Bus Data Sender</c> and
/// <c>Azure Service Bus Data Receiver</c> at namespace scope. The dev UAMI held them only at
/// topic scope (<c>sprk-membership-changes</c>) until 2026-08-23, when task 051 granted both at
/// namespace scope — a topic-scoped grant cannot read <c>sdap-jobs</c>. Owner/Contributor do
/// <b>not</b> imply either; they are dataActions.
/// </para>
/// </remarks>
public static class ServiceBusClientFactory
{
    /// <summary>
    /// True when the namespace + managed-identity path should be used.
    /// </summary>
    public static bool UseManagedIdentity(ServiceBusOptions options)
        => !string.IsNullOrWhiteSpace(options?.FullyQualifiedNamespace);

    /// <summary>
    /// Builds the <see cref="ServiceBusClient"/> using whichever credential is configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither a namespace nor a connection string is configured. Thrown loudly rather than
    /// returning a client that cannot connect: a background job processor that silently fails to
    /// start looks identical to an empty queue, and jobs would be accepted and never run.
    /// </exception>
    public static ServiceBusClient Create(ServiceBusOptions options, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (UseManagedIdentity(options))
        {
            ArgumentNullException.ThrowIfNull(credential);
            return new ServiceBusClient(options.FullyQualifiedNamespace, credential);
        }

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new ServiceBusClient(options.ConnectionString);
        }

        throw new InvalidOperationException(
            "Service Bus is not configured. Set 'ServiceBus:FullyQualifiedNamespace' to the namespace " +
            "FQDN (e.g. spaarke-servicebus-dev.servicebus.windows.net) to authenticate with the managed " +
            "identity — this is the supported path and requires the 'Azure Service Bus Data Sender' " +
            "and 'Azure Service Bus Data Receiver' roles at namespace scope. " +
            "Alternatively set 'ServiceBus:ConnectionString' (or the legacy 'ConnectionStrings:ServiceBus') " +
            "to a SAS connection string. Background job processing cannot start without one of these.");
    }

    /// <summary>
    /// Builds a namespace + managed-identity <see cref="ServiceBusClient"/> for callers that carry
    /// their own namespace setting rather than <see cref="ServiceBusOptions"/>.
    /// </summary>
    /// <remarks>
    /// Exists for <c>MembershipJunctionUpdaterHost</c>, whose namespace lives in
    /// <c>Membership:JunctionUpdater:ServiceBusNamespace</c>. Routing it here rather than letting it
    /// keep its own <c>new ServiceBusClient(...)</c> is what allows
    /// <c>ServiceBusClientGuardTests</c> to assert a single construction site — an allowlist with
    /// one entry is a census waiting to regrow, which is the failure mode this project exists to
    /// stop (ADR-028 A4).
    /// </remarks>
    /// <param name="fullyQualifiedNamespace">Namespace FQDN.</param>
    /// <param name="credential">
    /// The DI-injected credential. Never construct one inline: five user-assigned identities exist
    /// in the dev subscription and <c>DefaultAzureCredential</c> cannot tell which to use — one is
    /// named like the BFF's but is not attached to it. The central singleton is pinned by ClientId.
    /// </param>
    public static ServiceBusClient CreateForNamespace(
        string fullyQualifiedNamespace,
        TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                "A fully-qualified Service Bus namespace is required (e.g. " +
                "spaarke-servicebus-dev.servicebus.windows.net).");
        }

        ArgumentNullException.ThrowIfNull(credential);
        return new ServiceBusClient(fullyQualifiedNamespace, credential);
    }
}
