using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 018 (spec FR-04 as amended 2026-11-08, owner ruling "option A widened"): whether a Work Item is
// privilege-flagged, derived at read time from the communications it is about, AS THE CALLER.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: task 015's WorklistItemExtras.PrivilegeFlagged read sprk_signal.sprk_privilegeflagged, which nothing writes (ontology
//     DI-64). The D-142 witness read (SignalReadService.WitnessesTheCallerReadsAsync) and EarlierInvoiceReadability are batched caller
//     reads of other columns; CommunicationAccessFilter reads sprk_privilegeclassification to decide one thread row's visibility.
//   Extension: the D-142 read is private, selects only the key and is keyed on lineage tables; EarlierInvoiceReadability judges invoices
//     only; CommunicationAccessFilter decides visibility per row, never a flag per Work Item. This is the one rule (subject + witness
//     communications, fail closed) in the same batched shape, public so ontology DI-143 does not copy it.
//   Cost of doing nothing: every Decide-lane summary reads PrivilegeFlagged = false, so the model-visible tool (task 022) reduces
//     nothing and a privileged communication's subject and title reach the model (FR-04, ADR-015).
//
// PLACEMENT (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). The decision slice's data owner is the BFF (Signals are
//   readable only through BFF routes); no package, no AI-internal type, no background work; scoped, registered unconditionally in
//   SignalsModule like the services that use it.
// =====================================================================================================================

/// <summary>
/// What a Work Item's privilege flag is derived from (<see cref="WorkItemPrivilege.SourcesOf"/>): the <c>sprk_communication</c> ids it
/// is about (its subject, when the subject is a communication, and every witness or evidence communication), or
/// <see cref="Undetermined"/> when that cannot be told from what was stored.
/// </summary>
/// <param name="CommunicationIds">The distinct communications whose <c>sprk_privilegeclassification</c> decides the flag. Empty when
/// the item names none.</param>
/// <param name="Undetermined">The item's sources cannot be told (a communication subject with no id, stored evidence that is not a JSON
/// array, a communication reference that is not a GUID): the item is flagged without a read (fail closed).</param>
public sealed record WorkItemPrivilegeSources(IReadOnlyList<Guid> CommunicationIds, bool Undetermined)
{
    /// <summary>An item about no communication: never flagged, and never read.</summary>
    public static readonly WorkItemPrivilegeSources None = new([], false);

    /// <summary>An item whose sources cannot be told: always flagged, never read.</summary>
    public static readonly WorkItemPrivilegeSources Unknown = new([], true);
}

/// <summary>
/// FR-04 (owner ruling 2026-11-08, option A widened): a Work Item is <b>privilege-flagged</b> when its subject, when the subject is a
/// <c>sprk_communication</c>, or any of its witness or evidence communications has <c>sprk_communication.sprk_privilegeclassification</c>
/// = Privileged or Potentially Privileged (<see cref="CommunicationPrivilegeClassification"/>). None and null are not flagged.
/// </summary>
/// <remarks>
/// <para><b>As the caller, fail closed.</b> The classification is read with the caller's own <see cref="IDataverseUserClient"/>; there
/// is no app-only fallback. A communication the caller's read does not return (no Read on the row, or a 403 on the table) flags its
/// item. A FAULT (an exception, a 5xx, a 401, throttling, an unparseable body, a body that carries <c>@odata.nextLink</c>) flags every
/// item that needed the read. An unrecognised option value flags its item. <see cref="WorkItemPrivilegeSources.Undetermined"/> flags
/// without a read.</para>
/// <para><b>Bounded.</b> One batched read per call, in In lists of at most <see cref="ChunkSize"/> (200) distinct ids, shaped like the
/// D-142 witness read (<c>SignalReadService.WitnessesTheCallerReadsAsync</c>); none when no item names a communication. The Decide-lane
/// page (<see cref="DecisionCapability.ListDecidePageAsync"/>) calls it once per page.</para>
/// <para><b>Never a read gate.</b> Privilege never gates a read on its own (owner ruling 2026-07-16): this returns a flag and drops,
/// adds or reorders nothing. The flag only reduces what the model sees (task 022). Only the id and the classification are read, and
/// nothing read is returned but the flag (ADR-015, minimum text).</para>
/// <para><b>Reuse (ontology DI-143, #2630).</b> The Decision Record snapshot reuses this rule rather than copying it: build each item's
/// sources with <see cref="SourcesOf"/> (the Signal's subject table and id, and its stored <c>sprk_evidencerefs</c>) and call
/// <see cref="FlaggedAsync{TKey}"/> with the deciding caller's client. <c>sprk_signal.sprk_privilegeflagged</c> is never a source
/// (ontology DI-64: no production writer).</para>
/// </remarks>
public sealed class WorkItemPrivilege
{
    /// <summary>Ids per batched caller read (In lists of at most 200, as the D-142 witness read).</summary>
    internal const int ChunkSize = 200;

