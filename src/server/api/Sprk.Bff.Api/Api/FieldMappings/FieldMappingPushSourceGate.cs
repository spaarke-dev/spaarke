using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 022 fix round 4 — the mapping-source rule on a push: values copied from a secure
/// source are written only into a row owned by that source's isolation owner.
/// </summary>
/// <remarks>
/// <para><b>One predicate.</b> Whether the source is secure, and whether a child is isolated with it, is
/// <see cref="MappingSourceCopyGate.DecideAsync"/> — the predicate the create (<c>RecordCreationService</c>, the owner check) and
/// Repair ask. Nothing here re-derives "secure".</para>
/// <para><b>Reads.</b> The source's gate columns (<see cref="MappingSourceCopyGate.SourceGateColumns"/>) are read app-only, once
/// per push, on the first child that would be written. The source is first decided for a row in no isolation: a source that is
/// not secure is allowed for every child and no child's owner is read. A secure source decides each child by the child's owner,
/// read app-only as the push reads its children (the update planner's reader). The Secure Record owner team is resolved at most
/// once per push (<see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync"/>, the resolution the create's
/// owner check and Repair use): with no Secure Record setup in the environment the source's flag alone decides; set up but the
/// team missing, ambiguous or unreadable is undetermined.</para>
/// <para><b>Time of check.</b> Each child's owner and filing are read just before its write; the write follows. A change to the
/// child's owner between the two is not seen by this gate (a stated limit).</para>
/// <para><b>Filing (UAC-r2 (a)).</b> A push from a secure source never changes what a child is filed under: a child whose
/// pushed values would change any filing column (<see cref="FilingWrites"/>: the registry's filing lookups, the regarding pair
/// and the columns the pair agrees with, which the update planner can move the pair with) is not written at all
/// (<see cref="RefileNotAllowedReasonCode"/>). A pushed clear of a filing column is always a filing change. A pushed value equal
/// to the child's current filing is no change; that child goes on to the owner question. A source that is not secure is
/// unchanged, its re-files and clears included.</para>
/// <para><b>Restricted source (task 022c).</b> Values mapped from a source whose <c>sprk_accesspermission</c> is Restricted, or
/// cannot be read (<see cref="RecordCreationService.MappingSourceAccessOf"/>, the create's own answer), are written only into a
/// child that is Restricted (<see cref="RecordCreationService.StoredRowIsRestricted"/>: its stored value). The column is read
/// in the same one source read, and on the child only when the source is Restricted or unreadable, in the same per-child read.
/// A child that is not Restricted gets <see cref="RestrictedSourceNotRestrictedReasonCode"/> (a Restricted source) or
/// <see cref="UndeterminedReasonCode"/> (an unreadable one). Such a source never changes a child's filing either
/// (<see cref="RefileNotAllowedReasonCode"/>, the secure source's rule). A source that is not secure and is NotRestricted or
/// carries no column reads no child, as before.</para>
/// <para><b>Fail closed.</b> A source that does not come back, a fault reading it or the child's owner, a child with no owner, a
/// Secure Record owner team that is set up but cannot be told, or any other answer the gate cannot tell is
/// <see cref="UndeterminedReasonCode"/>: the child is not written. Cancellation is rethrown.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: <see cref="MappingSourceCopyGate"/> (the predicate, reused as is)
/// and the create's owner check, which asks it for the owner the create decides. Extension: the push has no decided owner, only
/// each child's stored one, and needs the source read once and the answer per child for both the apply and the preview, so the
/// read and the per-child question live here beside the push (ADR-010, like <see cref="FieldMappingPushFamily"/>). Cost of doing
/// nothing: a push from a secure source writes its values into a child owned by an ordinary team.</para>
/// </remarks>
internal sealed class FieldMappingPushSourceGate
{
    /// <summary>The child is not owned by the secure source's isolation owner: nothing was written to it.</summary>
    internal const string NotIsolatedReasonCode = "field_mapping.push.secure_source_not_isolated";

    /// <summary>Whether the source is secure, or the child's owner, could not be told: nothing was written to it.</summary>
    internal const string UndeterminedReasonCode = "field_mapping.push.secure_source_undetermined";

