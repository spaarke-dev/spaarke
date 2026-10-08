// unified-access-control-r2 task 174 (owner round 84, 2026-10-08; closes #1442): enforcement uses a record's EFFECTIVE flags.
//
// A work assignment or project filed under a secure or Limited/Restricted matter or project is treated with the most
// restrictive Secure flag and Access Permission of itself and of every record above it in the filing chain, so a child
// whose own stored flag has not caught up yet (inheritance pending, Refused or Failed; task 175's cascade in flight) is
// never more open than its parent. A matter files under nothing and is unchanged.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the flag read (ExternalParticipationService.GetRootRecordFlagsAsync) reads the record's OWN row only; the
//       ONE filing walk (SecureRootInheritance.ReadSecureParentsAsync / ReadSecureParentsOfManyAsync, #1410) climbs the chain
//       and, since this task, carries each ancestor's Access Permission from the same parent read.
//   (2) Extension — this is the fold of those two answers, nothing else: no reader, no query, no DI registration. It is a
//       static helper (not a member of either) because the read path folds a walk it already ran for the No Access veto,
//       and the flag reader folds a walk it runs itself; one fold serves both.
//   (3) Cost of doing nothing — a filed child of a secure or Restricted parent keeps org-wide, standing and (on Restricted)
//       direct contact access, and grants on it are accepted, until its own flag is set (#1442).

using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// The effective (parent-derived) access flags of a root record (task 174, owner round 84): the most restrictive of its own
/// <see cref="RootRecordFlags"/> and those of every record it is filed under, through the ONE filing walk.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> Secure if the record or ANY ancestor is secure (secure-if-any). Access Permission: Restricted over
/// Limited over Standard across the record and its ancestors. The record's own <c>statecode</c> is kept (an ancestor's state
/// is not an access policy). A <c>sprk_matter</c> files under nothing and is unchanged.</para>
/// <para><b>Fails closed (ADR-003).</b> An ancestry the walk could not decide — an unreadable row, filing, pair type or
/// ancestor flag, an EMPTY ancestor flag, a chain past <see cref="SecureRootInheritance.MaxFilingDepth"/> — folds to
/// <see cref="RootRecordFlags.Unreadable"/>: secure, Limited AND Restricted, with the unreadable marker, so the write-time
/// policy reports "could not be read" rather than a Restricted record it cannot back.</para>
/// <para><b>Never widens.</b> Every term is an OR or a max over the record's own value, so the result is never more open than
/// the record's own flags.</para>
/// </remarks>
internal static class EffectiveRootFlags
{
    /// <summary>One record's effective flags from its own and its ancestry. <c>null</c> ancestry (a table that files under
    /// nothing) returns <paramref name="own"/>.</summary>
    internal static RootRecordFlags Fold(RootRecordFlags own, SecureParentsAnswer? ancestry)
    {
        if (ancestry is null || own.IsUnreadable)
        {
            return own;
        }

        if (!ancestry.IsKnown)
        {
            return RootRecordFlags.Unreadable;
        }

        var inherited = FilingPermission.Rank(ancestry.StrictestPermission?.Value);
        var restricted = own.IsRestricted || inherited == 2;
        return own with
        {
            IsSecure = own.IsSecure || ancestry.HasSecureParent,
            IsRestricted = restricted,
            IsLimited = !restricted && (own.IsLimited || inherited == 1),
        };
    }

    /// <summary>Folds every record of a batch. An id without an ancestry answer keeps its own flags (the walk answers every
    /// id it is asked about; <c>null</c> ancestry means the table files under nothing).</summary>
    internal static IReadOnlyDictionary<Guid, RootRecordFlags> Fold(
        IReadOnlyDictionary<Guid, RootRecordFlags> own, IReadOnlyDictionary<Guid, SecureParentsAnswer>? ancestry)
    {
        if (ancestry is null || ancestry.Count == 0)
        {
            return own;
        }

        return own.ToDictionary(kv => kv.Key, kv => Fold(kv.Value, ancestry.GetValueOrDefault(kv.Key)));
    }

