namespace Spaarke.Dataverse;

/// <summary>
/// Supplies a signed <b>client assertion</b> for MSAL confidential clients that act as the BFF
/// identity — the secret-free credential ADR-028 Amendment <b>A4</b> requires.
///
/// <para><b>Why this contract lives in <c>Spaarke.Dataverse</c> rather than beside its
/// implementation.</b> <c>Spaarke.Dataverse</c> is the BASE layer: it references no other Spaarke
/// project, and that is CI-enforced by <c>tests/Spaarke.ArchTests/LayerDependencyTests.cs</c> (FR-14).
/// It therefore cannot reference <c>Sprk.Bff.Api</c> (wrong direction) and cannot reference
/// <c>Spaarke.Core</c> (circular — <c>Spaarke.Core</c> already references this project). Shared-library
/// types nonetheless need a credential the BFF owns. <b>Dependency inversion is the only legal
/// seam</b>: the contract is declared here, the implementation lives in the BFF, and the BFF injects
/// it downward. Placing it anywhere else fails the build.</para>
///
/// <para><b>Deliberately narrow.</b> One method, primitive in and primitive out. In particular it does
/// NOT expose MSAL's <c>AssertionRequestOptions</c>, even though this assembly does reference
/// <c>Microsoft.Identity.Client</c> and could. Keeping MSAL types out of the contract means an
/// implementation is free to mint assertions by any mechanism without dragging MSAL into the base
/// layer. Consumers adapt at the call site:
/// <c>.WithClientAssertion(opts =&gt; provider.GetAssertionAsync(opts.CancellationToken))</c>.</para>
///
/// <para><b>Honest limit of that narrowness (task 020 code review, Q4).</b> An earlier version claimed a
/// Key Vault <b>certificate</b> implementation — ADR-028 A4's sanctioned alternative where MI-FIC's
/// same-tenant rule cannot hold — would need no contract change. <b>That is not safe as written.</b> A
/// certificate assertion is a self-signed JWT whose <c>aud</c> must be the token endpoint and whose
/// <c>iss</c>/<c>sub</c> must be the client id — precisely the <c>TokenEndpoint</c> and <c>ClientID</c>
/// this contract drops, and precisely why Identity.Web's own <c>ClientAssertionProviderBase</c> takes
/// <c>AssertionRequestOptions</c>. A certificate provider behind this contract would have to re-derive
/// both from configuration and would inherit options-blind caching, which for a certificate is a
/// correctness bug rather than a performance one.
///
/// This is <b>not</b> a problem today: MI-FIC is the adopted credential, A4 records that the certificate
/// path was explicitly not taken, and no deployment shape currently requires it. The widening, if ever
/// needed, is a Spaarke-owned request record (<c>ClientId</c>, <c>TokenEndpoint</c>, <c>TenantId</c> —
/// still no MSAL types, so the base layer stays clean). Task 021 owns that decision alongside the
/// client-level seam it authors. Recorded so the next author meets it here rather than under deadline.</para>
///
/// <para><b>Implementations MUST cache.</b> A signed assertion is valid until expiry, so implementations
/// reuse it rather than re-minting per call — see <c>ManagedIdentityAssertionProvider</c>, which holds
/// one <c>ManagedIdentityClientAssertion</c> for the process. Callers may therefore invoke this freely
/// without rate-limiting it themselves. (Precision, task 020 code review W-4: for the managed-identity
/// implementation the underlying token cache is process-static and keyed by identity, so this is about
/// avoiding redundant work rather than avoiding IMDS traffic. A certificate-backed implementation, which
/// signs locally, would have different costs again.)</para>
///
/// <para><b>Failure is expected and must be catchable, not fatal.</b> On a developer workstation there
/// is no IMDS endpoint, so the managed-identity implementation throws
/// <c>MsalServiceException("managed_identity_unreachable_network")</c> at <i>first call</i> — never at
/// construction. That distinction is load-bearing: it is what lets task 021 build an ordered credential
/// selection that falls through to the next option, and it is why implementations MUST NOT probe the
/// identity endpoint in their constructor.</para>
///
/// <para>Introduced by <c>spaarke-auth-v4-dataverse-MI</c> task 020 (FR-B1).</para>
/// </summary>
public interface IClientAssertionProvider
{
    /// <summary>
    /// Returns a signed client assertion (a JWT) proving this application's identity to Entra ID,
    /// for use as the <c>client_assertion</c> parameter of an OAuth token request.
    /// </summary>
    /// <param name="cancellationToken">Cancels the mint if it has to go to the network.</param>
    /// <returns>The signed assertion. Cached by the implementation until expiry.</returns>
    /// <exception cref="Microsoft.Identity.Client.MsalServiceException">
    /// The assertion could not be minted — no managed identity is reachable
    /// (<c>managed_identity_unreachable_network</c>), or the configured identity does not exist on
    /// this resource (<c>managed_identity_request_failed</c>). Both are catchable and distinguishable
    /// by <c>ErrorCode</c>, which is what an ordered credential selector needs in order to fall
    /// through rather than fail the request.
    /// </exception>
    Task<string> GetAssertionAsync(CancellationToken cancellationToken = default);
}
