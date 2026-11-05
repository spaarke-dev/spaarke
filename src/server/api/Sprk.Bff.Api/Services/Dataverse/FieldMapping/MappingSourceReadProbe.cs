using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>Who reads a mapping source: the CALLER (Read-as-caller first) or a system principal (app-only, no probe).</summary>
/// <remarks>
/// The only way to skip the Read probe is to name <see cref="System"/> explicitly; a caller with no token is still a caller,
/// and the probe denies them (it answers <see cref="AccessRights.None"/> for a missing token). Task 013's principal model
/// maps onto this: caller principal → <see cref="ForCaller"/>, system principal → <see cref="System"/> (ADR-028: the
/// managed identity never answers a caller's Read question). A class, not a record: it carries the caller's token, so it
/// has no generated <c>ToString</c> or value equality that could print or compare it.
/// </remarks>
internal sealed class MappingSourceReader
{
    private MappingSourceReader(
        CallerRecordAccessProbe? probe, string? callerBearerToken, Func<Guid, CancellationToken, Task<bool>>? documentShown)
    {
        Probe = probe;
        CallerBearerToken = callerBearerToken;
        DocumentShown = documentShown;
    }

    /// <summary>The caller-rights probe, or <see langword="null"/> for the system principal.</summary>
    internal CallerRecordAccessProbe? Probe { get; }

    /// <summary>The caller's bearer token (OBO input). Never logged.</summary>
    internal string? CallerBearerToken { get; }

    /// <summary>
    /// Task 043 (owner 2026-11-04): whether THE external document narrowing (<c>InternalPlaneDocumentNarrowing</c>) shows a
    /// document to this caller, as the ROUTE decided it, or <see langword="null"/> when the route gave no answer. With no
    /// answer a <c>sprk_document</c> source is not mapped (as before this task). Asked only for a document source, only after
    /// the caller's Read has allowed it, and before the document is read app-only. <see langword="false"/> and a throw both
    /// mean "not shown".
    /// </summary>
    internal Func<Guid, CancellationToken, Task<bool>>? DocumentShown { get; }

    /// <summary>True for the system principal: the source is read app-only without a caller probe.</summary>
    internal bool IsSystem => Probe is null;

    /// <summary>A system creator (generation job, playbook node, ingestion, the repair pass): reads app-only.</summary>
    internal static MappingSourceReader System { get; } = new(null, null, null);

    /// <summary>A caller: the source is read only after <see cref="MappingSourceReadProbe.CheckCallerReadOfSourceAsync"/>
    /// answers Read and, for a document, <paramref name="documentShown"/> shows it.</summary>
    internal static MappingSourceReader ForCaller(
        CallerRecordAccessProbe probe, string? callerBearerToken, Func<Guid, CancellationToken, Task<bool>>? documentShown = null)
        => new(probe ?? throw new ArgumentNullException(nameof(probe)), callerBearerToken, documentShown);
}

/// <summary>A resolved mapping source: the record whose fields a new record inherits (FR-03).</summary>
internal sealed record MappingSource(string EntityLogicalName, Guid RecordId);

/// <summary>
/// What reading a mapping source gave. <see cref="Readable"/> false means NO mapping at all — not even a Default rule —
/// and <see cref="Warning"/> is <see cref="MappingSourceReadProbe.SourceUnavailableWarning"/>.
/// </summary>
/// <param name="Readable">The principal may map from the source (and, when columns were asked for, it was read).</param>
/// <param name="Record">The source record's asked-for columns; <see langword="null"/> when nothing was read.</param>
/// <param name="Warning">The one warning, or <see langword="null"/>.</param>
internal sealed record MappingSourceRead(bool Readable, Entity? Record, string? Warning)
{
    /// <summary>The ONE "no mapping" outcome — identical for a missing, denied, unmapped, probe-failed or unreadable source.</summary>
    internal static MappingSourceRead Unavailable { get; } = new(false, null, MappingSourceReadProbe.SourceUnavailableWarning);
}

/// <summary>The caller's Read decision on one record, as <see cref="MappingSourceReadProbe.CheckCallerReadAsync"/> reached it.</summary>
internal enum CallerReadDecision
{
    /// <summary>The caller holds Read.</summary>
    Allowed,

    /// <summary>The type is not in <see cref="EntityAccessFilter.TryResolveEntitySet"/>'s table (for a mapping source: nor the
    /// Document table), so its access cannot be evaluated.</summary>
    EntityTypeNotAuthorizable,

