using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

// field-mapping-server-write-path-r1 task 032b-1 (design notes/task-032-design.md §5.3, notes/task-032b-design.md §10): the
// app-only mapping write gates, MOVED unchanged out of RecordCreationService.Repair.cs so a second app-only writer (032b-2's
// invoice step) reuses them instead of re-implementing them (UAC-r2 Q-U5: "reuse, not a re-implementation"). Repair calls them
// through thin private wrappers that keep its log lines and its outcomes; one copy, no mode, no new input.

/// <summary>What <see cref="MappingSourceCopyGate.DecideAsync"/> answered.</summary>
internal enum MappingSourceCopyVerdict
{
    /// <summary>The source is not secure, or the row is isolated with it: its data may be copied.</summary>
    Allowed,

    /// <summary>The source is secure (flagged, or owned by the Secure Record owner team) and the row is not isolated.</summary>
    Secure,

    /// <summary>Whether the source is secure cannot be told (<see cref="MappingSourceCopyDecision.Reason"/>): fail closed.</summary>
    Unknown,
}

/// <summary>The copy gate's answer. <see cref="Fault"/> is set only for <see cref="MappingSourceCopyGate.ReasonFault"/>.</summary>
internal sealed record MappingSourceCopyDecision(MappingSourceCopyVerdict Verdict, string? Reason = null, Exception? Fault = null)
{
    internal static MappingSourceCopyDecision Allowed { get; } = new(MappingSourceCopyVerdict.Allowed);

    internal static MappingSourceCopyDecision Secure { get; } = new(MappingSourceCopyVerdict.Secure);

    internal static MappingSourceCopyDecision Unknown(string reason, Exception? fault = null) =>
        new(MappingSourceCopyVerdict.Unknown, reason, fault);
}

/// <summary>
/// Review F1-e (ii), belt and braces over the owner gate: whether a mapping source's data may be copied into a row, decided
/// app-only from the source's own secure status (moved from Repair's <c>SourceMayBeCopiedAsync</c>, task 032b-1).
/// </summary>
internal static class MappingSourceCopyGate
{
    private const string OwnerColumn = "ownerid";

    /// <summary>The source's owner did not come back in the read.</summary>
    internal const string ReasonSourceOwnerMissing = "source_owner_missing";

    /// <summary>A team owns the source (or a flagged source meets a team-owned row) and the Secure Record owner team is missing.</summary>
    internal const string ReasonSecureTeamMissing = "secure_team_missing";

    /// <summary>As <see cref="ReasonSecureTeamMissing"/>, with the Secure Record owner team ambiguous.</summary>
    internal const string ReasonSecureTeamAmbiguous = "secure_team_ambiguous";

    /// <summary>A fault while deciding (the exception is <see cref="MappingSourceCopyDecision.Fault"/>).</summary>
    internal const string ReasonFault = "gate_fault";

    /// <summary>The source columns the gate reads to tell whether the source may be copied (review F1-e (ii)).</summary>
    internal static IEnumerable<string> SourceGateColumns(string sourceTable) =>
        SecureChildLineage.IsRoot(sourceTable) ? [OwnerColumn, AccessFollowsParent.SecureFlagColumn] : [OwnerColumn];

    /// <summary>
    /// Whether the source's data may be copied into the row. Not when the source is a secure-flagged root whose flag is not an
    /// explicit <c>false</c> (empty reads as flagged), or is owned by the Secure Record owner team, unless the row is owned by
    /// that Secure team. Anything that cannot be told — an owner that did not come back, a fault deciding the team, or (review
    /// pass 3) a Secure Record owner team that is missing or ambiguous when a team owns the source — is
    /// <see cref="MappingSourceCopyVerdict.Unknown"/> (fail closed). Cancellation is rethrown.
    /// </summary>
    /// <param name="sourceTable">The source's table.</param>
    /// <param name="read">The source row, read with at least <see cref="SourceGateColumns"/>.</param>
    /// <param name="rowOwner">The owner of the row the data would be copied into, or <c>null</c>.</param>
    /// <param name="secureOwnerTeam">The Secure Record owner team (<c>SecureChildShareSynchronizer.SecureOwnerTeamAsync</c>).</param>
    /// <param name="ct">Cancellation.</param>
    internal static async Task<MappingSourceCopyDecision> DecideAsync(
        string sourceTable,
        Entity read,
        EntityReference? rowOwner,
        Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> secureOwnerTeam,
        CancellationToken ct)
    {
        try
        {
            var flagged = SecureChildLineage.IsRoot(sourceTable)
                && read.GetAttributeValue<bool?>(AccessFollowsParent.SecureFlagColumn) != false;
            var sourceOwner = read.GetAttributeValue<EntityReference>(OwnerColumn);
            if (sourceOwner is null || sourceOwner.Id == Guid.Empty)
                return MappingSourceCopyDecision.Unknown(ReasonSourceOwnerMissing);

            var sourceTeamOwned = string.Equals(sourceOwner.LogicalName, "team", StringComparison.OrdinalIgnoreCase);
            var rowTeamOwned = rowOwner is { LogicalName: "team" } && rowOwner.Id != Guid.Empty;
            Guid? secureTeam = null;
            if (sourceTeamOwned || (flagged && rowTeamOwned))
            {
                // Review pass 3: a Secure Record owner team that is missing or ambiguous cannot tell a Secure-team owner
                // from any other, so the source's owner cannot be told: fail closed.
                var resolved = await secureOwnerTeam(ct).ConfigureAwait(false);
                if (resolved.TeamId is not { } known || known == Guid.Empty)
                {
                    return MappingSourceCopyDecision.Unknown(
                        resolved.Refusal is null ? ReasonSecureTeamMissing : ReasonSecureTeamAmbiguous);
                }

                secureTeam = known;
            }

            var sourceSecureTeam = sourceTeamOwned && sourceOwner.Id == secureTeam;
            if (!flagged && !sourceSecureTeam)
                return MappingSourceCopyDecision.Allowed;

            var isolated = rowTeamOwned
                && (sourceSecureTeam ? rowOwner!.Id == sourceOwner.Id : rowOwner!.Id == secureTeam);
            return isolated ? MappingSourceCopyDecision.Allowed : MappingSourceCopyDecision.Secure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MappingSourceCopyDecision.Unknown(ReasonFault, ex);
        }
    }
}

