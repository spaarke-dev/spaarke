namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R5 task 007 (D1-07) — configuration for the session-files cleanup
/// <see cref="SessionFilesCleanupJob"/> hosted service.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the <c>SessionFilesCleanup</c> configuration section by
/// <c>AnalysisServicesModule.AddAnalysisServicesModule</c>. Admin-overridable per
/// environment to tune cleanup cadence and batch sizes.
/// </para>
/// <para>
/// Per R5 CLAUDE.md §3.2 this options class intentionally has NO <c>Enabled</c>
/// property — the cleanup job's kill-switch is the
/// <c>(Analysis:Enabled &amp;&amp; DocumentIntelligence:Enabled)</c> compound gate
/// that the registration block sits inside (registration itself is the kill-switch
/// per ADR-018 Flag Scope Discipline).
/// </para>
/// </remarks>
public sealed class SessionFilesCleanupOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SessionFilesCleanup";

    /// <summary>
    /// Hours between scheduled cleanup runs. Default 6 (matches spec NFR-02 cadence guidance).
    /// Minimum 1 (clamped at validation time); values &lt; 1 fall back to default 6.
    /// </summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>
    /// Maximum number of documents deleted per AI Search <c>DeleteDocumentsAsync</c> batch call.
    /// Default 1000 (Azure AI Search SDK per-request limit).
    /// </summary>
    public int DeleteBatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum number of Redis session keys scanned per tenant per scheduled run.
    /// Default 10000 — guard against unbounded scans on large tenants.
    /// </summary>
    public int MaxKeysPerScan { get; set; } = 10_000;
}