    /// <summary>The probe threw (not a cancellation of <c>ct</c>).</summary>
    ProbeFailed,

    /// <summary>The caller's rights do not include Read (also a missing record, a missing token, any "could not answer").</summary>
    InsufficientRights,
}

/// <summary>The caller's Read decision plus what the callers log with it.</summary>
/// <param name="Decision">The decision.</param>
/// <param name="EntitySet">The resolved entity set; empty for <see cref="CallerReadDecision.EntityTypeNotAuthorizable"/>.</param>
/// <param name="Rights">The rights the probe answered (<see cref="AccessRights.None"/> when it did not answer).</param>
/// <param name="Failure">The probe's exception for <see cref="CallerReadDecision.ProbeFailed"/>; for logging only.</param>
internal readonly record struct CallerReadCheck(
    CallerReadDecision Decision, string EntitySet, AccessRights Rights, Exception? Failure)
{
    /// <summary>True only for <see cref="CallerReadDecision.Allowed"/>.</summary>
    public bool IsAllowed => Decision == CallerReadDecision.Allowed;
}

/// <summary>
/// field-mapping-server-write-path-r1 task 003 (FR-04, NFR-01) — the ONE caller-Read decision, and the mapping-source read
/// that is gated by it.
/// </summary>
/// <remarks>
/// <para><b>One implementation, two consumers.</b> <see cref="CheckCallerReadAsync"/> is the sequence
/// <see cref="QuickCreateSourceAccessFilter"/> ran inline (spaarkeai-word-add-in-r1 task 030): resolve the entity set through
/// <see cref="EntityAccessFilter.TryResolveEntitySet"/> (a miss is NOT authorizable), ask
/// <see cref="CallerRecordAccessProbe"/> as the caller (OBO <c>RetrievePrincipalAccess</c>), then
/// <see cref="OperationAccessPolicy.HasRequiredRights"/> under the existing <c>read</c> key; a throw is a decision of its own and
/// a cancellation of <c>ct</c> propagates. The filter renders each decision as its unchanged 403; the mapping read below
/// collapses every non-<see cref="CallerReadDecision.Allowed"/> decision to one "no mapping" outcome.</para>
///
/// <para><b>Not an existence oracle.</b> A caller who cannot read the source gets <see cref="MappingSourceRead.Unavailable"/>:
/// no mapped values, one warning, no refusal. A missing record, a denied one, an unmapped type, a thrown probe and a failed
/// read all give that SAME object (same text, which names neither the record nor its type). The mapping read asks through
/// <see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>, so a denied record is re-asked on the
/// same schedule as an unknown one (task 064) and the two take the same work as well. The source is never read before
/// the probe answers Read.</para>
///
/// <para><b>System principals</b> read app-only with no probe (spec FR-04; ADR-028): there is no caller to ask about, and
/// the managed identity is never used to answer a caller's Read question.</para>
///
/// <para><b>Residual (carried from task 030 §9, not fixed here).</b> Read is a RECORD-level check; the app-only read does not
/// apply column-level (field-level security) masking, so a secured column named in an admin-authored profile is still
/// copied.</para>
///
/// <para>ADR-010: static, no DI registration, no interface.</para>
/// </remarks>
internal static class MappingSourceReadProbe
{
    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record (<see cref="AccessRights.Read"/>).</summary>
    internal const string ReadOperation = "read";

    /// <summary>
    /// The ONE warning for a source the principal may not map from. Names no record, no id and no type, and reads the same
    /// whether the source is missing, invisible to the caller, of an unauthorizable type, or the check or read failed.
    /// </summary>
    internal const string SourceUnavailableWarning =
        "The related record is not available to you, so no fields were copied from it.";

    /// <summary>The Document table's logical name: the one mapping source outside <see cref="EntityAccessFilter"/>'s map (task 043).</summary>
    internal const string DocumentLogicalName = "sprk_document";

