using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>Where a resolved mapping source came from (FR-03).</summary>
internal enum MappingSourceOrigin
{
    /// <summary>No source: nothing was named and the row is filed under no parent. A silent no-op.</summary>
    None,

    /// <summary>The caller named the source (<c>mappingSource</c>: entity + id).</summary>
    Explicit,

    /// <summary>Inferred from the row's ADR-024 regarding pair.</summary>
    RegardingPair,

    /// <summary>Inferred from the row's single typed parent lookup.</summary>
    TypedParent,

    /// <summary>Something was named but no one source can be told from it: no source, with a warning.</summary>
    Undetermined,
}

/// <summary>The resolver's answer: the source (or none), where it came from, and at most one warning.</summary>
internal sealed record MappingSourceResolution(MappingSource? Source, MappingSourceOrigin Origin, string? Warning);

/// <summary>
/// field-mapping-server-write-path-r1 task 003 (FR-03) — which record a new row inherits its mapped fields from.
/// </summary>
/// <remarks>
/// <para><b>Precedence</b> (spec FR-03, owner Q7, ADR-024):</para>
/// <list type="number">
/// <item>The explicit <c>mappingSource</c> (entity + id) when the caller names one. It always wins; a half-named or malformed
/// one is <see cref="MappingSourceOrigin.Undetermined"/>, never a cue to infer some other record.</item>
/// <item>Else the ADR-024 regarding pair (<see cref="SecureRootInheritance.PairIdColumn"/> +
/// <see cref="SecureRootInheritance.PairTypeColumn"/>). With a type, the record-type catalog names its table; without one,
/// only a typed parent lookup of the row with the same id says what it names (the regarding builders' shape). A pair that
/// names nothing that can be told is <see cref="MappingSourceOrigin.Undetermined"/> — it never falls through to a typed
/// lookup that may name a different record.</item>
/// <item>Else the row's single typed parent lookup — the filed-under parents as
/// <see cref="RecordOwnershipContext.ParentsOf(IEnumerable{KeyValuePair{string, object}}, string?)"/> defines them (the
/// same lookups the owner rule reads; citations excluded). Several distinct parents are
/// <see cref="MappingSourceOrigin.Undetermined"/>; none is <see cref="MappingSourceOrigin.None"/>.</item>
/// </list>
/// <para><b>Decided from the payload and the catalog only</b> — never from the record the source names — so no answer here
/// depends on whether that record exists or whom it is visible to. Read authorization on the source is
/// <see cref="MappingSourceReadProbe.ReadAsync"/>'s, after this. Every warning is one constant that echoes no caller value.
/// Never throws except a cancellation of <c>ct</c>.</para>
/// <para>ADR-010: static, no DI registration, no interface. The catalog read is the owner's (a delegate), so this stays
/// free of I/O.</para>
/// </remarks>
internal static class MappingSourceResolver
{
    /// <summary>The ONE warning for "something was named, but no single source can be told from it". Echoes nothing.</summary>
    internal const string SourceUndeterminedWarning =
        "The record to copy fields from could not be determined, so no fields were copied.";

    private static readonly Regex LogicalName = new(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant);

    private static readonly MappingSourceResolution NoSource = new(null, MappingSourceOrigin.None, null);

    private static readonly MappingSourceResolution Undetermined =
        new(null, MappingSourceOrigin.Undetermined, SourceUndeterminedWarning);

