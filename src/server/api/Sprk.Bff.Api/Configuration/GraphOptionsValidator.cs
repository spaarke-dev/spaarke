using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Validates GraphOptions with conditional requirements based on authentication mode.
/// </summary>
public class GraphOptionsValidator : IValidateOptions<GraphOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphOptions options)
    {
        var errors = new List<string>();

        // If ManagedIdentity is enabled, ClientId is required.
        // Retained: this one guards a setting that IS consumed — GraphClientFactory and
        // ManagedIdentityCredentialFactory both read the Graph:ManagedIdentity:ClientId key directly,
        // and on a multi-identity App Service an unpinned managed identity fails with "Unable to load
        // the proper Managed Identity".
        if (options.ManagedIdentity.Enabled && string.IsNullOrWhiteSpace(options.ManagedIdentity.ClientId))
        {
            errors.Add("Graph:ManagedIdentity:ClientId is required when ManagedIdentity is enabled");
        }

        // ── REMOVED (auth-v4 task 024, FR-B5) ──────────────────────────────────────────────────────
        // Previously: "If ManagedIdentity is disabled, Graph:ClientSecret is required."
        //
        // That rule blocked startup on a value NOTHING READS. Verified exhaustively at task 024 rather
        // than assumed: GraphOptions.ClientSecret has no consumer anywhere in src/. The two
        // .WithClientSecret(_options.ClientSecret) call sites that look like consumers —
        // ReportingEmbedService:80 and ReportingProfileManager:77 — take IOptions<PowerBiOptions>, a
        // different type with its own ClientSecret and its own separate secret. So the rule mandated a
        // setting whose only effect was to prevent a secret-free boot.
        //
        // Removing it does NOT weaken the system's credential validation; it moves the question to the
        // component that can answer it. Whether a usable credential exists depends on the ORDERED
        // CREDENTIAL LIST, which this options type knows nothing about, and is checked at startup by
        // CredentialSelectionOptionsValidator (order well-formed) and IdentityConfigurationValidator
        // (identities coherent; no-credential-of-any-kind fails fast). Re-deriving it here would be the
        // per-call-site credential handling ADR-028 A4 exists to end.
        // ───────────────────────────────────────────────────────────────────────────────────────────

        return errors.Any()
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
