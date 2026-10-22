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
    /// The Finance file store (ebilling-r1 task 090, procedure-and-file-model section 4.2): Azure Blob with managed identity,
    /// holding Procedure sources, invoice PDFs and supporting files, and export files. Bound from <c>Finance:FileStore</c> and
    /// checked at start-up by <see cref="FinanceOptionsValidator"/>. Optional: an empty endpoint boots, and the file store then
    /// refuses every operation as not configured.
    /// </summary>
    public FinanceFileStoreOptions FileStore { get; set; } = new();

    /// <summary>
    /// A SharePoint Embedded container (drive) id: <c>b!</c> followed by URL-safe base64 characters only (so a comma- or
    /// space-separated list such as <c>b!x,b!y</c> is refused). Empty passes (optional key). Distinctness from the other
    /// indexed containers is checked by <see cref="FinanceOptionsValidator"/>.
    /// </summary>
    private const string ContainerIdPattern = @"^b![A-Za-z0-9_-]+$";
}

/// <summary>
/// <c>Finance:FileStore</c> (ebilling-r1 task 090; procedure-and-file-model section 4.2). Managed identity only: the endpoint is a
/// bare <c>https</c> blob endpoint, never a connection string, account key or SAS (ADR-028, root CLAUDE.md §9); a secret-bearing
/// value fails start-up. The containers (<c>procedures</c>, <c>invoice-files</c>, <c>billing-exports</c>) and the role (Storage
/// Blob Data Contributor plus <c>blobs/tags/read</c>) are provisioned by task 091.
/// </summary>
public sealed class FinanceFileStoreOptions
{
    /// <summary>The configuration path of this section.</summary>
    public const string SectionPath = "Finance:FileStore";

    /// <summary>Default <see cref="OrphanGraceMinutes"/>: one day.</summary>
    public const int DefaultOrphanGraceMinutes = 1440;

    /// <summary>Largest <see cref="OrphanGraceMinutes"/>: 30 days.</summary>
    public const int MaxOrphanGraceMinutes = 43200;

    /// <summary>Default <see cref="NotServableAlertMinutes"/>.</summary>
    public const int DefaultNotServableAlertMinutes = 60;

    /// <summary>Smallest <see cref="NotServableAlertMinutes"/>.</summary>
    public const int MinNotServableAlertMinutes = 5;

    /// <summary>Largest <see cref="NotServableAlertMinutes"/> (7 days).</summary>
    public const int MaxNotServableAlertMinutes = 10080;

    /// <summary>
    /// The storage account's blob endpoint, e.g. <c>https://&lt;account&gt;.blob.core.windows.net</c>. Empty = not configured:
    /// the host boots and every file-store operation refuses (<c>stored_file.store_not_configured</c>; the download answers 503).
    /// App setting <c>Finance__FileStore__BlobEndpoint</c>, set per environment (079 after 091).
    /// </summary>
    public string BlobEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The first segment of every blob name (<c>{tenantId}/{parent table}/{parent id}/{file id}</c>): the OrganizationId of the
    /// Dataverse ENVIRONMENT this deployment serves (not the Entra tenant id, not a customer id: a customer's prod and UAT must never
    /// share a prefix). A GUID, required when <see cref="BlobEndpoint"/> is set; start-up checks the shape only, and the store checks
    /// it against the environment's <c>organization</c> row before its first operation (a mismatch refuses every operation). Every
    /// finished name is re-checked against it, and a stored path naming another segment is refused. Set once per environment and
    /// never changed while rows exist (a row's path is stable for its lifetime). App setting <c>Finance__FileStore__TenantId</c>.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Whether Defender for Storage malware scanning runs on the account. <c>null</c> (absent) means ENABLED: a file is served only
    /// when its scan tag says "No threats found" with a scan time no earlier than its Last-Modified. Only an explicit <c>false</c>
    /// serves files as not scanned (dev, where scanning is off, 091).
    /// </summary>
    public bool? MalwareScanningEnabled { get; set; }

    /// <summary>
    /// How old a blob with no Stored File row must be before the reconcile job deletes it, in minutes. Must be at least the invoice
    /// submission job's longest retry horizon (validated at start-up), so a submission still being retried never loses its bytes.
    /// </summary>
    public int OrphanGraceMinutes { get; set; } = DefaultOrphanGraceMinutes;

    /// <summary>A row still Not yet servable after this many minutes is reported by the reconcile job's detector (a warning; no repair).</summary>
    public int NotServableAlertMinutes { get; set; } = DefaultNotServableAlertMinutes;

    /// <summary>True when an endpoint is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BlobEndpoint);

    /// <summary>True unless scanning is explicitly switched off.</summary>
    public bool ScanningEnabled => MalwareScanningEnabled != false;
}
