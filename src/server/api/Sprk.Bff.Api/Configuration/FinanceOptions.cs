using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Finance Intelligence Module.
/// Bound to the "Finance" configuration section.
/// </summary>
public class FinanceOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Finance";

    /// <summary>
    /// Below this confidence, classify as Unknown instead of InvoiceCandidate.
    /// </summary>
    public decimal ClassificationConfidenceThreshold { get; set; } = 0.5m;

    /// <summary>
    /// Fire BudgetWarning signal at this percentage of budget consumed.
    /// </summary>
    public decimal BudgetWarningPercentage { get; set; } = 80m;

    /// <summary>
    /// Fire VelocitySpike signal when spend increases by this percentage month-over-month.
    /// </summary>
    public decimal VelocitySpikePct { get; set; } = 50m;

    /// <summary>
    /// Finance summary cache TTL in minutes. Explicit invalidation is primary; TTL is safety net.
    /// </summary>
    public int FinanceSummaryCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Azure OpenAI deployment name for extraction (Playbook B). gpt-4o for accuracy.
    /// </summary>
    public string ExtractionDeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// Stable opaque ID of the Finance Invoice Processing playbook (sprk_playbookid, GUID-format).
    /// </summary>
    /// <remarks>
    /// Per Q&amp;A 2026-06-22 Q1: code resolves playbooks by <c>sprk_playbookid</c> (immutable opaque ID,
    /// mirrors the row's <c>sprk_analysisplaybookid</c> PK GUID). Value formerly was the literal
    /// slug <c>"PB-013"</c> (a <c>sprk_playbookcode</c> admin-facing value), which silently broke
    /// when the lookup column changed under the Q1 refactor.
    /// DEV environment value: <c>1e657651-9308-f111-8407-7c1e520aa4df</c> (Finance Invoice Processing).
    /// Higher environments populate at deploy time via per-env config.
    /// </remarks>
    public string InvoiceExtractionPlaybookId { get; set; } = string.Empty;

    /// <summary>
    /// SPE container id of the <b>Procedure Library</b> (ebilling-r1 FR-16, D-55): billing guidelines, rate cards and
    /// internal procedures. Internal only (D-63): never visible to firm (CIAM) users. Its documents go through the
    /// standard document pipeline and are intended to be indexed (blocked today by the pointer check — DEF-014).
    /// </summary>
    /// <remarks>
    /// Optional at start-up: empty is allowed so no stamp fails to boot; a consumer that needs it refuses with a stable
    /// ProblemDetails code when it is empty. A non-empty value must be a container id (<c>b!…</c>). App setting
    /// <c>Finance__ProcedureLibraryContainerId</c>, written by provisioning or an operator, never by the customer — it is
    /// one of <see cref="Sprk.Bff.Api.Infrastructure.DI.GraphModule.StampContainerSettingKeys"/> (D-67).
    /// </remarks>
    [RegularExpression(ContainerIdPattern, ErrorMessage = "Finance:ProcedureLibraryContainerId must be a SharePoint Embedded container id starting with 'b!', or empty.")]
    public string ProcedureLibraryContainerId { get; set; } = string.Empty;

    /// <summary>
    /// SPE container id of <b>E-Billing Output Files</b> (ebilling-r1 FR-16, D-55): AP export files and adjustment reports.
    /// Files in this container are documents but are never AI-indexed (<c>FileIndexingService</c> skips them).
    /// </summary>
    /// <remarks>
    /// Optional at start-up, same rules as <see cref="ProcedureLibraryContainerId"/>. App setting
    /// <c>Finance__EbillingOutputContainerId</c> (D-67).
    /// </remarks>
    [RegularExpression(ContainerIdPattern, ErrorMessage = "Finance:EbillingOutputContainerId must be a SharePoint Embedded container id starting with 'b!', or empty.")]
    public string EbillingOutputContainerId { get; set; } = string.Empty;

    /// <summary>
    /// A SharePoint Embedded container (drive) id: <c>b!</c> followed by URL-safe base64 characters only (so a comma- or
    /// space-separated list such as <c>b!x,b!y</c> is refused). Empty passes (optional key). Distinctness from the other
    /// indexed containers is checked by <see cref="FinanceOptionsValidator"/>.
    /// </summary>
    private const string ContainerIdPattern = @"^b![A-Za-z0-9_-]+$";
}
