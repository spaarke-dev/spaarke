// -----------------------------------------------------------------------------
// IOrgSettingsContractApplier.cs
//
// HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) — F14 verbatim.
// Fresh Production-tier Dataverse envs default `maxuploadfilesize=5MB`,
// but the UniversalDocumentUpload PCF bundle exceeds this → solution
// import fails 5 min in with "Webresource content size is too big". This
// seam gates H6 on applying the canonical Org Settings contract (e.g.
// maxuploadfilesize=25_600_000) BEFORE any solution import fires.
//
// PRODUCTION IMPL (task 253, G38):
//   <see cref="DataverseWebApiOrgSettingsContractApplier"/> — one GET of the
//   environment's `organization` row, then one PATCH of the settings below
//   target, through the Dataverse Web API as H6's importer identity. It
//   replaced PacOrgSettingsContractApplier (`pac org update-settings`): the
//   L2 Worker host has no pac CLI.
//
// MANIFEST:
//   The canonical (column name → minimum value) map is
//   <see cref="StaticOrgSettingsContractManifest"/> below — code, not a file
//   (the Worker publish carries no scripts/ folder).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Applies the canonical Org Settings contract on the target Dataverse env
/// BEFORE H6's solution import fires. Idempotent — no-op when settings
/// already match. Domain outcomes never throw.
/// </summary>
public interface IOrgSettingsContractApplier
{
    /// <summary>Applies each entry in <paramref name="request"/>.OrgSettings.</summary>
    Task<OrgSettingsContractOutcome> ApplyAsync(
        OrgSettingsContractApplyRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Input for <see cref="IOrgSettingsContractApplier.ApplyAsync"/>.</summary>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit).</param>
/// <param name="ClientId">BFF app-reg id — the identity H6 signs in to the environment as.</param>
/// <param name="ClientSecret">BFF app-reg secret for the legacy FR-39 ClientSecret chain (null on secret-free envs).</param>
/// <param name="TargetDataverseUrl">Customer Dataverse env URL (H5 output).</param>
/// <param name="OrgSettings">Canonical (settingName, expectedValue) map from the manifest.</param>
public sealed record OrgSettingsContractApplyRequest(
    string TenantId,
    string ClientId,
    string? ClientSecret,
    string TargetDataverseUrl,
    IReadOnlyDictionary<string, string> OrgSettings);

/// <summary>Result of <see cref="IOrgSettingsContractApplier.ApplyAsync"/>.</summary>
public abstract record OrgSettingsContractOutcome
{
    /// <summary>All settings match or were successfully applied.</summary>
    public sealed record Success(IReadOnlyDictionary<string, string> AppliedOrAlreadyCorrect) : OrgSettingsContractOutcome;

    /// <summary>
    /// One or more settings could not be applied.
    /// </summary>
    public sealed record Failure(string Diagnostic) : OrgSettingsContractOutcome;
}

/// <summary>
/// The canonical Org Settings contract: <c>organization</c> column name → minimum value. Production impl:
/// <see cref="StaticOrgSettingsContractManifest"/>.
/// </summary>
public interface IOrgSettingsContractManifest
{
    /// <summary>Canonical (settingName, expectedValue) map for the current release.</summary>
    IReadOnlyDictionary<string, string> OrgSettings { get; }
}

/// <summary>
/// Static <see cref="IOrgSettingsContractManifest"/> impl. HANDLER-08
/// (F14 verbatim) canonical set: maxuploadfilesize=25_600_000 (25 MB —
/// covers the UniversalDocumentUpload PCF bundle size). #2027 adds org
/// auditing: isauditenabled=true and auditretentionperiodv2=-1 (Forever).
/// </summary>
public sealed class StaticOrgSettingsContractManifest : IOrgSettingsContractManifest
{
    /// <summary>R1 canonical Org Settings — F14 verbatim, plus org auditing (#2027).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultOrgSettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["maxuploadfilesize"] = "25600000", // 25 MB, F14 verbatim

            // #2027: the per-table and per-column IsAuditEnabled flags SpaarkeMaster carries record nothing while the
            // organization switch is off, and Microsoft documents no default-on — so a new environment could keep no
            // audit history at all. The uac-r2 FR-32 interim answer ("who could see record X on date D") IS that
            // history, and audit cannot be backfilled.
            ["isauditenabled"] = "true",

            // #2027 owner decision (2026-10-18, relayed by uac-r2): keep audit Forever (-1) — a legal-ops system of
            // record; the cost is Dataverse log capacity. -1 is satisfied only by -1 (see the applier).
            ["auditretentionperiodv2"] = "-1",
        };

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> OrgSettings => DefaultOrgSettings;
}
