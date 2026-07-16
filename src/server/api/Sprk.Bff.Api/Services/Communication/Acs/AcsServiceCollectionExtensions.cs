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
}