    /// <summary>
    /// What <paramref name="ids"/> of <paramref name="entityType"/> are filed under, at every level
    /// (<see cref="SecureRootInheritance.MaxFilingDepth"/>): the ONE walk, batched for many records and the one-record shape
    /// for one. <c>null</c> for a table that files under nothing (no read). Never throws a read fault: a walk that throws,
    /// or no reader, answers every id <see cref="SecureParentsAnswer.Unverifiable"/> (fail closed).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, SecureParentsAnswer>?> ReadAncestryAsync(
        IGenericEntityService? dataverse, ILogger logger, string entityType, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (!SecureRootInheritance.Inherits(entityType))
        {
            return null;
        }

        var distinct = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, SecureParentsAnswer>();
        }

        if (dataverse is null)
        {
            logger.LogError(
                "[EFFECTIVE-ACCESS] No filing reader is configured, so what {Count} {EntityType} record(s) are filed under cannot " +
                "be read. Failing CLOSED — each is treated as secure and Restricted (task 174).", distinct.Count, entityType);
            return Unverifiable(distinct, "what it is filed under could not be read (no filing reader)");
        }

        try
        {
            if (distinct.Count == 1)
            {
                var one = await SecureRootInheritance
                    .ReadSecureParentsAsync(dataverse, logger, entityType, distinct[0], ct, maxDepth: SecureRootInheritance.MaxFilingDepth)
                    .ConfigureAwait(false);
                return new Dictionary<Guid, SecureParentsAnswer> { [distinct[0]] = one };
            }

            return await SecureRootInheritance
                .ReadSecureParentsOfManyAsync(dataverse, logger, entityType, distinct, ct, maxDepth: SecureRootInheritance.MaxFilingDepth)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EFFECTIVE-ACCESS] What {Count} {EntityType} record(s) are filed under could not be read. Failing CLOSED — " +
                "each is treated as secure and Restricted (task 174).", distinct.Count, entityType);
            return Unverifiable(distinct, "what it is filed under could not be read");
        }
    }

    /// <summary>
    /// ONE record's effective flags from its own (already read) flags: the single-record walk, skipped when no ancestor can
    /// make the record stricter (its own flags unreadable, or already Secure AND Restricted) or the table files under
    /// nothing. For the callers that hold a filing reader of their own (the No Access guard and enforcer).
    /// </summary>
    internal static async Task<RootRecordFlags> FoldOneAsync(
        IGenericEntityService dataverse, ILogger logger, string entityType, Guid recordId, RootRecordFlags own,
        CancellationToken ct)
    {
        if (own.IsUnreadable || (own.IsSecure && own.IsRestricted) || !SecureRootInheritance.Inherits(entityType))
        {
            return own;
        }

        var ancestry = await ReadAncestryAsync(dataverse, logger, entityType, new[] { recordId }, ct).ConfigureAwait(false);
        return Fold(own, ancestry?.GetValueOrDefault(recordId));
    }

    private static IReadOnlyDictionary<Guid, SecureParentsAnswer> Unverifiable(IEnumerable<Guid> ids, string why) =>
        ids.ToDictionary(id => id, _ => new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), why));

    /// <summary>
    /// The record a filed child's effective access comes from, for display (task 174 / 067 amendment): the ancestor that
    /// raises the Access Permission above the record's own, else the first secure ancestor when the record itself is not
    /// flagged secure; <c>null</c> when the record's own flags already govern (or the ancestry is unknown).
    /// </summary>
    internal static SecureFilingParent? InheritedFrom(RootRecordFlags own, SecureParentsAnswer? ancestry)
    {
        if (ancestry is null || !ancestry.IsKnown || own.IsUnreadable)
        {
            return null;
        }

        var ownRank = own.IsRestricted ? 2 : own.IsLimited ? 1 : 0;
        if (ancestry.StrictestPermission is { } permission && FilingPermission.Rank(permission.Value) > ownRank)
        {
            return permission.From;
        }

        return !own.IsSecure && ancestry.HasSecureParent ? ancestry.SecureParents[0] : null;
    }
}

/// <summary>
/// One record's effective access for display (task 174, task 067's amendment): the effective flags, and the record they are
/// inherited from when an ancestor makes them stricter than the record's own.
/// </summary>
/// <param name="Flags">The effective flags (<see cref="EffectiveRootFlags.Fold(RootRecordFlags, SecureParentsAnswer?)"/>).</param>
/// <param name="InheritedFrom">The ancestor that governs, or <c>null</c> when the record's own flags do.</param>
public sealed record EffectiveRootAccess(RootRecordFlags Flags, SecureFilingParent? InheritedFrom);
