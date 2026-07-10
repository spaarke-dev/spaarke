using System.Globalization;

namespace Sprk.Bff.Api.Services.Ai.Audit;

/// <summary>
/// Builds the SCALABLE synthetic partition-key value for the Tier-2 compliance audit container
/// (task AIR2-074, spec FR-D-05, design row R21).
///
/// <para>
/// <b>Why this exists.</b> The permanent audit container was originally keyed on bare
/// <c>/tenantId</c>. Spaarke deployments are customer-dedicated (one tenant per Cosmos database),
/// so <c>/tenantId</c> collapses <i>every</i> audit write into a SINGLE logical partition — a
/// slow-motion failure against Cosmos's 20 GB logical-partition cap. This helper re-keys the audit
/// container off <c>/tenantId</c> onto a tenant + monthly-time-bucket value
/// (<c>{tenantId}|{yyyy-MM}</c>), so each month rolls into a fresh logical partition and no single
/// partition grows unboundedly. Cosmos partition keys cannot change in place, so the re-key ships as
/// a NEW container (<c>audit-partitioned</c>) with the audit-write path cut over to it.
/// </para>
///
/// <para>
/// <b>One convention with the memory store (task AIR2-050 / ADR-042).</b> The memory-items container
/// adopts the same discipline: a single synthetic scalable partition-key path
/// (<c>/subjectId</c> there, <c>/partitionKey</c> here), NEVER bare <c>/tenantId</c>, whose value is
/// computed and normalized at the single store write chokepoint. Memory partitions by subject
/// identity; audit — which has no natural single subject and is inherently time-series + tenant-
/// scoped — partitions by tenant + time bucket. Tenant-id normalization mirrors
/// <c>MemoryItemStore.NormalizeSubjectId</c> so the two stores canonicalize identifiers identically.
/// </para>
/// </summary>
public static class AuditPartitionKey
{
    /// <summary>Separator between the tenant segment and the monthly time-bucket segment.</summary>
    private const char Separator = '|';

    /// <summary>
    /// Builds the deterministic partition-key value <c>{normalizedTenantId}|{yyyy-MM}</c> for an audit
    /// record. The monthly bucket is derived from the record's UTC timestamp, so writes for a tenant
    /// spread across one logical partition per calendar month.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (normalized per <see cref="NormalizeTenantId"/>).</param>
    /// <param name="timestamp">The audit record's timestamp; bucketed by its UTC year-month.</param>
    /// <returns>The synthetic partition-key value written to the <c>/partitionKey</c> document path.</returns>
    public static string Build(string tenantId, DateTimeOffset timestamp)
    {
        var tenant = NormalizeTenantId(tenantId);
        var bucket = timestamp.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return $"{tenant}{Separator}{bucket}";
    }

    /// <summary>
    /// Normalizes a tenant id: trim, strip braces, and canonicalize GUIDs to lowercase "D" format.
    /// Cosmos partition-key values are case-sensitive, and AAD tenant ids can arrive as
    /// <c>{UPPERCASE}</c> GUIDs from different call sites; canonicalizing keeps every audit write for a
    /// tenant in the same logical partition. Mirrors <c>MemoryItemStore.NormalizeSubjectId</c>
    /// (task 050) so both stores use one canonicalization rule. Non-GUID ids are trimmed only —
    /// casing is preserved because they may be externally meaningful.
    /// </summary>
    internal static string NormalizeTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var trimmed = tenantId.Trim().Trim('{', '}');
        return Guid.TryParse(trimmed, out var guid) ? guid.ToString("D") : trimmed;
    }
}
