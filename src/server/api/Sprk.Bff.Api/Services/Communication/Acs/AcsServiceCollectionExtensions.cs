using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// DI wiring for the ACS identity plane (FR-03 / task 010). Called by
/// <c>CommunicationModule.AddCommunicationModule</c> so the ACS registration lives with the ACS code
/// but is composed through the one Communication feature module (ADR-010). Kept as its own extension so
/// the wiring is unit-testable in isolation (verifying the client is built from the injected central
/// <see cref="TokenCredential"/>, not a <c>new</c>'d credential).
/// </summary>
public static class AcsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ACS identity plane UNCONDITIONALLY (ADR-010 / ADR-032): the identity service +
    /// its <see cref="CommunicationIdentityClient"/> are consumed by the messaging transport
    /// (011/020/051) with no feature gate. The client is built via a lazy factory from the DI-injected
    /// central <see cref="TokenCredential"/> (ADR-028 / NFR-05) + the configured ACS endpoint — the
    /// factory is only invoked on first resolution, so no live ACS resource is required at startup.
    /// No consumer maps the client eagerly at startup, so no Null-Object peer is required yet (when
    /// 011/020/051 add endpoints that consume it, apply ADR-032 if the plane becomes feature-gated).
    /// </summary>
    public static IServiceCollection AddAcsIdentityPlane(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AcsOptions>(configuration.GetSection(AcsOptions.SectionName));

        // ACS admin client — built from the injected central TokenCredential (ADR-028 / NFR-05).
        // MUST NOT construct a credential inline or use an ACS connection-string key.
        services.AddSingleton(sp =>
        {
            var endpoint = sp.GetRequiredService<IOptions<AcsOptions>>().Value.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException(
                    "Communication:Acs:Endpoint must be configured to use the ACS identity plane " +
                    "(no live ACS resource is provisioned yet — see task 012).");
            }

            var credential = sp.GetRequiredService<TokenCredential>();
            return new CommunicationIdentityClient(new Uri(endpoint), credential);
        });

        services.AddSingleton<IAcsIdentityService, AcsIdentityService>();

        return services;
    }

    /// <summary>
    /// Registers the ACS thread + membership plane UNCONDITIONALLY (ADR-010 / ADR-032 — consumed by the
    /// messaging channel sender (020), the membership-reconcile job (041), and outbound send (051) with no
    /// feature gate). Depends on the <see cref="CommunicationIdentityClient"/> + <see cref="AcsOptions"/>
    /// registered by <see cref="AddAcsIdentityPlane"/>, so call this AFTER it.
    /// <para>
    /// The ACS Chat data plane has no app-only path, so the <see cref="ChatClient"/> is authenticated with a
    /// server-minted <c>chat</c> token for a BFF service identity, rooted in the DI-injected central
    /// <see cref="TokenCredential"/> via the identity client (ADR-028 / NFR-05 — no inline credential, no
    /// connection-string key). The client is built by a lazy factory (invoked on first resolution) whose
    /// credential defers all live ACS calls to the first thread operation — so no live ACS resource is
    /// required at startup (none is provisioned until task 012).
    /// </para>
    /// </summary>
    public static IServiceCollection AddAcsThreadPlane(this IServiceCollection services)
    {
        // The ACS ChatClient — authenticated as the BFF service identity via a token rooted in the injected
        // central TokenCredential (through the identity client). Lazy: constructing the credential makes no
        // live call; the service identity + token are minted on the first ACS operation.
        services.AddSingleton(sp =>
        {
            var endpoint = sp.GetRequiredService<IOptions<AcsOptions>>().Value.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException(
                    "Communication:Acs:Endpoint must be configured to use the ACS thread plane " +
                    "(no live ACS resource is provisioned yet — see task 012).");
            }

            var identityClient = sp.GetRequiredService<CommunicationIdentityClient>();
            var credentialLogger = sp.GetRequiredService<ILogger<AcsServiceChatCredential>>();
            var serviceCredential = new AcsServiceChatCredential(identityClient, credentialLogger);

            return new ChatClient(new Uri(endpoint), serviceCredential.CreateCredential());
        });

        services.AddSingleton<IAcsThreadService, AcsThreadService>();

        return services;
    }
}
