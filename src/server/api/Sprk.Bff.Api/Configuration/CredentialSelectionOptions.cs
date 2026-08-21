using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// The confidential credentials the BFF may present as its own identity, in priority order
/// (ADR-028 Amendment <b>A4</b>).
/// </summary>
public enum CredentialKind
{
    /// <summary>
    /// A client assertion minted by the App Service's user-assigned managed identity and trusted by
    /// the app registration through a federated identity credential. The canonical A4 credential:
    /// secret-free, nothing to rotate, nothing to leak.
    /// </summary>
    ManagedIdentityFederated,

    /// <summary>
    /// An X.509 certificate whose private key lives in Key Vault. A4's sanctioned alternative for
    /// deployment shapes where MI-FIC's same-tenant rule cannot hold. Secret-free in the sense that
    /// matters — no bearer string in configuration — but it does expire and must be rotated.
    /// </summary>
    KeyVaultCertificate,

    /// <summary>
    /// The client secret. <b>Transitional only</b>, under ADR-028 exception <b>E-3</b>, and removed by
    /// this project's task 033. Microsoft ranks it last ("development and testing only"). It remains in
    /// the default order solely because it is the rollback target while the migration is in flight.
    /// </summary>
    ClientSecret,
}

/// <summary>
/// Binds <c>Graph:Credentials</c> — the ordered credential list that makes rollback a configuration
/// edit rather than a redeploy (spec FR-B2, NFR-06).
///
/// <para><b>What this buys.</b> Design §6 claims rollback at every phase is "a credential reorder or a
/// slot swap back". Without an ordered list that claim is simply false: every call site hard-codes one
/// credential, so backing out means changing code and redeploying — during an incident, on the OBO
/// path, which fails closed for every user at once. This options class is the difference between that
/// sentence being true and being aspirational.</para>
///
/// <para><b>Config shape:</b></para>
/// <code>
/// Graph__Credentials__Order__0            = ManagedIdentityFederated
/// Graph__Credentials__Order__1            = ClientSecret
/// Graph__Credentials__KeyVaultCertificateName = spaarke-bff-obo-cert   (only if the cert kind is listed)
/// Graph__Credentials__NegativeCacheSeconds    = 10                     (optional)
/// Graph__Credentials__FailuresBeforeSuppression = 2                    (optional)
/// </code>
///
/// <para><b><see cref="Order"/> deliberately starts EMPTY.</b> Not an oversight — a defence against the
/// configuration binder's collection-merge semantics. Had the property been initialised to the
/// canonical default, binding a shorter list from configuration would leave the surplus defaults in
/// place: an operator narrowing the order to <c>[ManagedIdentityFederated]</c> to prove the secret is
/// unused would silently get <c>[ManagedIdentityFederated, ClientSecret]</c> back, and the secret they
/// were trying to eliminate would still be live. The canonical default is applied instead in
/// <c>AuthorizationModule</c>, and only when the section is entirely absent, so an explicitly empty
/// list stays empty and fails validation as the operator intended.</para>
/// </summary>
public sealed class CredentialSelectionOptions
{
    public const string SectionName = "Graph:Credentials";

    /// <summary>
    /// Credential kinds to try, most-preferred first. Names bind case-insensitively against
    /// <see cref="CredentialKind"/>. See the class remarks on why this starts empty.
    /// </summary>
    public IList<string> Order { get; set; } = new List<string>();

    /// <summary>
    /// Key Vault certificate name for the <see cref="CredentialKind.KeyVaultCertificate"/> branch.
    /// Required only when that kind appears in <see cref="Order"/>.
    /// </summary>
    public string? KeyVaultCertificateName { get; set; }

    /// <summary>
    /// How long a credential is suppressed after it has failed enough consecutive times
    /// (<see cref="FailuresBeforeSuppression"/>). Without this, a credential that cannot be obtained is
    /// retried on <b>every</b> token acquisition — measured at ~80 ms per request off-Azure at task 020.
    ///
    /// <para><b>SECONDS, not minutes — and that bound is measured, not chosen for taste.</b> Task 030
    /// observed Entra <i>flapping</i> for roughly two minutes after a federated credential is created or
    /// changed: successes and failures interleaved as replicas converge, returning
    /// <c>AADSTS70025</c>. A minutes-long suppression would latch onto one transient failure inside that
    /// window and hold the process on the <i>fallback</i> credential — the secret — long after MI-FIC
    /// started working. That is a silent downgrade of the exact property this project exists to
    /// establish, and nothing in the logs would say so, because from the process's point of view
    /// everything is healthy. Keep this small. See notes/decisions/030-fic-automation.md §11.</para>
    /// </summary>
    public int NegativeCacheSeconds { get; set; } = 10;

