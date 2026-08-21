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

        // ── AMENDED at task 022: the trap was REMOVED, so this rule is no longer fatal ─────────────
        // Task 023 could only guard the hazard because changing GraphClientFactory's app-only branch
        // was out of its scope. Task 022 owns that branch and deleted the `AZURE_CLIENT_ID ?? API_APP_ID`
        // fallback outright, so AZURE_CLIENT_ID now has ZERO consumers anywhere in src/ and cannot be
        // mistaken for an app registration by any code path.
        //
        // The rule is deliberately NOT deleted with the trap. The setting is still a genuine
        // misconfiguration signal — it says an operator (or a script) believed this key meant something
        // it does not — and reporting it is what gets it cleared at task 031. But it is now reported at
        // error level in BOTH branches rather than failing startup, because failing startup over a
        // setting that nothing reads is a false positive, and this project's own AP-7 rule forbids
        // converting an inert condition into an outage.
        if (azureClientIdIsAManagedIdentity)
        {
            _logger.LogError(
                "IDENTITY CONFLATION SIGNAL (FR-B4), now INERT: AZURE_CLIENT_ID holds the MANAGED "
                + "IDENTITY clientId {UamiClientId}, while the app registration is "
                + "{AppRegistrationClientId} (Graph:ManagedIdentity:Enabled={MiEnabled}). Since auth-v4 "
                + "task 022 no code reads AZURE_CLIENT_ID, so nothing can conflate the two identities "
                + "any more — but the setting is wrong for what it appears to mean and should be "
                + "cleared (task 031). Before task 022, disabling managed identity in this state built "
                + "a client credential from a managed identity and failed with an opaque AADSTS error.",
                azureClientId, appRegistration, managedIdentityEnabled);
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

        // ── Rule 5 (auth-v4 task 022, FR-B3) ───────────────────────────────────────────────────────
        // AgentToken:ClientSecret vs the transitional secret the provider actually resolves.
        //
        // Before task 022, AgentTokenService built its own confidential client from
        // AgentToken:ClientSecret specifically. It now takes the client from the ordered provider,
        // which resolves the transitional secret as AzureAd:ClientSecret → API_CLIENT_SECRET →
        // AZURE_CLIENT_SECRET — deliberately NOT reading the options-bound AgentToken key. Task 021
        // excluded it precisely because folding it in silently could change which secret the agent path
        // presents; task 022's constraint required the question be settled where the change is
        // observable rather than defaulted.
        //
        // It was settled by measurement: on spaarke-bff-dev (2026-08-21) AgentToken__ClientSecret,
        // API_CLIENT_SECRET, AzureAd__ClientSecret and Dataverse__ClientSecret all hold the same value
        // — BFF-API-ClientSecret — and AgentToken__ClientId is the BFF app registration.
        // Reconcile-DemoEnvironment.ps1:76 wires the demo environment identically.
        //
        // This rule exists because "identical today" is not "identical forever". If they ever diverge,
        // the agent OBO would present a different secret than before the migration and fail with
        // AADSTS7000215 — an error that says nothing about which of two settings is stale. Compared by
        // FINGERPRINT, never by value, and reported rather than fatal: the divergence is inert while a
        // secret-free credential is selected, and taking the whole BFF down over the agent endpoint's
        // credential would be disproportionate.
        var agentSecret = _configuration["AgentToken:ClientSecret"];
        var transitionalSecret = FirstNonBlank(
            _configuration["AzureAd:ClientSecret"],
            _configuration["API_CLIENT_SECRET"],
            _configuration["AZURE_CLIENT_SECRET"]);

        if (!string.IsNullOrWhiteSpace(agentSecret)
            && !string.IsNullOrWhiteSpace(transitionalSecret)
            && !string.Equals(agentSecret, transitionalSecret, StringComparison.Ordinal))
        {
            _logger.LogError(
                "AgentToken:ClientSecret DIVERGES from the transitional secret the credential provider "
                + "resolves (fingerprints {AgentFingerprint} vs {ProviderFingerprint}; "
                + "ClientSecret {InOrder} in {Section}:Order). Since auth-v4 task 022 the agent OBO "
                + "exchange uses the provider's secret, not this one, so a divergence means the agent "
                + "path would present a different credential than it did before the migration — "
                + "surfacing as AADSTS7000215 with no indication of which setting is stale. Reconcile "
                + "them, or remove AgentToken:ClientSecret (task 033 removes it in any case).",
                Fingerprint(agentSecret),
                Fingerprint(transitionalSecret),
                order.Contains(CredentialKind.ClientSecret) ? "IS" : "is NOT",
                CredentialSelectionOptions.SectionName);
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Short SHA-256 prefix, so two secrets can be compared in a log line without either appearing in
    /// it. Same construction the provider uses for its cache key.
    /// </summary>
    private static string Fingerprint(string value)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))[..16];
}