/// <summary>
/// Whether a mapped value fits its target column, from the column's metadata (task 052a; moved from Repair's
/// <c>TargetColumnValue</c> and its helpers, task 032b-1). The refusals are <see cref="RecordRepairReason"/> codes.
/// </summary>
internal static class MappedTargetFit
{
    /// <summary>
    /// The mapped <paramref name="value"/> as the SDK writes it to <paramref name="column"/> of the row's table, or why it is not
    /// written (task 052a; review F1, K4) — each a refusal the SDK would otherwise give for the WHOLE write:
    /// <list type="bullet">
    /// <item>the table has no such column, or an update may not set it (unknown counts as not):
    /// <see cref="RecordRepairReason.MappedColumnNotUpdatable"/>;</item>
    /// <item>the value is not of a kind the column takes — the create path's <see cref="RecordCreationService.Fits"/>, and for a
    /// number column a number that does not convert to the column's exact CLR type (<see cref="AsNumber"/>):
    /// <see cref="RecordRepairReason.MappedValueTypeMismatch"/>;</item>
    /// <item>text longer than the column: <see cref="RecordRepairReason.MappedValueTooLong"/>;</item>
    /// <item>a number outside the column's stated range, or a range not stated: <see cref="RecordRepairReason.MappedValueOutOfRange"/>;</item>
    /// <item>a lookup to a table the column does not take, or targets not stated: <see cref="RecordRepairReason.MappedLookupTableNotAccepted"/>;</item>
    /// <item>an option (or any option of a multi-select) the column does not have, or choices not stated:
    /// <see cref="RecordRepairReason.MappedOptionNotAccepted"/>.</item>
    /// </list>
    /// </summary>
    internal static (string? Refusal, object? Value) Check(
        IReadOnlyDictionary<string, ColumnMetadata> columns, string column, object value)
    {
        if (!columns.TryGetValue(column, out var metadata) || metadata.IsValidForUpdate != true)
            return (RecordRepairReason.MappedColumnNotUpdatable, null);
        if (metadata.AttributeType is not { } type)
            return (RecordRepairReason.MappedValueTypeMismatch, null);

        if (IsNumberType(type))
        {
            if (AsNumber(type, value) is not { } number)
                return (RecordRepairReason.MappedValueTypeMismatch, null);
            return InRange(metadata, number) ? (null, number) : (RecordRepairReason.MappedValueOutOfRange, null);
        }

        if (!RecordCreationService.Fits(type, value))
            return (RecordRepairReason.MappedValueTypeMismatch, null);

        return value switch
        {
            string text when metadata.MaxLength is { } max && text.Length > max => (RecordRepairReason.MappedValueTooLong, null),
            EntityReference reference when metadata.Targets is not { Count: > 0 } targets
                                           || !targets.Contains(reference.LogicalName, StringComparer.OrdinalIgnoreCase) =>
                (RecordRepairReason.MappedLookupTableNotAccepted, null),
            OptionSetValue option when metadata.OptionValues is not { } choices || !choices.Contains(option.Value) =>
                (RecordRepairReason.MappedOptionNotAccepted, null),
            OptionSetValueCollection options when metadata.OptionValues is not { } choices
                                                  || options.Count == 0 || options.Any(o => o is null || !choices.Contains(o.Value)) =>
                (RecordRepairReason.MappedOptionNotAccepted, null),
            _ => (null, value),
        };
    }

    private static bool IsNumberType(string type) => type is "Integer" or "BigInt" or "Decimal" or "Double" or "Money";

