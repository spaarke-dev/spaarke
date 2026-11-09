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
/// once per push (<see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>, the resolution the create's owner check
/// uses).</para>
/// <para><b>Fail closed.</b> A source that does not come back, a fault reading it or the child's owner, a child with no owner, or
/// an answer the gate cannot tell is <see cref="UndeterminedReasonCode"/>: the child is not written. Cancellation is rethrown.</para>
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

    /// <summary>The per-child text of <see cref="NotIsolatedReasonCode"/>. Names no record.</summary>
    internal const string NotIsolatedMessage =
        "Some fields weren't copied to this record because the source record is secure. Nothing was written to it.";

    /// <summary>The per-child text of <see cref="UndeterminedReasonCode"/>. Names no record.</summary>
    internal const string UndeterminedMessage =
        "Fields weren't copied to this record because it could not be checked whether the source record is secure. "
        + "Nothing was written to it. Try again later.";

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
            ct => SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(entities, configuration, ct), logger);

    /// <summary>The fixed per-child text of a reason code this gate gives.</summary>
    internal static string MessageFor(string reasonCode) =>
        reasonCode == NotIsolatedReasonCode ? NotIsolatedMessage : UndeterminedMessage;

    /// <summary>
    /// <c>null</c> when values copied from the source may be written into the child; otherwise the reason code
    /// (<see cref="NotIsolatedReasonCode"/> or <see cref="UndeterminedReasonCode"/>) and nothing may be written to it.
    /// </summary>
    internal async Task<string?> RefusalForChildAsync(string targetEntity, Guid childId, CancellationToken ct)
    {
        try
        {
            var source = await (_source ??= ReadSourceAsync(ct)).ConfigureAwait(false);
            if (source is null)
                return Undetermined(targetEntity, childId, "the source's owner and secure flag did not come back", null);

            // The source decided for a row in no isolation: a source that is not secure is allowed for any owner.
            var unowned = await MappingSourceCopyGate.DecideAsync(_sourceTable, source, null, SecureTeamOnce, ct).ConfigureAwait(false);
            if (unowned.Verdict == MappingSourceCopyVerdict.Allowed)
                return null;
            if (unowned.Verdict == MappingSourceCopyVerdict.Unknown)
                return Undetermined(targetEntity, childId, unowned.Reason, unowned.Fault);

            // A secure source: the child's own owner decides.
            var child = await _entities.RetrieveAsync(targetEntity, childId, [OwnerColumn], ct).ConfigureAwait(false);
            var owner = child?.GetAttributeValue<EntityReference>(OwnerColumn);
            if (owner is null || owner.Id == Guid.Empty)
                return Undetermined(targetEntity, childId, "the child's owner did not come back", null);

            var decision = await MappingSourceCopyGate.DecideAsync(_sourceTable, source, owner, SecureTeamOnce, ct).ConfigureAwait(false);
            switch (decision.Verdict)
            {
                case MappingSourceCopyVerdict.Allowed:
                    return null;
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
        await _entities.RetrieveAsync(_sourceTable, _sourceId, MappingSourceCopyGate.SourceGateColumns(_sourceTable).ToArray(), ct)
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