    /// <summary>A secure source's push would change what the child is filed under: nothing was written to it (UAC-r2 (a)).</summary>
    internal const string RefileNotAllowedReasonCode = "field_mapping.push.secure_source_refile_not_allowed";

    /// <summary>The per-child text of <see cref="RefileNotAllowedReasonCode"/>. Names no record.</summary>
    internal const string RefileNotAllowedMessage =
        "No fields were copied to this record because its filing can't be changed by a push from a secure record.";

    /// <summary>The per-child text of <see cref="NotIsolatedReasonCode"/>. Names no record.</summary>
    internal const string NotIsolatedMessage =
        "No fields were copied to this record because the source record is secure.";

    /// <summary>The per-child text of <see cref="UndeterminedReasonCode"/>. Names no record.</summary>
    internal const string UndeterminedMessage =
        "No fields were copied to this record because it could not be checked whether the source record is secure. "
        + "Try again later.";

    /// <summary>Task 022c: the source's Access Permission is Restricted and the child's is not: nothing was written to it.</summary>
    internal const string RestrictedSourceNotRestrictedReasonCode = "field_mapping.push.restricted_source_not_restricted";

    /// <summary>The per-child text of <see cref="RestrictedSourceNotRestrictedReasonCode"/>. Names no record.</summary>
    internal const string RestrictedSourceNotRestrictedMessage =
        "No fields were copied to this record because the source record's access is Restricted and this record's is not.";

    private const string OwnerColumn = "ownerid";

    private readonly IGenericEntityService _entities;
    private readonly string _sourceTable;
    private readonly Guid _sourceId;
    private readonly Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> _secureOwnerTeam;
    private readonly ILogger _logger;
    private Task<Entity?>? _source;
    private Task<(Guid? TeamId, string? Refusal)>? _secureTeam;

