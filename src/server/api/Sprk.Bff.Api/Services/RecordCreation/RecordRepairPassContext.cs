using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// One run of the scheduled repair pass (field-mapping-server-write-path-r1 task 052b, spec FR-25): the rule a row must meet
/// for the pass to write it, and the pass's column-metadata cache. Handed to <see cref="RecordCreationService.RepairAsync"/>,
/// which asks <see cref="RowOutsideThePass"/> over the row it read before it decides anything, and reads column metadata
/// through <see cref="TryGetColumns"/> / <see cref="KeepColumns"/>.
/// </summary>
/// <remarks>
/// <para><b>Forward-only, defence in depth.</b> The pass's query bounds <c>createdon</c> and <c>createdby</c>; the job asks this
/// rule again over each scanned row; Repair asks it once more over the row it read, before any write. A row whose
/// <c>createdon</c> or <c>createdby</c> cannot be read is outside the pass (it cannot be shown to be inside).</para>
/// <para><b>The metadata cache</b> (task 052a's open point): a table's column metadata is read once per pass. Only a successful,
/// non-empty read is kept; a fault or an empty answer is never kept and is asked again by the next row. The cache lives as long
/// as the pass's one run (UAC-r2 condition 1, "read per pass, or cached briefly").</para>
/// <para>Component Justification (CLAUDE.md §11): (1) Existing — <see cref="MappingColumnMetadata.ReadAsync"/> reads, and does not
/// keep; <c>MetadataService</c>'s 6 h shared cache is a DataGrid contract and too long for <c>IsSecured</c> (052a notes). (2)
/// Extension — Repair's own read goes through this when a pass hands one; without it Repair is unchanged. (3) Cost of doing
/// nothing — the pass reads two whole-table metadata answers for every row it repairs, which 052a records as the condition
/// for shipping the pass; and the rule would be asked in one place only.</para>
/// </remarks>
internal sealed class RecordRepairPassContext
{
    internal const string CreatedOnColumn = "createdon";
    internal const string CreatedByColumn = "createdby";

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, ColumnMetadata>> _columns =
        new(StringComparer.OrdinalIgnoreCase);

    private int _metadataReads;

    /// <param name="goLive">The configured go-live instant: no row created before it is written.</param>
    /// <param name="productPrincipals">The <c>systemuser</c> ids whose rows the product created (task 050's marker).</param>
    internal RecordRepairPassContext(DateTimeOffset goLive, IReadOnlySet<Guid> productPrincipals)
    {
        ArgumentNullException.ThrowIfNull(productPrincipals);
        GoLive = goLive;
        ProductPrincipals = productPrincipals;
    }

    internal DateTimeOffset GoLive { get; }

    internal IReadOnlySet<Guid> ProductPrincipals { get; }

    /// <summary>How many column-metadata reads the pass made (a read that faulted counts).</summary>
    internal int MetadataReads => Volatile.Read(ref _metadataReads);

    /// <summary>
    /// <c>null</c> when the pass may write <paramref name="row"/>; otherwise why not, as a <see cref="RecordRepairReason"/> code:
    /// created before go-live, or with no readable <c>createdon</c>
    /// (<see cref="RecordRepairReason.RowCreatedBeforeGoLive"/>); with no readable <c>createdby</c>
    /// (<see cref="RecordRepairReason.RowCreatorUnknown"/>); created by a product principal
    /// (<see cref="RecordRepairReason.RowCreatedByProduct"/>). Pure.
    /// </summary>
    internal string? RowOutsideThePass(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (CreatedOnOf(row) is not { } createdOn || createdOn < GoLive)
            return RecordRepairReason.RowCreatedBeforeGoLive;

        if (!row.Attributes.TryGetValue(CreatedByColumn, out var value)
            || value is not EntityReference { Id: var creator }
            || creator == Guid.Empty)
        {
            return RecordRepairReason.RowCreatorUnknown;
        }

        return ProductPrincipals.Contains(creator) ? RecordRepairReason.RowCreatedByProduct : null;
    }

    /// <summary>The row's <c>createdon</c> as UTC, or <c>null</c> when it is absent or not a date.</summary>
    internal static DateTimeOffset? CreatedOnOf(Entity row) =>
        row.Attributes.TryGetValue(CreatedOnColumn, out var value) && value is DateTime when
            ? new DateTimeOffset(when.Kind switch
            {
                DateTimeKind.Local => when.ToUniversalTime(),
                // The SDK returns UTC; an unspecified kind is read as UTC, as the SDK writes it.
                _ => DateTime.SpecifyKind(when, DateTimeKind.Utc),
            })
            : null;

    /// <summary>The columns of <paramref name="table"/> this pass already read, if any.</summary>
    internal bool TryGetColumns(string table, out IReadOnlyDictionary<string, ColumnMetadata> columns) =>
        _columns.TryGetValue(table, out columns!);

    /// <summary>Counts one metadata read (kept or not).</summary>
    internal void CountMetadataRead() => Interlocked.Increment(ref _metadataReads);

    /// <summary>Keeps a successful, non-empty read for the rest of the pass.</summary>
    internal void KeepColumns(string table, IReadOnlyDictionary<string, ColumnMetadata> columns)
    {
        if (columns.Count > 0)
            _columns[table] = columns;
    }

    // ── Review F1-a: the Assign cascades of a table, read once per pass ─────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, IReadOnlyList<AssignCascadeRelationship>> _assignCascades =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _assignCascadeRefusalsLogged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Assign cascades of <paramref name="table"/> this pass already read, if any.</summary>
    internal bool TryGetAssignCascades(string table, out IReadOnlyList<AssignCascadeRelationship> cascades) =>
        _assignCascades.TryGetValue(table, out cascades!);

    /// <summary>Keeps a successful read (an empty one included: nothing cascades) for the rest of the pass.</summary>
    internal void KeepAssignCascades(string table, IReadOnlyList<AssignCascadeRelationship> cascades) =>
        _assignCascades[table] = cascades;

    private readonly ConcurrentDictionary<string, byte> _assignCascadesUnknown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Review K: <c>true</c> when this pass's read of <paramref name="table"/>'s relationships failed — still unknown for
    /// the rest of the pass (not asked again per row); the rows are retried next run, which reads it afresh.</summary>
    internal bool AssignCascadesUnknown(string table) => _assignCascadesUnknown.ContainsKey(table);

    /// <summary>Keeps a failed read as unknown for the rest of the pass; <c>true</c> the first time (log it once).</summary>
    internal bool KeepAssignCascadesUnknown(string table) => _assignCascadesUnknown.TryAdd(table, 0);

    /// <summary><c>true</c> the first time this pass refuses an owner write of <paramref name="table"/> for its cascades.</summary>
    internal bool FirstAssignCascadeRefusal(string table) => _assignCascadeRefusalsLogged.TryAdd(table, 0);
}