    /// <summary>True when <paramref name="entityLogicalName"/> names the Document table (trimmed, any case, as the map compares).</summary>
    internal static bool IsDocumentSource(string? entityLogicalName) =>
        string.Equals(entityLogicalName?.Trim(), DocumentLogicalName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Task 043 (owner 2026-11-04): THE entity set of a field-mapping source, shared by the three mapping-source readers
    /// (<see cref="CheckCallerReadOfSourceAsync"/>, <see cref="MappingSourceCallerMask.ApplyAsync"/> and the push's source
    /// check). <see cref="EntityAccessFilter.TryResolveEntitySet"/> first, so every table in that map resolves exactly as
    /// before; then <c>sprk_document</c> to the Document access path's set (<see cref="FinanceAuthorizationFilter.DocumentEntitySet"/>).
    /// Any other table is a miss. The association lockstep map itself is unchanged: upload, record-route and Compose
    /// authorization still find no document entry in it.
    /// </summary>
    internal static bool TryResolveSourceEntitySet(string? entityLogicalName, out string entitySet)
    {
        if (EntityAccessFilter.TryResolveEntitySet(entityLogicalName, out entitySet))
        {
            return true;
        }

        if (IsDocumentSource(entityLogicalName))
        {
            entitySet = FinanceAuthorizationFilter.DocumentEntitySet;
            return true;
        }

        entitySet = string.Empty;
        return false;
    }

    /// <summary>
    /// The caller's Read decision on (<paramref name="entityLogicalName"/>, <paramref name="recordId"/>) — the shared
    /// sequence (entity-set resolve → caller-rights probe → <c>HasRequiredRights(rights, "read")</c>) with its exception
    /// handling. Never throws except a cancellation of <paramref name="ct"/>.
    /// </summary>
    /// <param name="probe">The caller-rights probe.</param>
    /// <param name="callerBearerToken">The caller's bearer token (a missing one answers <see cref="AccessRights.None"/>).</param>
    /// <param name="entityLogicalName">The record's logical name (or an alias the entity-set table knows).</param>
    /// <param name="recordId">The record.</param>
    /// <remarks>Always asks through <see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>, so an
    /// unknown id and a denied one take the same work (#2137). There is deliberately no non-equalised mode: every caller
    /// turns a refusal into one uniform denial, and an option would let the next caller reopen the timing oracle.</remarks>
    /// <param name="ct">Cancellation; its cancellation propagates rather than being read as a decision.</param>
    internal static async Task<CallerReadCheck> CheckCallerReadAsync(
        CallerRecordAccessProbe probe,
        string? callerBearerToken,
        string? entityLogicalName,
        Guid recordId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (!EntityAccessFilter.TryResolveEntitySet(entityLogicalName, out var entitySet))
        {
            return new CallerReadCheck(CallerReadDecision.EntityTypeNotAuthorizable, string.Empty, AccessRights.None, null);
        }

        return await AskReadAsync(probe, callerBearerToken, entitySet, recordId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 043: <see cref="CheckCallerReadAsync"/> for a FIELD-MAPPING SOURCE: the same decision over
    /// <see cref="TryResolveSourceEntitySet"/>, so a <c>sprk_document</c> is asked on the Document access path's set. The
    /// quick-create filter keeps <see cref="CheckCallerReadAsync"/>, so its responses are unchanged (NFR-07). Never throws
    /// except a cancellation of <paramref name="ct"/>.
    /// </summary>
    internal static async Task<CallerReadCheck> CheckCallerReadOfSourceAsync(
        CallerRecordAccessProbe probe,
        string? callerBearerToken,
        string? entityLogicalName,
        Guid recordId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (!TryResolveSourceEntitySet(entityLogicalName, out var entitySet))
        {
            return new CallerReadCheck(CallerReadDecision.EntityTypeNotAuthorizable, string.Empty, AccessRights.None, null);
        }

        return await AskReadAsync(probe, callerBearerToken, entitySet, recordId, ct).ConfigureAwait(false);
    }

    /// <summary>The probe, then <c>HasRequiredRights(rights, "read")</c>, on a resolved entity set (the shared second half).</summary>
    private static async Task<CallerReadCheck> AskReadAsync(
        CallerRecordAccessProbe probe, string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct)
    {
        AccessRights rights;
        try
        {
            rights = await probe.GetCallerRightsForAuthorizationGateAsync(callerBearerToken, entitySet, recordId, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The DECISION only: the caller decides what a failed check means (the filter denies, the mapping skips).
            return new CallerReadCheck(CallerReadDecision.ProbeFailed, entitySet, AccessRights.None, ex);
        }

        return OperationAccessPolicy.HasRequiredRights(rights, ReadOperation)
            ? new CallerReadCheck(CallerReadDecision.Allowed, entitySet, rights, null)
            : new CallerReadCheck(CallerReadDecision.InsufficientRights, entitySet, rights, null);
    }

    /// <summary>
    /// Read <paramref name="columns"/> of <paramref name="source"/> for field mapping, as <paramref name="reader"/> may.
    /// A caller is probed for Read FIRST and the record is read app-only only after the probe answers Read; a system
    /// principal reads app-only with no probe. Never throws except a cancellation of <paramref name="ct"/>, and never
    /// refuses: every failure is <see cref="MappingSourceRead.Unavailable"/>.
    /// </summary>
    /// <param name="reader">Who reads.</param>
    /// <param name="source">The resolved source (<see cref="MappingSourceResolver"/>).</param>
    /// <param name="columns">The profile's source columns (<see cref="CreateTimeFieldMapping.CollectSourceColumns"/>); when
    /// empty (a Default-only profile) the caller is still probed but nothing is read.</param>
    /// <param name="appOnly">The app-only entity reader the owner already holds.</param>
    /// <param name="logger">Optional logger; the warning returned never carries what is logged.</param>
    /// <param name="ct">Cancellation.</param>
    internal static async Task<MappingSourceRead> ReadAsync(
        MappingSourceReader reader,
        MappingSource source,
        IReadOnlyCollection<string> columns,
        IGenericEntityService appOnly,
        ILogger? logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(appOnly);

        if (reader.Probe is { } probe)
        {
            // Task 043 review (K1): a route that carries no document decision can never map from a document, so the
            // caller-rights probe is not asked. The answer depends only on the route and the table, never on the record.
            if (IsDocumentSource(source.EntityLogicalName) && reader.DocumentShown is null)
            {
                logger?.LogInformation(
                    "[FIELD-MAPPING-SOURCE] This create carries no document decision for the caller; a document source maps nothing.");
                return MappingSourceRead.Unavailable;
            }

            var check = await CheckCallerReadOfSourceAsync(
                    probe, reader.CallerBearerToken, source.EntityLogicalName, source.RecordId, ct)
                .ConfigureAwait(false);

            if (!check.IsAllowed)
            {
                if (check.Failure is { } failure)
                {
                    logger?.LogWarning(failure,
                        "[FIELD-MAPPING-SOURCE] The caller-rights probe threw for {EntityType}({RecordId}); no mapping.",
                        source.EntityLogicalName, source.RecordId);
                }
                else
                {
                    logger?.LogInformation(
                        "[FIELD-MAPPING-SOURCE] Caller may not read {EntityType}({RecordId}) ({Decision}, holds {Rights}); no mapping.",
                        source.EntityLogicalName, source.RecordId, check.Decision, check.Rights);
                }

                return MappingSourceRead.Unavailable;
            }

            // Task 043 (UAC-r2 task 188): a document is also put to THE external document narrowing, as the route decided it,
            // before anything of it is read app-only. Not shown, no answer and a fault are the same Unavailable.
            if (IsDocumentSource(source.EntityLogicalName)
                && !await IsDocumentShownAsync(reader, source.RecordId, logger, ct).ConfigureAwait(false))
            {
                return MappingSourceRead.Unavailable;
            }
        }

        if (columns.Count == 0)
        {
            return new MappingSourceRead(true, null, null);
        }

        Entity? record;
        try
        {
            record = await appOnly
                .RetrieveAsync(source.EntityLogicalName, source.RecordId, columns.ToArray(), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[FIELD-MAPPING-SOURCE] Source {EntityType}({RecordId}) could not be read (system={IsSystem}); no mapping.",
                source.EntityLogicalName, source.RecordId, reader.IsSystem);
            return MappingSourceRead.Unavailable;
        }

        return record is null ? MappingSourceRead.Unavailable : new MappingSourceRead(true, record, null);
    }

    /// <summary>
    /// The route's answer to the external document narrowing for <paramref name="documentId"/>
    /// (<see cref="MappingSourceReader.DocumentShown"/>): <see langword="false"/> when the route gave none, when it answers
    /// "not shown", and when it throws. A cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    private static async Task<bool> IsDocumentShownAsync(
        MappingSourceReader reader, Guid documentId, ILogger? logger, CancellationToken ct)
    {
        if (reader.DocumentShown is not { } documentShown)
        {
            // Unreachable since review round 1 (ReadAsync returns before the probe when there is no decision); kept as defence in depth.
            logger?.LogInformation(
                "[FIELD-MAPPING-SOURCE] This create carries no document decision for the caller; a document source maps nothing.");
            return false;
        }

        try
        {
            if (await documentShown(documentId, ct).ConfigureAwait(false))
            {
                return true;
            }

            logger?.LogInformation(
                "[FIELD-MAPPING-SOURCE] The external document narrowing does not show the document source; no mapping.");
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[FIELD-MAPPING-SOURCE] The document decision faulted; no mapping (fail closed).");
            return false;
        }
    }
}