    /// <summary>
    /// Consecutive failures required before a credential is suppressed. <b>Must be at least 2</b>: one
    /// failure inside the flap window described above is not evidence that a credential is broken, and
    /// demoting on it would be the same silent downgrade by a different route.
    /// </summary>
    public int FailuresBeforeSuppression { get; set; } = 2;
}

/// <summary>
/// Validates <see cref="CredentialSelectionOptions"/> at startup. Data annotations cannot express any
/// of these rules — each is about the <i>relationship</i> between entries, or about a value that is
/// only meaningful because of ADR-028 A4.
/// </summary>
public sealed class CredentialSelectionOptionsValidator : IValidateOptions<CredentialSelectionOptions>
{
    private const string Prefix = "Graph:Credentials";

    public ValidateOptionsResult Validate(string? name, CredentialSelectionOptions options)
    {
        var failures = new List<string>();

        // FR-B2 acceptance criterion: an empty list fails fast with an actionable message. Silently
        // defaulting here would be worse than crashing — it would mean an operator who deliberately
        // blanked the list to force a decision got a credential chosen for them.
        if (options.Order.Count == 0)
        {
            failures.Add(
                $"{Prefix}:Order is empty. The BFF cannot authenticate as itself without at least one "
                + $"credential. Set {Prefix}:Order:0 to one of: "
                + $"{string.Join(", ", Enum.GetNames<CredentialKind>())}.");
        }

        var kinds = new List<CredentialKind>();
        foreach (var (raw, index) in options.Order.Select((v, i) => (v, i)))
        {
            if (Enum.TryParse<CredentialKind>(raw?.Trim(), ignoreCase: true, out var kind))
            {
                kinds.Add(kind);
            }
            else
            {
                failures.Add(
                    $"{Prefix}:Order:{index} is '{raw}', which is not a known credential kind. "
                    + $"Valid values: {string.Join(", ", Enum.GetNames<CredentialKind>())}.");
            }
        }

        var duplicates = kinds.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            failures.Add(
                $"{Prefix}:Order lists {string.Join(", ", duplicates)} more than once. A credential that "
                + "already failed will fail identically on a second attempt, so a repeat is always a typo.");
        }

        if (kinds.Contains(CredentialKind.KeyVaultCertificate)
            && string.IsNullOrWhiteSpace(options.KeyVaultCertificateName))
        {
            failures.Add(
                $"{Prefix}:Order includes {CredentialKind.KeyVaultCertificate} but "
                + $"{Prefix}:KeyVaultCertificateName is not set. Name the Key Vault certificate to load.");
        }

        if (options.FailuresBeforeSuppression < 2)
        {
            // Not a taste rule. See CredentialSelectionOptions.NegativeCacheSeconds: a single failure
            // inside Entra's ~2-minute post-change flap window is not evidence of a broken credential,
            // and suppressing on it silently demotes the process to the fallback secret.
            failures.Add(
                $"{Prefix}:FailuresBeforeSuppression is {options.FailuresBeforeSuppression}; it must be at "
                + "least 2. Suppressing a credential after a single failure would demote the process to "
                + "the fallback credential on one transient error during Entra's post-change "
                + "propagation window (measured ~2 minutes at task 030).");
        }

        if (options.NegativeCacheSeconds is < 0 or > 120)
        {
            failures.Add(
                $"{Prefix}:NegativeCacheSeconds is {options.NegativeCacheSeconds}; it must be between 0 and "
                + "120. A longer suppression outlives Entra's propagation flap window and would hold the "
                + "process on a fallback credential after the preferred one recovered.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Reports whether the configured order promotes the client secret above a secret-free credential —
    /// an ADR-028 A4 deviation.
    ///
    /// <para><b>Why this is a warning and not a validation failure.</b> A4 says never promote a secret
    /// above a secret-free credential, and taken alone that argues for rejecting the configuration
    /// outright. But the ordered list <i>is</i> this project's rollback mechanism (NFR-06), and the
    /// rollback of interest is precisely "put the secret back on top because MI-FIC is failing in
    /// production". Refusing to start in that configuration would disable the emergency exit at the one
    /// moment it is needed — on the OBO path, which fails closed for every user simultaneously. So the
    /// deviation is permitted and made <b>loud</b>: logged at error level, naming A4, so that a
    /// temporary rollback cannot quietly become the permanent state. The forcing functions in Phase 6
    /// (tasks 060/061) are what catch it if it does.</para>
    /// </summary>
    public static bool PromotesSecretAboveSecretFreeCredential(IReadOnlyList<CredentialKind> order)
    {
        var secretIndex = order.ToList().IndexOf(CredentialKind.ClientSecret);
        if (secretIndex < 0)
        {
            return false;
        }

        for (var i = secretIndex + 1; i < order.Count; i++)
        {
            if (order[i] is CredentialKind.ManagedIdentityFederated or CredentialKind.KeyVaultCertificate)
            {
                return true;
            }
        }

        return false;
    }
}
