namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Azure AI Content Understanding settings for invoice reading (ebilling-r1 task 043; FR-21, FR-47, D-49, D-81).
/// Bound to the <c>ContentUnderstanding</c> section created by task 003; validated at startup by
/// <see cref="ContentUnderstandingOptionsValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No key.</b> Authentication is the BFF's managed identity only (the DI <c>TokenCredential</c>, scope
/// <c>https://cognitiveservices.azure.com/.default</c>; ADR-028, FR-47). This class deliberately has no key,
/// secret or connection-string property; do not add one.
/// </para>
/// <para>
/// <b>Preview primary, GA fallback (D-81).</b> The analyzer built on <c>2026-06-01-preview</c> is primary under a
/// root CLAUDE.md §6.5 path-A exception; a GA twin with the same field schema is kept in step on
/// <c>2025-11-01</c>. Analyzers are scoped to the api-version they were created with (a preview analyzer is 404
/// under GA and vice versa), so <see cref="ApiVersion"/> and <see cref="AnalyzerId"/> always change together:
/// <c>2026-06-01-preview</c> + <c>sprk_invoice_v2p</c> (primary) or <c>2025-11-01</c> + <c>sprk_invoice_v2</c>
/// (fallback). Switching is configuration only; no code change.
/// </para>
/// <para>
/// <b>Not configured.</b> An empty <see cref="Endpoint"/> is valid: the host starts, the health check reports
/// Healthy with "not configured", and <c>IInvoiceAi.ReadInvoiceAsync</c> refuses with
/// <see cref="FeatureDisabledException"/> (<c>ai.invoice.read.not_configured</c>).
/// </para>
/// </remarks>
public sealed class ContentUnderstandingOptions
{
    /// <summary>Configuration section name (task 003).</summary>
    public const string SectionName = "ContentUnderstanding";

    /// <summary>The D-81 primary api-version.</summary>
    public const string PreviewApiVersion = "2026-06-01-preview";

    /// <summary>The D-81 primary analyzer (created on <see cref="PreviewApiVersion"/>).</summary>
    public const string PreviewAnalyzerId = "sprk_invoice_v2p";

    /// <summary>Upper bound of <see cref="MaxPages"/>.</summary>
    public const int MaxPagesCeiling = 2000;

    /// <summary>Upper bound of <see cref="PageRangeSize"/>: the service's per-request page limit.</summary>
    public const int PageRangeSizeCeiling = 300;

    /// <summary>
    /// Bare resource endpoint of the environment's AIServices resource (dev:
    /// <c>https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com/</c>). Empty = reader not configured.
    /// When set it must be an absolute https URL.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// REST api-version: a GA date (<c>2025-11-01</c>) or a date followed by <c>-preview</c>. Default: the D-81
    /// primary <see cref="PreviewApiVersion"/>. Must match the version <see cref="AnalyzerId"/> was created on.
    /// </summary>
    public string ApiVersion { get; set; } = PreviewApiVersion;

    /// <summary>
    /// Custom analyzer id (letters, digits, <c>.</c> and <c>_</c>; the service rejects hyphens). Default: the D-81
    /// primary <see cref="PreviewAnalyzerId"/>.
    /// </summary>
    public string AnalyzerId { get; set; } = PreviewAnalyzerId;

    /// <summary>
    /// Largest document, in pages, the reader accepts (default 300, valid 1..2000). A longer document is refused
    /// with <c>invoice.read.page_limit</c>; it is never truncated (input truncation stays off).
    /// </summary>
    public int MaxPages { get; set; } = 300;

    /// <summary>
    /// Pages analyzed per call (default 20, valid 1..300). A document of up to this many pages is read in one
    /// analyze call; a longer one is read in consecutive page ranges and merged (task 005 spike condition 3: the
    /// analyzer loses rows on very long documents read whole). Re-measured in tasks 161 / 162.
    /// </summary>
    public int PageRangeSize { get; set; } = 20;

    /// <summary>
    /// Longest wait for one analyze operation, in seconds (default 600, valid 30..3600). The spike's slowest
    /// whole-document call took 364 s.
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Wait between polls of an analyze operation, in milliseconds (default 2000, valid 250..30000; the floor stops a
    /// misconfiguration from polling the service in a tight loop for the whole operation timeout). A
    /// <c>Retry-After</c> header from the service takes precedence (capped at 30 s).
    /// </summary>
    public int PollIntervalMilliseconds { get; set; } = 2000;

    /// <summary>True when <see cref="Endpoint"/> is set.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
