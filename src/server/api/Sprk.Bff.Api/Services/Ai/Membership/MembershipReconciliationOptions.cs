// R3 Part 1 Phase 2 — Task 085 (2026-06-22)
// Options class for the MembershipReconciliationJob — drives which entity
// types the nightly reconciliation pass scans for source-of-truth lookup
// drift, and how many parent rows are fetched per Dataverse page.
//
// Defaults match the spec FR-2P2.5 membership-served entity set
// (matter / document / event / task / opportunity). Operators can override
// via the "Membership:Reconciliation" appsettings section. The CronSchedule
// default ("0 2 * * *" — 02:00 UTC daily) matches POML step 4.
//
// ADR-010 (DI minimalism): pure data class bound via Options pattern.
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.7,
//            FR-2.7; AC-2.3.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Configuration for <see cref="MembershipReconciliationJob"/>. Binds from the
/// <c>"Membership:Reconciliation"</c> appsettings section. Defaults are
/// conservative — the job runs against the spec FR-2P2.5 entity set and is
/// safe to ship even when the Service Bus topic (task 071) is not yet
/// deployed (the recon job writes the junction directly via
/// <see cref="IMembershipJunctionUpdater"/>).
/// </summary>
public sealed class MembershipReconciliationOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Membership:Reconciliation";

    /// <summary>
    /// Entity logical names whose source-of-truth Lookups are reconciled
    /// against the <c>sprk_userentityassociation</c> junction. Defaults to
    /// the spec FR-2P2.5 membership-served set
    /// (<c>sprk_matter</c>, <c>sprk_document</c>, <c>sprk_event</c>,
    /// <c>sprk_task</c>, <c>sprk_opportunity</c>). Operators MAY override
    /// to include additional entity types or shrink the set for
    /// targeted runs.
    /// </summary>
    /// <remarks>
    /// The discovery service (<see cref="IMembershipFieldDiscoveryService"/>)
    /// determines WHICH lookups on each entity are reconciled — operators do
    /// not list individual fields here. The two surfaces are intentionally
    /// orthogonal: this list controls the OUTER loop (entity types), the
    /// discovery cache controls the INNER loop (per-entity lookup fields).
    /// </remarks>
    public List<string> EntityTypes { get; set; } = new()
    {
        "sprk_matter",
        "sprk_document",
        "sprk_event",
        "sprk_task",
        "sprk_opportunity",
    };

    /// <summary>
    /// Cron schedule for the nightly reconciliation tick. Default
    /// <c>"0 2 * * *"</c> — 02:00 UTC daily, matching task 085 POML step 4
    /// and spec FR-2P2.7 24-hour staleness target.
    /// </summary>
    public string CronSchedule { get; set; } = "0 2 * * *";

    /// <summary>
    /// Whether the recon definition row is enabled by default at seed time.
    /// <c>true</c> — the recon job is INDEPENDENT of the Service Bus topic
    /// deploy gate (task 071) because it writes the junction directly via
    /// <see cref="IMembershipJunctionUpdater"/> without publishing to the
    /// topic. Operators can disable via
    /// <c>POST /api/admin/jobs/membership-reconciliation/disable</c> if a
    /// targeted recon is undesirable.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum parent rows fetched per Dataverse page during the
    /// source-of-truth scan. Default <c>500</c> — balances round-trip count
    /// against memory footprint. Smaller values yield more pages (lower
    /// peak memory); larger values yield fewer round-trips.
    /// </summary>
    public int FetchPageSize { get; set; } = 500;

    /// <summary>
    /// Maximum junction rows fetched per Dataverse page during the orphan
    /// scan. Default <c>500</c>. Same trade-off as <see cref="FetchPageSize"/>.
    /// </summary>
    public int OrphanFetchPageSize { get; set; } = 500;
}
