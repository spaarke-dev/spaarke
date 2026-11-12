using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// Task 049 fix round 2 (owner ruling 2026-11-05; UAC-r2's rulings D1 and D2): <see cref="RecordCreationRequest.SecureFromCreation"/>
/// — a project the wizard creates as secure is secure from creation. The create is planned INTO isolation
/// (<see cref="SecureRootFilingGate.PlanCreateAsync"/> with <c>secureRequested</c>) and completed at step 12 like a row filed
/// under a secure record, so the "Assigned *" step runs on a record that is already secure.
/// </summary>
/// <remarks>
/// <para><b>The gate is the delegation rule's (D1).</b> Making a record secure through <c>provision-project</c> needs Write on
/// the record and the table's Share privilege, asked as the caller by <see cref="DelegationRuleFilter"/>. The same rule is
/// asked here, before the plan, against the record as an ordinary create would have made it — owned by the team the owner
/// rule names for it (step 7's own decision, asked early): the caller's rights on it come from
/// <see cref="CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync"/> (there is no record yet for
/// <c>RetrievePrincipalAccess</c>), and the decision is <see cref="DelegationRuleFilter.RefusalFor"/>, the filter's own. A
/// refusal carries the filter's deny code and words; the child-record route renders it in the filter's ProblemDetails shape
/// (<see cref="DelegationRuleFilter.DenyResult"/>). A check that faults, or a question Dataverse does not answer, is
/// <see cref="DelegationRuleFilter.DenyCheckFailed"/>. Nothing is written on any refusal.</para>
/// <para><b>Known limit.</b> A create filed under a secure record is created secure without being asked; with the intent too,
/// the owner an ordinary create would get is the Secure Record Owners team, which step 7 refuses for a root, so such a create
/// is refused (fail closed). The Create Project wizard files a project only after it is created.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — the isolated create (task 158 r1) and provisioning.
/// Extension — a declared-intent trigger on that path (one optional request field, validated on the route). Cost of doing
/// nothing — a project the wizard creates as secure would not be secure from creation.</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>
    /// <see cref="RecordCreationRequest.SecureFromCreation"/>'s argument rule: a caller's create of a table whose create can be
    /// isolated (<see cref="SecureRootInheritance.Inherits"/>). Anything else is the adapter's defect, thrown before anything
    /// is read. (The child-record route answers 400 before it asks.)
    /// </summary>
    internal static void ValidateSecureFromCreation(string table, RecordCreationRequest request, RecordCreationPrincipal principal)
    {
        if (!request.SecureFromCreation)
            return;

        if (!SecureRootInheritance.Inherits(table))
        {
            throw new ArgumentException(
                $"A {NounOf(table)} cannot be created secure here: only a project or a work assignment can be.", nameof(request));
        }

        if (principal is not RecordCreationPrincipal.CallerPrincipal)
        {
            throw new ArgumentException(
                "A secure-from-creation create is a caller's create only: the caller's own rights are asked first.", nameof(request));
        }
    }

    /// <summary>
    /// Step 6, before the plan, for a <see cref="RecordCreationRequest.SecureFromCreation"/> create: the stamp plan and the
    /// owner an ordinary create would get (steps 6½ and 7, asked early; the stamp plan is reused at 6½), then the caller's
    /// Write and Share on a record that team owns, decided by the delegation filter's own rule. A refusal, or the stamp plan.
    /// </summary>
    private async Task<(RecordCreationRefusal? Refusal, (RecordCreationRefusal? Refusal, Dictionary<string, object>? Plan) Planned)>
        SecureFromCreationGateAsync(
            string table, RecordTableRule rule, DataverseWriteItemMapper.MappedItem item, IDataverseUserClient user, Guid me,
            Guid? ownerContextObjectId, RecordRequester? requestedBy, string noun, string correlationId, CancellationToken ct)
    {
        var planned = await PlanStampBeforeOwnerAsync(table, item, noun, ct).ConfigureAwait(false);
        if (planned.Refusal is { } stampRefusal)
            return (stampRefusal, planned);

        var ordinary = await DecideOwnerAsync(table, rule, item, planned.Plan, ownerContextObjectId, me, requestedBy, noun, ct)
            .ConfigureAwait(false);
        if (ordinary.Refusal is { } ownerRefusal)
            return (ownerRefusal, planned);

        string? denied;
        try
        {
            if (ordinary.Owner!.OwningTeamId is not { } team)
            {
                denied = DelegationRuleFilter.DenyCheckFailed;
            }
            else
            {
                var (rights, failure) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(user, me, table, team, ct)
                    .ConfigureAwait(false);
                if (failure is not null)
                {
                    _logger.LogWarning(
                        "[RECORD-CREATE] secure-from-creation {Table}: the caller's rights could not be read (HTTP {Status} {Code}); " +
                        "refused (correlationId={CorrelationId})", table, failure.StatusCode, failure.ErrorCode, correlationId);
                    denied = DelegationRuleFilter.DenyCheckFailed;
                }
                else
                {
                    denied = DelegationRuleFilter.RefusalFor(rights);
                    if (denied is not null)
                    {
                        _logger.LogWarning(
                            "[RECORD-CREATE] secure-from-creation {Table}: the caller holds {Rights} on the record as an ordinary " +
                            "create would make it (owner team {Team}); refused {Code} (correlationId={CorrelationId})",
                            table, rights, team, denied, correlationId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] secure-from-creation {Table}: the caller's rights check faulted; refused (correlationId={CorrelationId})",
                table, correlationId);
            denied = DelegationRuleFilter.DenyCheckFailed;
        }

        if (denied is null)
            return (null, planned);

        return (new RecordCreationRefusal(RecordCreationRefusalKind.Forbidden, denied,
            denied == DelegationRuleFilter.DenyCheckFailed
                ? DelegationRuleFilter.CheckFailedDetail
                : DelegationRuleFilter.RightsRequiredDetail), planned);
    }
}
