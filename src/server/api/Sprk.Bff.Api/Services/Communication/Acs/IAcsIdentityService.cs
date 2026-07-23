using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// The ACS identity plane (server-side only, FR-03). The BFF is the ACS "trusted service": it creates
/// ACS identities, persists the <c>communicationUserId ↔ Dataverse</c> mapping, and mints
/// <c>chat</c>-scoped tokens for participants. Clients hold NO ACS admin capability and receive NO token
/// in R1 (NFR-04); the BFF acts as the participant on ACS's behalf.
/// </summary>
/// <remarks>
/// This interface is a genuine seam (ADR-010): it is consumed by the downstream messaging transport
/// (thread/membership 011, sender 020, outbound send 051) and is mocked in unit tests. It mirrors the
/// ADR-045 channel-seam convention (<c>ICommunicationChannelSender</c> / <c>ICommunicationArchiver</c>).
/// Implementations MUST keep all <c>Azure.Communication.*</c> types inside <c>Services/Communication/</c>
/// (ADR-045) — no ACS type leaks to the API layer.
/// </remarks>
public interface IAcsIdentityService
{
    /// <summary>
    /// Guarantees the participant has an ACS identity. Returns the existing mapping if
    /// <c>sprk_communicationuserid</c> is already set (idempotent no-op); otherwise creates an ACS
    /// identity, persists the resulting <c>communicationUserId</c> on the participant's Dataverse record,
    /// and returns the new mapping.
    /// </summary>
    Task<AcsIdentityMapping> EnsureIdentityAsync(ParticipantReference participant, CancellationToken ct = default);

    /// <summary>
    /// Mints a <c>chat</c>-scoped ACS access token for the given <c>communicationUserId</c>. Validity is
    /// clamped to the ACS-supported 1–24h range; when <paramref name="validity"/> is null the configured
    /// default (24h) is used. Uniform for internal and (future) external identities — the Entra→ACS
    /// exchange is VoIP-scope only, so Chat is always server-minted here (design §8.2).
    /// </summary>
    Task<AcsChatToken> MintChatTokenAsync(string communicationUserId, TimeSpan? validity = null, CancellationToken ct = default);
}