    /// <summary>
    /// A mapped number as the exact CLR type the SDK takes for a column of <paramref name="type"/> — Integer: <see cref="int"/>
    /// (a whole number that fits), BigInt: <see cref="long"/> (likewise), Decimal: <see cref="decimal"/>, Double:
    /// <see cref="double"/>, Money: <see cref="Money"/> (#2124: a Copy keeps the source's value, so a decimal or a Money's
    /// amount must be converted here) — or <c>null</c> when the value is not a number or does not convert. Review K4: a conversion
    /// that overflows is <c>null</c> too, so one extreme number skips one column, never the whole mapping.
    /// </summary>
    private static object? AsNumber(string type, object value)
    {
        decimal? exact;
        try
        {
            exact = value switch
            {
                int whole => whole,
                long big => big,
                decimal number => number,
                Money money => money.Value,
                double real when double.IsFinite(real) => (decimal)real,
                _ => null,
            };
        }
        catch (OverflowException)
        {
            return null; // a double no decimal holds
        }

        if (exact is not { } amount)
            return null;

        try
        {
            return type switch
            {
                "Integer" when decimal.Truncate(amount) == amount && amount is >= int.MinValue and <= int.MaxValue => (int)amount,
                "BigInt" when decimal.Truncate(amount) == amount && amount is >= long.MinValue and <= long.MaxValue => (long)amount,
                "Decimal" => amount,
                "Double" => value is double real ? real : (double)amount,
                "Money" => new Money(amount),
                _ => null,
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>Whether a converted number lies within its column's stated range; a range not stated is not (fail closed).</summary>
    private static bool InRange(ColumnMetadata metadata, object number)
    {
        if (metadata.MinValue is not { } min || metadata.MaxValue is not { } max)
            return false;

        return number switch
        {
            int whole => whole >= min && whole <= max,
            long big => big >= min && big <= max,
            decimal amount => amount >= min && amount <= max,
            Money money => money.Value >= min && money.Value <= max,
            double real => real >= (double)min && real <= (double)max,
            _ => false,
        };
    }
}

/// <summary>A table's column metadata read: <see cref="Columns"/>, or <c>null</c> with the <see cref="Fault"/> (a failed read)
/// or without one (an empty answer, which cannot be told from a failed read).</summary>
internal sealed record MappingColumnMetadataRead(IReadOnlyDictionary<string, ColumnMetadata>? Columns, Exception? Fault);

/// <summary>
/// The mapped source columns that may be read and copied: <see cref="Copyable"/>, or <c>null</c> when the metadata does not say
/// whether <see cref="UnknownColumn"/> is field-secured (map nothing). <see cref="Secured"/> are left out.
/// </summary>
internal sealed record MappingCopyableSourceColumns(IReadOnlyList<string>? Copyable, IReadOnlyList<string> Secured, string? UnknownColumn);

/// <summary>
/// Column metadata for an app-only mapping (task 052a; moved from Repair's <c>ColumnMetadataForRepairAsync</c> and
/// <c>SourceColumnsRepairMayCopyAsync</c>, task 032b-1): read per call, never cached; a field-secured source column is never
/// read nor copied; metadata that cannot be read, comes back empty, or does not say maps nothing (UAC-r2 condition 1).
/// </summary>
internal static class MappingColumnMetadata
{
    /// <summary>
    /// <paramref name="table"/>'s column metadata, read app-only (<see cref="IGenericEntityService.GetColumnMetadataAsync"/>),
    /// or no columns when it faulted or came back empty. Cancellation is rethrown.
    /// </summary>
    internal static async Task<MappingColumnMetadataRead> ReadAsync(IGenericEntityService entities, string table, CancellationToken ct)
    {
        try
        {
            var columns = await entities.GetColumnMetadataAsync(table, ct).ConfigureAwait(false);
            return new MappingColumnMetadataRead(columns is { Count: > 0 } ? columns : null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new MappingColumnMetadataRead(null, ex);
        }
    }

    /// <summary>
    /// The mapped source columns a system-principal mapping may read and copy (owner decision 2026-10-21, field security option 1):
    /// with no caller to mask the source against (#2188), every column the source's metadata states is field-secured is left out,
    /// and so is a column the table does not have. <see cref="MappingCopyableSourceColumns.Copyable"/> is <c>null</c> when the
    /// metadata does not say whether a mapped column is secured (fail closed).
    /// </summary>
    internal static MappingCopyableSourceColumns CopyableSourceColumns(
        IReadOnlyDictionary<string, ColumnMetadata> columns, IEnumerable<string> mappedSourceColumns)
    {
        var copyable = new List<string>();
        var secured = new List<string>();
        foreach (var column in mappedSourceColumns)
        {
            if (!columns.TryGetValue(column, out var metadata))
                continue;

            if (metadata.IsSecured is not { } isSecured)
                return new MappingCopyableSourceColumns(null, secured, column);

            (isSecured ? secured : copyable).Add(column);
        }

        return new MappingCopyableSourceColumns(copyable, secured, null);
    }
}
