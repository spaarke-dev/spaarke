using Microsoft.Identity.Client;

namespace Spaarke.Dataverse;

/// <summary>
/// Supplies a fully-configured MSAL <see cref="IConfidentialClientApplication"/> for the BFF's own
/// identity, with the credential already selected from the configured ordered list — MI-FIC, then a
/// Key Vault certificate, then the transitional client secret (ADR-028 Amendment <b>A4</b>).
///
/// <para><b>Why a SECOND contract exists alongside <see cref="IClientAssertionProvider"/>.</b> Ordered
/// credential selection cannot be expressed behind the assertion contract, and this is a fact about the
/// credentials rather than a preference about types. <see cref="IClientAssertionProvider"/> returns
/// <c>Task&lt;string&gt;</c> — a signed assertion — but of the three sanctioned credentials <b>only
/// MI-FIC is an assertion</b>. A Key Vault certificate is bound with <c>.WithCertificate(x509)</c> and a
/// secret with <c>.WithClientSecret(...)</c>; neither has an assertion to hand back. Widening the
/// assertion contract to cover them would produce a type whose name is a lie and whose two other
/// implementations would have to return something they do not have. The selection therefore has to
/// happen one level up, where the credential is actually bound: at <c>Build()</c>. Recorded at task 020
/// (finding V1) and authored at task 021.</para>
///
/// <para><b>Why it lives in <c>Spaarke.Dataverse</c>.</b> Same reason as
/// <see cref="IClientAssertionProvider"/>: this is the BASE layer, CI-enforced to reference no other
/// Spaarke project (<c>tests/Spaarke.ArchTests/LayerDependencyTests.cs</c>, FR-14), so a shared-library
/// type can only receive a BFF-owned credential by dependency inversion. An MSAL type in the signature
/// is legal here and adds <b>no</b> package reference — <c>Spaarke.Dataverse.csproj</c> already carries
/// <c>Microsoft.Identity.Client</c> 4.87.0 for its own confidential clients. Verified at task 021, not
/// assumed: FR-14 is unaffected because FR-14 constrains <c>ProjectReference</c>, and no new
/// <c>PackageReference</c> is introduced either.</para>
///
/// <para><b>Only ONE consumer needs this interface.</b> <c>DataverseAccessDataSource</c> lives in this
/// assembly and cannot see the implementation. The three BFF-side consumers —
/// <c>GraphClientFactory</c>, <c>DataverseUserClient</c>, <c>AgentTokenService</c> — can inject the
/// implementation <b>concretely</b>, which ADR-010 prefers. Do not add the interface to call sites that
/// do not need it.</para>
///
/// <para><b>Why this is async, and what it costs the consumers.</b> Selection has to <i>prove</i> a
/// credential before binding it, and the proof is I/O: minting a managed-identity assertion, or
/// fetching a certificate from Key Vault. It cannot be deferred to first token acquisition, because by
/// then the credential is already bound into the built client and MSAL surfaces the failure as a failed
/// <i>token request</i> — far too late to fall through. Consequence for task 022: the four call sites
/// currently build their confidential client in a <b>constructor</b>, which cannot await. They must move
/// to lazy first-use construction. <c>CiamGraphClientFactory.GetOrCreateAppAsync</c> — a
/// <c>SemaphoreSlim</c> guarding a one-time build — is the in-repo precedent to copy.</para>
///
/// <para><b>Note this is NOT the banned availability probe.</b> Task 021's constraints forbid adding an
/// <c>IsAvailable</c> member, on the grounds that a probe is a second network call racing the real one.
/// This is not that: the MI-FIC proof calls <see cref="IClientAssertionProvider.GetAssertionAsync"/>
/// once, and <c>ManagedIdentityClientAssertion</c> caches the signed assertion until expiry, so the very
/// same assertion is what MSAL's callback then returns. It is the <i>first</i> call, not a duplicate of
/// one.</para>
///
/// <para>Introduced by <c>spaarke-auth-v4-dataverse-MI</c> task 021 (FR-B2).</para>
/// </summary>
public interface IConfidentialClientProvider
{
    /// <summary>
    /// Returns a confidential client for <paramref name="clientId"/> in <paramref name="tenantId"/>,
    /// built with the highest-priority configured credential that could actually be obtained.
    /// Implementations cache the built client, so callers may invoke this per use rather than holding
    /// the result — but see the remarks on why the call is async.
    /// </summary>
    /// <param name="tenantId">Directory (tenant) id the client authenticates against.</param>
    /// <param name="clientId">Application (client) id of the app registration the BFF acts as.</param>
    /// <param name="ct">Cancels credential acquisition if it has to go to the network.</param>
    /// <returns>A built, credential-bound confidential client.</returns>
    /// <exception cref="InvalidOperationException">
    /// Every configured credential was exhausted without one becoming available. This is deliberately
    /// fatal to the request rather than silently degrading: an OBO path with no credential cannot
    /// authenticate anyone, so failing here is the fail-closed outcome (NFR-03).
    /// </exception>
    /// <exception cref="MsalServiceException">
    /// A credential failed in a way that is NOT a fall-through condition — most importantly
    /// <c>managed_identity_request_failed</c>, which means IMDS was reachable but the configured
    /// identity was absent or wrong (the FR-B4 signature). Falling through on that would run production
    /// on the transitional secret while every health signal looked green, so it is rethrown.
    /// </exception>
    Task<IConfidentialClientApplication> GetClientAsync(
        string tenantId,
        string clientId,
        CancellationToken ct = default);
}
