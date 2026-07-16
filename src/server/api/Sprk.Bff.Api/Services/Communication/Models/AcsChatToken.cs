namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// A server-minted, <c>chat</c>-scoped ACS access token for one participant (FR-03). Produced by
/// <see cref="Acs.IAcsIdentityService.MintChatTokenAsync"/> with 1–24h validity (default 24h).
/// </summary>
/// <remarks>
/// <para><b>NFR-04 (binding):</b> this token MUST NOT reach any client in R1. The BFF holds the minted
/// token and acts as the participant on ACS's behalf — there is no client-side ACS SDK and no browser
/// token path in R1 (the live-client browser-token upgrade is the next project). This model is therefore
/// server-internal and MUST NOT be surfaced on any client-facing DTO or endpoint response.</para>
/// <para><see cref="ExpiresOn"/> + the caller-supplied validity give a future live client a refresh-callback
/// shape (re-issue before expiry) without exposing the token today.</para>
/// </remarks>
public sealed record AcsChatToken
{
    /// <summary>The ACS <c>communicationUserId</c> (MRI) this token was minted for.</summary>
    public required string CommunicationUserId { get; init; }

    /// <summary>The opaque ACS access token string (server-held; never sent to a client in R1).</summary>
    public required string Token { get; init; }

    /// <summary>Absolute expiry of the token — drives the (future) refresh callback.</summary>
    public required DateTimeOffset ExpiresOn { get; init; }
}