    /// <param name="entities">The app-only Dataverse service (the push's own).</param>
    /// <param name="sourceTable">The push source's table.</param>
    /// <param name="sourceId">The push source's id.</param>
    /// <param name="secureOwnerTeam">The Secure Record owner team's resolution.</param>
    /// <param name="logger">Logger; the decisions are logged by id, never returned.</param>
    internal FieldMappingPushSourceGate(
        IGenericEntityService entities,
        string sourceTable,
        Guid sourceId,
        Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> secureOwnerTeam,
        ILogger? logger = null)
    {
        _entities = entities;
        _sourceTable = sourceTable;
        _sourceId = sourceId;
        _secureOwnerTeam = secureOwnerTeam;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>The gate for one push, over the app-only service and the configured Secure Record owner team.</summary>
    internal static FieldMappingPushSourceGate For(
        IGenericEntityService entities, IConfiguration configuration, string sourceTable, Guid sourceId, ILogger? logger) =>
        new(entities, sourceTable, sourceId,
            ct => SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync(entities, configuration, ct), logger);

    /// <summary>The fixed per-child text of a reason code this gate gives.</summary>
    internal static string MessageFor(string reasonCode) => reasonCode switch
    {
        NotIsolatedReasonCode => NotIsolatedMessage,
        RefileNotAllowedReasonCode => RefileNotAllowedMessage,
        RestrictedSourceNotRestrictedReasonCode => RestrictedSourceNotRestrictedMessage,
        _ => UndeterminedMessage,
    };

    /// <summary>
    /// The filing columns <paramref name="payload"/> writes on <paramref name="targetEntity"/> (logical column → the value it
    /// writes). A filing column is one the create's registry predicate names
    /// (<see cref="RecordCreationService.IsFilingLookup(string, string)"/>: lineage, ownership-parent and root filing lookups),
    /// the regarding pair (<see cref="SecureRootInheritance.PairIdColumn"/>, <see cref="SecureRootInheritance.PairTypeColumn"/>),
    /// or a column the pair agrees with (<see cref="Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.PairAgreementColumns"/>:
    /// a lookup the update planner moves the pair with when the pair repeats it, after this gate).
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> FilingWrites(string targetEntity, IReadOnlyDictionary<string, object?> payload)
    {
        var filing = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var agreement = Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.PairAgreementColumns(targetEntity);
        foreach (var (key, value) in payload)
        {
            var column = SecureRootInheritance.NormalizeColumn(key);
            if (string.Equals(column, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase)
                || RecordCreationService.IsFilingLookup(targetEntity, column)
                || agreement.ContainsKey(column))
            {
                filing[column] = value;
            }
        }

        return filing;
    }

    /// <summary>
    /// Whether writing <paramref name="pushed"/> changes a filing column that holds <paramref name="held"/>. A clear (a pushed
    /// <c>null</c>) is always a filing change, whatever the column holds: a push from a secure source never clears a filing.
    /// A value changes the column unless it names the record the column holds; a value that names no record, or one compared
    /// with a held value that was not read, is a change.
    /// </summary>
    internal static bool ChangesFiling(object? pushed, Guid? held) =>
        pushed is null || IdOf(pushed) is not { } id || id != held;

    /// <summary>The id a pushed value names: a bind path <c>/{set}({id})</c>, a bare id, or a GUID text.</summary>
    private static Guid? IdOf(object? value) => value switch
    {
        Guid id when id != Guid.Empty => id,
        string text when Guid.TryParse(BindId(text), out var id) && id != Guid.Empty => id,
        _ => null,
    };

    private static string BindId(string text)
    {
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        return open >= 0 && close > open ? text[(open + 1)..close] : text.Trim();
    }

    /// <summary>The id the child holds in a filing column (a lookup, or the pair id's text); <c>null</c> when it holds none.</summary>
    private static Guid? HeldId(Entity child, string column) =>
        child.Attributes.TryGetValue(column, out var held) ? held switch
        {
            EntityReference reference when reference.Id != Guid.Empty => reference.Id,
            _ => IdOf(held),
        } : null;

    /// <summary>
    /// <c>null</c> when values copied from the source may be written into the child; otherwise the reason code
    /// (<see cref="NotIsolatedReasonCode"/>, <see cref="RefileNotAllowedReasonCode"/>,
    /// <see cref="RestrictedSourceNotRestrictedReasonCode"/> or <see cref="UndeterminedReasonCode"/>) and nothing may be
    /// written to it.
    /// </summary>
    /// <param name="targetEntity">The child's table.</param>
    /// <param name="childId">The child.</param>
    /// <param name="payload">What the push would write to the child (Web API keys), as the rules built it.</param>
    /// <param name="ct">Cancellation.</param>
    internal async Task<string?> RefusalForChildAsync(
        string targetEntity, Guid childId, IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        try
        {
            var source = await (_source ??= ReadSourceAsync(ct)).ConfigureAwait(false);
            if (source is null)
                return Undetermined(targetEntity, childId, "the source's owner and secure flag did not come back", null);

            // The source decided for a row in no isolation: a source that is not secure is allowed for any owner. Task 022c: the
            // Restricted-source rule is decided on the same one read; a source that is NoColumn or NotRestricted adds nothing.
            var unowned = await MappingSourceCopyGate.DecideAsync(_sourceTable, source, null, SecureTeamOnce, ct).ConfigureAwait(false);
            var access = RecordCreationService.MappingSourceAccessOf(_sourceTable, source);
            var restricted = access is RecordCreationService.MappingSourceAccess.Restricted
                or RecordCreationService.MappingSourceAccess.Unreadable;
            if (unowned.Verdict == MappingSourceCopyVerdict.Allowed && !restricted)
                return null;
            if (unowned.Verdict == MappingSourceCopyVerdict.Unknown)
                return Undetermined(targetEntity, childId, unowned.Reason, unowned.Fault);

            // A secure, Restricted or unreadable source: the child's filing, its own owner (a secure source) and its stored Access
            // Permission (a Restricted or unreadable source, where its table carries the column) decide, read together.
            var secure = unowned.Verdict != MappingSourceCopyVerdict.Allowed;
            var filing = FilingWrites(targetEntity, payload);
            string[] columns =
            [
                .. secure ? [OwnerColumn] : Array.Empty<string>(),
                .. filing.Keys,
                .. restricted ? RecordCreationService.StoredRowAccessColumns(targetEntity) : [],
            ];
            var child = columns.Length == 0
                ? new Entity(targetEntity, childId) // nothing the answer needs is on the child: it is not read
                : await _entities.RetrieveAsync(targetEntity, childId, columns, ct).ConfigureAwait(false);
            if (child is null)
                return Undetermined(targetEntity, childId, "the child did not come back", null);

            // UAC-r2 (a): a push from a secure source never changes a child's filing; task 022c: nor does a push from a Restricted
            // or unreadable source. A clear is always a change; a value equal to what the child is filed under is no change.
            if (filing.FirstOrDefault(f => ChangesFiling(f.Value, HeldId(child, f.Key))) is { Key: { } moved })
            {
                _logger.LogInformation(
                    "[FIELD-MAPPING-PUSH] {Target} {ChildId}: the push from the source {Source} {SourceId} (secure: {Secure}, "
                    + "Access Permission: {Access}) would change the filing column {Column}; nothing was written to it ({Code}).",
                    targetEntity, childId, _sourceTable, _sourceId, secure, access, moved, RefileNotAllowedReasonCode);
                return RefileNotAllowedReasonCode;
            }

            if (secure)
            {
                var owner = child.GetAttributeValue<EntityReference>(OwnerColumn);
                if (owner is null || owner.Id == Guid.Empty)
                    return Undetermined(targetEntity, childId, "the child's owner did not come back", null);

                var decision = await MappingSourceCopyGate.DecideAsync(_sourceTable, source, owner, SecureTeamOnce, ct).ConfigureAwait(false);
                switch (decision.Verdict)
                {
                    case MappingSourceCopyVerdict.Allowed:
                        break;
                    case MappingSourceCopyVerdict.Secure:
                        _logger.LogInformation(
                            "[FIELD-MAPPING-PUSH] {Target} {ChildId}: the source {Source} {SourceId} is secure and the record is not "
                            + "owned by its isolation owner; nothing was written to it ({Code}).",
                            targetEntity, childId, _sourceTable, _sourceId, NotIsolatedReasonCode);
                        return NotIsolatedReasonCode;
                    default:
                        return Undetermined(targetEntity, childId, decision.Reason, decision.Fault);
                }
            }

            // Task 022c (the Restricted-source rule, as the create applies it): values mapped from a Restricted source, or from a
            // source whose Access Permission cannot be read, are written only into a child that is Restricted.
            if (!restricted || RecordCreationService.StoredRowIsRestricted(targetEntity, child))
                return null;
            if (access == RecordCreationService.MappingSourceAccess.Unreadable)
            {
                return Undetermined(targetEntity, childId,
                    "the source's Access Permission could not be read and the record is not Restricted", null);
            }

            _logger.LogInformation(
                "[FIELD-MAPPING-PUSH] {Target} {ChildId}: the source {Source} {SourceId} is Restricted and the record is not; "
                + "nothing was written to it ({Code}).",
                targetEntity, childId, _sourceTable, _sourceId, RestrictedSourceNotRestrictedReasonCode);
            return RestrictedSourceNotRestrictedReasonCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Undetermined(targetEntity, childId, MappingSourceCopyGate.ReasonFault, ex);
        }
    }

    private Task<(Guid? TeamId, string? Refusal)> SecureTeamOnce(CancellationToken ct) => _secureTeam ??= _secureOwnerTeam(ct);

    private async Task<Entity?> ReadSourceAsync(CancellationToken ct) =>
        await _entities.RetrieveAsync(_sourceTable, _sourceId,
                MappingSourceCopyGate.SourceGateColumns(_sourceTable)
                    .Concat(RecordCreationService.MappingSourceAccessColumns(_sourceTable))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(), ct)
            .ConfigureAwait(false);

    private string Undetermined(string targetEntity, Guid childId, string? reason, Exception? fault)
    {
        _logger.LogWarning(fault,
            "[FIELD-MAPPING-PUSH] {Target} {ChildId}: whether the source {Source} {SourceId} may be copied into it could not be "
            + "told ({Reason}); nothing was written to it ({Code}).",
            targetEntity, childId, _sourceTable, _sourceId, reason, UndeterminedReasonCode);
        return UndeterminedReasonCode;
    }
}
