using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the ACS (Azure Communication Services) identity plane
/// (messaging-communication-app-r1 task 010 / FR-03). Bound from the
/// <c>Communication:Acs</c> section.
/// </summary>
/// <remarks>
/// <para><b>ADR-028 / NFR-05:</b> only the ACS <see cref="Endpoint"/> is configured here. Authentication
/// uses the DI-injected central <c>TokenCredential</c> (Managed Identity) — there is deliberately NO
/// connection-string / access-key setting, so no inline ACS credential can be constructed.</para>
/// <para>No live ACS resource is provisioned yet; <see cref="Endpoint"/> is populated per-environment once
/// provisioning (task 012) lands. Absent an endpoint, the ACS identity plane is registered but its client
/// is not constructed until first use.</para>
/// </remarks>
public sealed class AcsOptions
{
    public const string SectionName = "Communication:Acs";

    /// <summary>
    /// ACS resource endpoint, e.g. <c>https://{resource}.communication.azure.com</c>. Environment-specific;
    /// resolved from provisioning (task 012). The central <c>TokenCredential</c> authenticates against it.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Default validity (hours) for a minted <c>chat</c>-scoped token when a caller does not specify one.
    /// Clamped to the ACS-supported 1–24h range; defaults to 24h.
    /// </summary>
    [Range(1, 24)]
    public int DefaultTokenValidityHours { get; set; } = 24;
}