    /// <summary>Resolve the mapping source of a <paramref name="table"/> row being created.</summary>
    /// <param name="table">The new row's table (logical name) — it scopes which lookups cite rather than file.</param>
    /// <param name="explicitEntityLogicalName">The caller's <c>mappingSource</c> entity, if any.</param>
    /// <param name="explicitRecordId">The caller's <c>mappingSource</c> id, if any.</param>
    /// <param name="fields">The CALLER's payload as SDK attributes (lookups as <see cref="EntityReference"/>, the pair id as
    /// text), before any mapped value is merged in.</param>
    /// <param name="recordTypeTable">The record-type catalog read: the table (<c>sprk_recordlogicalname</c>) a
    /// <c>sprk_recordtype_ref</c> id names, or <see langword="null"/>. A throw is read as "cannot be told".</param>
    /// <param name="ct">Cancellation.</param>
    internal static async Task<MappingSourceResolution> ResolveAsync(
        string table,
        string? explicitEntityLogicalName,
        Guid? explicitRecordId,
        IEnumerable<KeyValuePair<string, object>> fields,
        Func<Guid, CancellationToken, Task<string?>> recordTypeTable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(recordTypeTable);

        // 1 — explicit wins, whole or not at all.
        var namesEntity = !string.IsNullOrWhiteSpace(explicitEntityLogicalName);
        var namesId = explicitRecordId is { } id && id != Guid.Empty;
        if (namesEntity || namesId)
        {
            return namesEntity && namesId && Normalize(explicitEntityLogicalName) is { } explicitTable
                ? new MappingSourceResolution(new MappingSource(explicitTable, explicitRecordId!.Value), MappingSourceOrigin.Explicit, null)
                : Undetermined;
        }

        var row = fields as IReadOnlyCollection<KeyValuePair<string, object>> ?? fields.ToArray();
        var parents = RecordOwnershipContext.ParentsOf(row, table);

        // 2 — the ADR-024 regarding pair.
        var pair = PairOf(row);
        if (pair.Names)
        {
            if (pair.Id is not { } pairId)
                return Undetermined;

            if (pair.TypeId is not { } typeId)
            {
                // No type: only a typed parent lookup with the same id says what it names.
                var agreeing = parents.Where(p => p.RecordId == pairId).ToArray();
                return agreeing.Length == 1
                    ? new MappingSourceResolution(
                        new MappingSource(agreeing[0].EntityLogicalName.ToLowerInvariant(), pairId), MappingSourceOrigin.RegardingPair, null)
                    : Undetermined;
            }

            string? pairTable;
            try
            {
                pairTable = Normalize(await recordTypeTable(typeId, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                pairTable = null;
            }

            return pairTable is null
                ? Undetermined
                : new MappingSourceResolution(new MappingSource(pairTable, pairId), MappingSourceOrigin.RegardingPair, null);
        }

        // 3 — the single typed parent lookup.
        return parents.Count switch
        {
            0 => NoSource,
            1 => new MappingSourceResolution(
                new MappingSource(parents[0].EntityLogicalName.ToLowerInvariant(), parents[0].RecordId), MappingSourceOrigin.TypedParent, null),
            _ => Undetermined,
        };
    }

    /// <summary>What the row's pair columns say: whether it names a record, its id (null when not a record id) and its type.</summary>
    private readonly record struct Pair(bool Names, Guid? Id, Guid? TypeId);

    private static Pair PairOf(IEnumerable<KeyValuePair<string, object>> row)
    {
        bool names = false;
        Guid? id = null;
        Guid? typeId = null;
        foreach (var (column, value) in row)
        {
            if (string.Equals(column, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase))
            {
                switch (value)
                {
                    case null:
                        break;
                    case string text when string.IsNullOrWhiteSpace(text):
                        break;
                    case string text:
                        names = true;
                        id = Guid.TryParse(text.Trim(), out var parsed) && parsed != Guid.Empty ? parsed : null;
                        break;
                    case Guid guid:
                        names = true;
                        id = guid == Guid.Empty ? null : guid;
                        break;
                    default:
                        names = true; // a value that is not a record id is read as invalid, never as absent
                        id = null;
                        break;
                }
            }
            else if (string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase)
                     && value is EntityReference type && type.Id != Guid.Empty)
            {
                typeId = type.Id;
            }
        }

        return new Pair(names, id, typeId);
    }

    /// <summary>A trimmed, lower-case logical name, or <see langword="null"/> when it is not one.</summary>
    private static string? Normalize(string? logicalName)
    {
        var name = logicalName?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(name) && LogicalName.IsMatch(name) ? name : null;
    }
}
