using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Auth;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Startup guard for the <b>two different identities</b> MI-FIC requires the BFF to hold at once
/// (spec FR-B4, auth-v4 task 023).
///
/// <para><b>The hazard, stated plainly.</b> A managed-identity federated credential involves two
/// identities that are easy to confuse and catastrophic to swap. The <b>user-assigned managed
/// identity</b> mints the assertion; the <b>app registration</b> is what that assertion authenticates
/// as. Configure the app registration's clientId where the UAMI's belongs and the credential is created
/// cleanly, deploys cleanly, and fails only at token exchange. The dev subscription makes this sharper:
/// it holds five UAMIs, one named <c>spaarke-bff-identity</c> as though it were the BFF's without being
/// attached to it.</para>
///
/// <para><b>Why a startup validator rather than a check inside the provider.</b> Task 020 established —
/// and task 021's ordered selection depends on — that <c>ManagedIdentityAssertionProvider</c> must
/// <i>never</i> fail at construction: a workstation has no IMDS endpoint, and a constructor that probed
/// it would take down local development and destroy the fall-through. That property is about
/// <b>availability</b>. The rules below are about <b>configuration coherence</b>, which is knowable
/// without a network and therefore belongs at startup, where a misconfiguration is loud instead of
/// latent.</para>
///
/// <para><b>This validator also warns.</b> Unusual for <see cref="IValidateOptions{T}"/>, and
/// deliberate: rule 2 below describes a real defect that is currently <i>inert</i> in every Spaarke
/// environment. Failing startup on it would take dev down to fix a bug that is not firing — the exact
/// shape of the <c>#3b</c> incident. Reporting it at error level puts it in front of an operator
/// without that cost.</para>
/// </summary>
public sealed class IdentityConfigurationValidator : IValidateOptions<CredentialSelectionOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityConfigurationValidator> _logger;

    public IdentityConfigurationValidator(
        IConfiguration configuration,
        ILogger<IdentityConfigurationValidator> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValidateOptionsResult Validate(string? name, CredentialSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        var assertionIdentity = ManagedIdentityCredentialFactory.ResolveUamiClientId(_configuration);
        var appRegistration = FirstNonBlank(
            _configuration["AzureAd:ClientId"],
            _configuration["API_APP_ID"]);
        var azureClientId = _configuration["AZURE_CLIENT_ID"];

        var order = options.Order
            .Select(raw => Enum.TryParse<CredentialKind>(raw?.Trim(), ignoreCase: true, out var k) ? (CredentialKind?)k : null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToList();

        // ── Rule 1 ─────────────────────────────────────────────────────────────────────────────────
        // The two identities are the same value. This cannot be intentional: an app registration
        // cannot mint its own managed-identity assertion, so the credential could never work. Fatal,
        // and safe to make fatal — no Spaarke environment is in this state (verified against
        // spaarke-bff-dev, 2026-08-21).
        if (!string.IsNullOrWhiteSpace(assertionIdentity)
            && !string.IsNullOrWhiteSpace(appRegistration)
            && string.Equals(assertionIdentity, appRegistration, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"The managed-identity clientId and the app-registration clientId are both "
                + $"'{assertionIdentity}'. These are two DIFFERENT identities: the user-assigned managed "
                + "identity MINTS the client assertion, and the app registration is what that assertion "
                + "authenticates AS. Set Graph:ManagedIdentity:ClientId to the UAMI's clientId and "
                + "AzureAd:ClientId (or API_APP_ID) to the app registration's.");
        }

        // ── Rule 2 ─────────────────────────────────────────────────────────────────────────────────
        // AZURE_CLIENT_ID is ambiguous by convention: the Azure SDK reads it as a MANAGED IDENTITY's
        // clientId, while GraphClientFactory.cs:54 reads it as the APP REGISTRATION's
        // (AZURE_CLIENT_ID ?? API_APP_ID). On spaarke-bff-dev it currently holds the UAMI's clientId,
        // so that resolution yields a managed identity where an app registration is required.
        //
        // It is inert TODAY only because Graph:ManagedIdentity:Enabled=true makes the branch that
        // consumes it dead code. Disable the flag — plausible during an incident — and the BFF builds a
        // ClientSecretCredential from a managed identity's clientId paired with the app registration's
        // secret, which fails with an opaque AADSTS error naming neither.
        //
        // So: fatal exactly when it would fire, reported loudly when it would not. Task 023's
        // constraints put changing GraphClientFactory's fallback semantics out of scope — this guards
        // and surfaces it without touching them.
        var managedIdentityEnabled = bool.TryParse(
            _configuration["Graph:ManagedIdentity:Enabled"], out var miEnabled) && miEnabled;

        var azureClientIdIsAManagedIdentity =
            !string.IsNullOrWhiteSpace(azureClientId)
            && !string.IsNullOrWhiteSpace(assertionIdentity)
            && string.Equals(azureClientId, assertionIdentity, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(appRegistration)
            && !string.Equals(azureClientId, appRegistration, StringComparison.OrdinalIgnoreCase);

        if (azureClientIdIsAManagedIdentity)
        {
            if (!managedIdentityEnabled)
            {
                failures.Add(
                    $"AZURE_CLIENT_ID is set to '{azureClientId}', which is the MANAGED IDENTITY's "
                    + $"clientId, but the app registration is '{appRegistration}'. With "
                    + "Graph:ManagedIdentity:Enabled false, GraphClientFactory resolves the app-only "
                    + "clientId as AZURE_CLIENT_ID ?? API_APP_ID and would build a ClientSecretCredential "
                    + "from a managed identity. Either set Graph:ManagedIdentity:Enabled=true, or clear "
                    + "AZURE_CLIENT_ID so API_APP_ID is used.");
            }
            else
            {
                _logger.LogError(
                    "IDENTITY CONFLATION HAZARD (FR-B4), currently inert: AZURE_CLIENT_ID is the "
                    + "MANAGED IDENTITY clientId {UamiClientId}, while the app registration is "
                    + "{AppRegistrationClientId}. GraphClientFactory reads AZURE_CLIENT_ID as the "
                    + "APP-ONLY clientId, so disabling Graph:ManagedIdentity:Enabled would make it "
                    + "build a ClientSecretCredential from a managed identity and fail with an opaque "
                    + "AADSTS error. Harmless while managed identity stays enabled. Clear "
                    + "AZURE_CLIENT_ID to remove the trap.",
                    azureClientId, appRegistration);
            }
        }

        // ── Rule 3 ─────────────────────────────────────────────────────────────────────────────────
        // MI-FIC configured with NO fallback beneath it and no identity to mint with. Scoped to the
        // no-fallback case on purpose, and the scoping is load-bearing rather than timid: with a
        // fallback present, an absent managed identity is a DESIGNED fall-through condition (task 021)
        // and the ordinary shape of every developer workstation and test fixture in this repo — none of
        // which set Graph:ManagedIdentity:ClientId. Failing startup on it would break all of them to
        // guard a case that is not the hazard.
        //
        // With no fallback there is nothing to fall through TO, so the process can only fail at first
        // token exchange — which on the OBO path means every user, at once, with no startup signal.
        // That is the case FR-B4 is about, and it is also this project's END STATE: once task 033
        // removes ClientSecret from the order, this rule automatically becomes strict.
        if (order.Count == 1
            && order[0] == CredentialKind.ManagedIdentityFederated
            && string.IsNullOrWhiteSpace(assertionIdentity))
        {
            failures.Add(
                $"{CredentialKind.ManagedIdentityFederated} is the only configured credential, but no "
                + "user-assigned managed identity is set (Graph:ManagedIdentity:ClientId and "
                + "ManagedIdentity:ClientId are both empty). There is no credential to fall through to, "
                + "so this would fail at the first token exchange rather than here. Set the UAMI's "
                + "clientId, or add a fallback credential to Graph:Credentials:Order.");
        }

        // ── Rule 4 (auth-v4 task 024, FR-B5) ───────────────────────────────────────────────────────
        // NO credential of any kind can be obtained. This is the backstop that lets task 024 relax the
        // three [Required] attributes safely: those mandated a SECRET specifically, which is why a
        // secret-free deployment was impossible; this asserts the weaker, correct thing — that SOME
        // credential is configured — and it does so at startup rather than at the first token exchange.
        //
        // "Definitely unavailable" is judged conservatively, and the conservatism is the point:
        //   • ClientSecret          — no secret in any canonical key ⇒ definitely unavailable.
        //   • KeyVaultCertificate   — no certificate name ⇒ definitely unavailable.
        //   • ManagedIdentityFederated — NEVER definitely unavailable. An unset UAMI clientId means
        //     system-assigned, which is a legitimate shape this validator cannot rule out without a
        //     network call. Rule 3 above already covers the one case where an unset identity is
        //     unambiguously fatal (MI-FIC with nothing beneath it to fall through to).
        // So this fires only when every configured credential is provably absent — e.g. an order of
        // [ClientSecret] with no secret anywhere. It cannot produce a false positive that would break a
        // developer workstation or a fixture, which is the AP-7 constraint every fail-fast change in
        // this project has to satisfy.
        var anyPossiblyAvailable = order.Count == 0 || order.Any(kind => kind switch
        {
            CredentialKind.ManagedIdentityFederated => true,
            CredentialKind.KeyVaultCertificate => !string.IsNullOrWhiteSpace(options.KeyVaultCertificateName),
            CredentialKind.ClientSecret => !string.IsNullOrWhiteSpace(FirstNonBlank(
                _configuration["AzureAd:ClientSecret"],
                _configuration["API_CLIENT_SECRET"],
                _configuration["AZURE_CLIENT_SECRET"])),
            _ => false,
        });

        if (!anyPossiblyAvailable)
        {
            failures.Add(
                $"None of the configured credentials ({string.Join(", ", order)}) can be obtained: no "
                + "client secret is set (AzureAd:ClientSecret, API_CLIENT_SECRET, AZURE_CLIENT_SECRET) "
                + "and no Key Vault certificate is named. The BFF cannot authenticate as itself, so "
                + "every OBO exchange would fail closed. Configure a credential, or add "
                + $"{CredentialKind.ManagedIdentityFederated} to {CredentialSelectionOptions.SectionName}:Order.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