    internal const string CommunicationTable = "sprk_communication";
    internal const string CommunicationEntitySet = "sprk_communications";
    internal const string CommunicationKey = "sprk_communicationid";
    internal const string ClassificationColumn = "sprk_privilegeclassification";

    private readonly ILogger<WorkItemPrivilege> _logger;

    public WorkItemPrivilege(ILogger<WorkItemPrivilege> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// The sources of one Work Item: <paramref name="subjectId"/> when <paramref name="subjectTable"/> is <c>sprk_communication</c>
    /// or unknown, plus every evidence line whose <c>ref</c> is <c>sprk_communication:{id}</c> (as the evaluator writes witness refs). Never reads.
    /// </summary>
    /// <remarks><b>Untyped subjects.</b> <c>sprk_communication</c> has no record-type catalog row (DI-63), so a communication subject is an
    /// UNTYPED pair (<c>SignalWriter</c>: id and name, no <c>sprk_regardingrecordtype</c>). An untyped subject id is therefore read as a
    /// communication: when the caller's read returns it, its classification decides; when it does not (an unreadable communication, or a row
    /// of another table), the item is flagged, the same rule as any communication the caller cannot read.</remarks>
    /// <param name="subjectTable">The subject's table (logical name, from the record-type catalog); null when untyped or not cataloged.</param>
    /// <param name="subjectId">The subject's id; null when the item has none.</param>
    /// <param name="storedEvidence">The item's stored evidence lines (a JSON array), as stored; null or blank when none.</param>
    /// <returns><see cref="WorkItemPrivilegeSources.Unknown"/> when a communication subject has no id, the evidence is not a JSON array,
    /// or a communication line or reference does not name a communication by GUID.</returns>
    public static WorkItemPrivilegeSources SourcesOf(string? subjectTable, Guid? subjectId, string? storedEvidence)
    {
        var ids = new List<Guid>();
        var communicationSubject = string.Equals(subjectTable, CommunicationTable, StringComparison.OrdinalIgnoreCase);
        if (subjectId is { } subject && subject != Guid.Empty)
        {
            // A typed communication, or an untyped pair (a communication has no catalog row): read as a communication.
            if (communicationSubject || string.IsNullOrWhiteSpace(subjectTable))
            {
                ids.Add(subject);
            }
        }
        else if (communicationSubject)
        {
            return WorkItemPrivilegeSources.Unknown; // a communication subject whose id cannot be read: fail closed
        }

        if (!string.IsNullOrWhiteSpace(storedEvidence))
        {
            try
            {
                using var doc = JsonDocument.Parse(storedEvidence);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return WorkItemPrivilegeSources.Unknown;
                }

                foreach (var line in doc.RootElement.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // A line of kind "communication" must name its communication; one that does not cannot be judged (fail closed).
                    var communicationLine = line.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                        && string.Equals(kind.GetString(), "communication", StringComparison.OrdinalIgnoreCase);
                    var text = line.TryGetProperty("ref", out var reference) && reference.ValueKind == JsonValueKind.String ? reference.GetString() : null;
                    var separator = text?.IndexOf(':', StringComparison.Ordinal) ?? -1;
                    if (separator <= 0 || !string.Equals(text![..separator].Trim(), CommunicationTable, StringComparison.OrdinalIgnoreCase))
                    {
                        if (communicationLine)
                        {
                            return WorkItemPrivilegeSources.Unknown;
                        }

                        continue;
                    }

                    if (!Guid.TryParse(text[(separator + 1)..], out var id) || id == Guid.Empty)
                    {
                        return WorkItemPrivilegeSources.Unknown; // names a communication, but which one cannot be told
                    }

                    ids.Add(id);
                }
            }
            catch (JsonException)
            {
                return WorkItemPrivilegeSources.Unknown;
            }
        }

        return ids.Count == 0 ? WorkItemPrivilegeSources.None : new WorkItemPrivilegeSources(ids.Distinct().ToList(), false);
    }

