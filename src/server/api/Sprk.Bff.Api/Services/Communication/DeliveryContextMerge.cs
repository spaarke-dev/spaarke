using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// FR-C2 (email-communication-intelligence-r2, task 022): when task-021 dedup collapses N copies of the same
/// email to one canonical <c>sprk_communication</c>, the DISCARDED copies' delivery context — which monitored
/// mailbox delivered each copy, which user saved it — is MERGED onto the canonical row instead of dropped, so
/// downstream review sees the full delivery picture (no delivery fact lost).
/// </summary>
/// <remarks>
/// <para>The per-copy unique fact is the DELIVERING MAILBOX (recipients/subject/body are identical across copies
/// of one email). It is accumulated as a <c>"; "</c>-delimited SET on the canonical row — an additive extension of
/// the canonical row (ADR-045; the row stays the reconciliation unit — NOT a child entity). The set-union (not
/// concat) makes the merge IDEMPOTENT: Service-Bus redelivery of the same duplicate adds nothing.</para>
/// <para>Two columns (gated schema, managed solution per ADR-027): <see cref="DeliveredMailboxesAttribute"/> (the
/// inbound delivering mailboxes) and <see cref="SavedByUsersAttribute"/> (the user-upload savers).</para>
/// <para>Best-effort / non-fatal (NFR-04): <see cref="MergeAsync"/> catches every failure and degrades — a merge
/// failure NEVER fails capture or upload.</para>
/// </remarks>
public static class DeliveryContextMerge
{
    private const string EntityName = "sprk_communication";

    /// <summary>Set-union of monitored mailbox addresses that delivered a copy of this email (FR-C2 inbound).</summary>
    public const string DeliveredMailboxesAttribute = "sprk_deliveredmailboxes";

    /// <summary>Set-union of user identities that saved a copy of this email via upload (FR-C2 office/upload).</summary>
    public const string SavedByUsersAttribute = "sprk_savedbyusers";

    private const string Separator = "; ";
    // Guard against unbounded growth of a memo column (Dataverse memo max is large, but bound defensively).
    private const int MaxLength = 8000;

    /// <summary>
    /// Idempotent set-union of a <c>"; "</c>-delimited set: returns <paramref name="existing"/> with
    /// <paramref name="value"/> added iff absent (case-insensitive — addresses/oids are case-insensitive).
    /// Order-preserving (existing entries first, new appended). Pure + allocation-light; the testable core of the
    /// merge's idempotency contract. Returns <paramref name="existing"/> unchanged (trimmed set) when the value is
    /// blank or already present.
    /// </summary>
    public static string Union(string? existing, string? value)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            var trimmed = candidate?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                ordered.Add(trimmed);
        }

        if (existing is not null)
            foreach (var part in existing.Split(';'))
                Add(part);
        Add(value);

        var joined = string.Join(Separator, ordered);
        return joined.Length > MaxLength ? joined[..MaxLength] : joined;
    }

    /// <summary>
    /// Reads the canonical row's current set for <paramref name="attribute"/>, set-unions <paramref name="value"/>
    /// in, and writes it back ONLY when the set actually changed (idempotent — no write, no double-append on
    /// redelivery). Best-effort / non-fatal (NFR-04): any read/write failure logs and returns without throwing —
    /// the caller's capture/upload path is never failed over a delivery-context merge.
    /// </summary>
    public static async Task MergeAsync(
        IGenericEntityService dataverse,
        Guid canonicalId,
        string attribute,
        string? value,
        ILogger logger,
        CancellationToken ct)
    {
        if (canonicalId == Guid.Empty || string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            var row = await dataverse.RetrieveAsync(EntityName, canonicalId, new[] { attribute }, ct)
                .ConfigureAwait(false);
            var current = row?.GetAttributeValue<string>(attribute);
            var merged = Union(current, value);

            // Idempotent: unchanged set (already present / redelivery) → no write.
            if (string.Equals(merged, current, StringComparison.Ordinal))
                return;

            await dataverse.UpdateAsync(
                    EntityName,
                    canonicalId,
                    new Dictionary<string, object> { [attribute] = merged },
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FR-C2 delivery-context merge failed (non-fatal) for canonical {CanonicalId}, attribute {Attribute}.",
                canonicalId, attribute);
        }
    }
}