    /// <summary>
    /// The keys of the <paramref name="items"/> that are privilege-flagged, judged with ONE batched read of their communications'
    /// classification as the caller (chunks of <see cref="ChunkSize"/> distinct ids). Never throws but for cancellation.
    /// </summary>
    /// <typeparam name="TKey">The caller's item key (a Signal id for the Decide lane; whatever DI-143's snapshot keys by).</typeparam>
    /// <param name="caller">The caller's Dataverse client: every read here is the caller's (no app-only fallback).</param>
    /// <param name="items">Each item and its sources (<see cref="SourcesOf"/>).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<IReadOnlySet<TKey>> FlaggedAsync<TKey>(
        IDataverseUserClient caller, IReadOnlyDictionary<TKey, WorkItemPrivilegeSources> items, CancellationToken ct)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(items);

        var flagged = new HashSet<TKey>();
        var toRead = new List<KeyValuePair<TKey, WorkItemPrivilegeSources>>();
        foreach (var item in items)
        {
            if (item.Value is null || item.Value.Undetermined)
            {
                flagged.Add(item.Key);
            }
            else if (item.Value.CommunicationIds.Count > 0)
            {
                toRead.Add(item);
            }
        }

        if (toRead.Count == 0)
        {
            return flagged;
        }

        var privileged = await PrivilegedOrUnreadableAsync(caller, toRead.SelectMany(i => i.Value.CommunicationIds).Distinct(), ct)
            .ConfigureAwait(false);
        foreach (var (key, sources) in toRead)
        {
            if (privileged is null || sources.CommunicationIds.Any(privileged.Contains))
            {
                flagged.Add(key);
            }
        }

        return flagged;
    }

    /// <summary>
    /// The <paramref name="ids"/> that flag: classified Privileged, Potentially Privileged or an unrecognised value, or not returned by
    /// the caller's read. Null on a fault (every id flags).
    /// </summary>
    private async Task<IReadOnlySet<Guid>?> PrivilegedOrUnreadableAsync(IDataverseUserClient caller, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var flags = new HashSet<Guid>();
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            DataverseUserResponse response;
            try
            {
                response = await caller.GetAsync(
                    $"{CommunicationEntitySet}?$select={CommunicationKey},{ClassificationColumn}&$filter={SignalReadService.InFilter(CommunicationKey, chunk)}",
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Work Item privilege: the caller's read of {Table} faulted ({ExceptionType}); every item that names one is flagged (FR-04, fail closed).",
                    CommunicationTable, ex.GetType().Name);
                return null;
            }

            if (response.IsSuccess
                && response.Body is { ValueKind: JsonValueKind.Object } body
                && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                && !body.TryGetProperty("@odata.nextLink", out _))
            {
                var returned = new HashSet<Guid>();
                foreach (var row in value.EnumerateArray())
                {
                    if (EarlierInvoiceReadability.ReadGuid(row, CommunicationKey) is not { } id || !chunk.Contains(id))
                    {
                        continue;
                    }

                    returned.Add(id);
                    if (IsFlagging(row))
                    {
                        flags.Add(id);
                    }
                }

                flags.UnionWith(chunk.Where(id => !returned.Contains(id))); // not returned to the caller: unreadable, flagged
                continue;
            }

            // A 403 is "no Read on the table": none of these is readable, so each flags. A 401 is the same mapped code but means the
            // caller's token failed mid-request: a fault, not a verdict.
            if (!response.IsSuccess && response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && response.StatusCode != 401)
            {
                flags.UnionWith(chunk);
                continue;
            }

            _logger.LogWarning(
                "Work Item privilege: the caller's read of {Table} failed ({ErrorCode}); every item that names one is flagged (FR-04, fail closed).",
                CommunicationTable, response.IsSuccess ? "unparseable" : response.ErrorCode);
            return null;
        }

        return flags;
    }

    /// <summary>Privileged, Potentially Privileged, or a value that is not None: flagged. Absent or null: not flagged.</summary>
    private static bool IsFlagging(JsonElement row)
    {
        if (!row.TryGetProperty(ClassificationColumn, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return !(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var option)
            && option == (int)CommunicationPrivilegeClassification.None);
    }
}
