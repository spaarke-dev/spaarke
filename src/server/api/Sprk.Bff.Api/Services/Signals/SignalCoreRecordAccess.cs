using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> decided.</summary>
public enum SignalAccessOutcome
{
    /// <summary>The caller may read the Signal and its core record (or, for a no-core Do item, its subject row).</summary>
    Allowed,

    /// <summary>
    /// Not found, not readable, or not decidable. One outcome for all of them (the 097/#1312 uniform 404): the route is
    /// not an existence oracle, and a fault is never a Read-shaped consolation prize.
    /// </summary>
    NotFound,

    /// <summary>The caller could not be resolved to a Dataverse identity (no token, OBO refused): the single 403 (D-29).</summary>
    CallerUnresolved,

    /// <summary>
    /// A no-core Signal's subject could not be read because of a FAULT (throttling, a 5xx, a failed entity-set lookup), which says
    /// nothing about whether the caller may read it: the route answers 503, never the uniform 404 that means "not yours" (uac-r2 F4-b).
    /// Only the no-core subject read produces it; every other fault stays the uniform denial.
    /// </summary>
    Unavailable,
}

/// <summary>The decision, plus what the caller was allowed to learn about the Signal.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Lane">The Signal's lane; set only when <see cref="Outcome"/> is <see cref="SignalAccessOutcome.Allowed"/>.</param>
/// <param name="PolicyVersionId">The policy version that raised the Signal; set only when allowed.</param>
/// <param name="CoreTypeId">The core-record pair the caller's own read returned (task 043 K2), so a route that re-reads the
/// Signal app-only can refuse when the pair changed between the two reads. Both null for a no-core Signal.</param>
/// <param name="CoreId">The core record id of that pair; null for a no-core Signal.</param>
public sealed record SignalAccessDecision(
    SignalAccessOutcome Outcome, DecisionLane? Lane, Guid? PolicyVersionId, Guid? CoreTypeId = null, Guid? CoreId = null)
{
    public static readonly SignalAccessDecision NotFound = new(SignalAccessOutcome.NotFound, null, null);
    public static readonly SignalAccessDecision CallerUnresolved = new(SignalAccessOutcome.CallerUnresolved, null, null);
    public static readonly SignalAccessDecision Unavailable = new(SignalAccessOutcome.Unavailable, null, null);
}

/// <summary>
/// THE one place a Signal-keyed route decides whether the caller may see a Signal (task 036; task 038 reuses it
/// rather than writing a second). Every question is asked AS THE CALLER through <see cref="IDataverseUserClient"/>,
/// so Dataverse's own security model decides, never the BFF identity.
/// </summary>
/// <remarks>
/// <para><b>The rule (D-33..D-36, D-38).</b> (1) The caller reads the Signal row itself: Dataverse first trims by the
/// Signal's own business unit / secure-child sharing. (2) The caller then reads the Signal's CORE record, whatever its
/// type: the type comes from the <c>sprk_recordtype_ref</c> catalog row the Signal's <c>sprk_corerecordtype</c> names,
/// the id from <c>sprk_corerecordid</c>. No code here names a core type (D-36 extensibility). (3) A Signal with NO core
/// record is visible only in the Do lane (D-35) and only to a caller who can read its SUBJECT row (<c>sprk_regardingrecordtype</c> /
/// <c>sprk_regardingrecordid</c>, an ownership-parent table) as themselves, in addition to the Signal row (D-129: records are
/// owned by teams, so the old "caller is the Signal's owner" test matched nobody for a team-owned item). A subject in a secure
/// record family is unreadable to a non-member, so the Signal is too (D-123).</para>
/// <para><b>Fail closed, one denial.</b> A row the caller cannot read, a malformed core-record pair, a catalog row that
/// does not resolve, a Dataverse fault of any kind: all <see cref="SignalAccessOutcome.NotFound"/>, except a fault reading a
/// no-core Signal's SUBJECT, which is <see cref="SignalAccessOutcome.Unavailable"/> (503, uac-r2 F4-b: a fault is not a
/// denial). Only a caller who cannot be resolved at all is <see cref="SignalAccessOutcome.CallerUnresolved"/>.</para>
/// <para>The catalog row and entity-set name are configuration and metadata, not matter data, so they are read with the
/// BFF's own <see cref="IGenericEntityService"/>; nothing read that way is returned to the caller.</para>
/// <para><b>Core set (K2).</b> The core record's table must be one of <see cref="CoreAncestorResolver.CoreRecordEntities"/>,
/// uac-r2's access-control core taxonomy, however the catalog row reads. Without it a user with Write on
/// <c>sprk_signal</c> could point a Signal's core record at any row they can read and use the route as a probe.
/// Still generic: the check is membership in uac-r2's list, not a branch per type (a fifth core type added there is
/// accepted here unchanged).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>AuthorizationService.GetCallerRecordAccessAsync</c>
/// (uac-r2 task 070) already answers "may this caller read this record" for any entity set, as the caller, failing closed
/// without a token. Extension: that method needs the caller's user id and bearer token plumbed in and returns a rights
/// snapshot cached by <c>CachedAccessDataSource</c> (ADR-003 A1, 60 s). The decision here needs more than a rights bit: it needs
/// the Signal row's own columns (lane, policy version, core-record pair, subject pair) read under the caller's Dataverse
/// security, and a plain read answers "may they read it" and returns those columns in one call, uncached, so a revoked
/// share takes effect on the next request. Using it would add a second call per record and a stale window for no gain.
/// Cost of doing nothing: no check at all; the route would serve a plan for a Signal on a matter the caller cannot open.
/// If the reviewer prefers the shared mechanism for the CORE-record half, <c>GetCallerRecordAccessAsync</c> is a drop-in
/// for that one read.</para>
/// <para><b>uac-r2 mechanisms reused, none rebuilt:</b> the caller-identity client (<see cref="IDataverseUserClient"/>,
/// relocated by uac-r2 task 126) and the route-level uniform 404 / single 403 shapes. A caller-rights probe
/// (<c>CallerRecordAccessProbe</c>) answers a different question (rights beyond Read) and is not needed for a read.</para>
/// </remarks>
public sealed class SignalCoreRecordAccess
{
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string RecordLogicalNameColumn = "sprk_recordlogicalname";

    private readonly IDataverseUserClient _userClient;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<SignalCoreRecordAccess> _logger;

    public SignalCoreRecordAccess(
        IDataverseUserClient userClient,
        IGenericEntityService entities,
        ILogger<SignalCoreRecordAccess> logger)
    {
        _userClient = userClient ?? throw new ArgumentNullException(nameof(userClient));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SignalAccessDecision> AuthorizeAsync(Guid signalId, CancellationToken ct)
    {
        if (signalId == Guid.Empty)
        {
            return SignalAccessDecision.NotFound;
        }

        try
        {
            var signal = await _userClient.GetAsync(
                $"sprk_signals({signalId})?$select=sprk_lane,_sprk_policyversion_value,_sprk_corerecordtype_value,sprk_corerecordid,sprk_regardingrecordtype,sprk_regardingrecordid",
                ct).ConfigureAwait(false);

            if (!signal.IsSuccess)
            {
                return await DeniedAsync(signal, "signal", ct).ConfigureAwait(false);
            }

            if (signal.Body is not { ValueKind: JsonValueKind.Object } row
                || !TryReadLane(row, out var lane)
                || !TryReadGuid(row, "_sprk_policyversion_value", out var policyVersionId))
            {
                _logger.LogWarning("Signal access: the Signal row is missing its lane or policy version; denying.");
                return SignalAccessDecision.NotFound;
            }

            var hasType = TryReadGuid(row, "_sprk_corerecordtype_value", out var coreTypeId);
            var coreIdText = ReadString(row, "sprk_corerecordid");

            if (!hasType && string.IsNullOrWhiteSpace(coreIdText))
            {
                return await AuthorizeNoCoreRecordAsync(row, lane, policyVersionId, ct).ConfigureAwait(false);
            }

            // A pair with only one half is malformed, not "no core record": deny rather than treat it as owner-only.
            if (!hasType || !Guid.TryParse(coreIdText, out var coreId) || coreId == Guid.Empty)
            {
                _logger.LogWarning("Signal access: the Signal's core-record pair is incomplete or unparseable; denying.");
                return SignalAccessDecision.NotFound;
            }

            var logicalName = (await _entities
                .RetrieveAsync(RecordTypeRefEntity, coreTypeId, [RecordLogicalNameColumn], ct)
                .ConfigureAwait(false))
                .GetAttributeValue<string>(RecordLogicalNameColumn)?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(logicalName))
            {
                _logger.LogWarning("Signal access: the core record type does not resolve to a table; denying.");
                return SignalAccessDecision.NotFound;
            }

            if (!CoreAncestorResolver.CoreRecordEntities.Contains(logicalName, StringComparer.OrdinalIgnoreCase))
            {
                // K2: a Signal's core record can only be a core type; anything else is a forged or stale pointer.
                _logger.LogWarning("Signal access: the core record type is not an access-control core type; denying.");
                return SignalAccessDecision.NotFound;
            }

            var entitySet = await _entities.GetEntitySetNameAsync(logicalName, ct).ConfigureAwait(false);

            // createdon exists on every Dataverse table; the value is not used, only whether the caller may read the row.
            var core = await _userClient.GetAsync($"{entitySet}({coreId})?$select=createdon", ct).ConfigureAwait(false);
            if (!core.IsSuccess)
            {
                return await DeniedAsync(core, "core record", ct).ConfigureAwait(false);
            }

            return new SignalAccessDecision(SignalAccessOutcome.Allowed, lane, policyVersionId, coreTypeId, coreId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: "could not answer" is "not authorized". Type only, never the message (it can echo row content).
            _logger.LogError("Signal access: the check faulted ({ExceptionType}); denying.", ex.GetType().Name);
            return SignalAccessDecision.NotFound;
        }
    }

    private async Task<SignalAccessDecision> AuthorizeNoCoreRecordAsync(
        JsonElement signalRow, DecisionLane lane, Guid policyVersionId, CancellationToken ct)
    {
        // D-35 as amended by D-129 (records are owned by teams, never by a person): a no-core item is visible to whoever can read
        // its SUBJECT row as themselves, and only in the Do lane. The Signal row itself was read as the caller before this point
        // (both reads must succeed), so the subject's own team and sharing decide, not an owner-equality test; a subject in a
        // secure record family is unreadable to a non-member and so is the uniform denial (D-123).
        if (lane != DecisionLane.Do)
        {
            return SignalAccessDecision.NotFound;
        }

        var subjectType = ReadString(signalRow, "sprk_regardingrecordtype")?.Trim().ToLowerInvariant();
        if (!Guid.TryParse(ReadString(signalRow, "sprk_regardingrecordid"), out var subjectId) || subjectId == Guid.Empty
            || !RecordOwnershipResolver.IsOwnershipParent(subjectType))
        {
            // No subject pair, or a table that is not one of uac-r2's ownership parents: a forged or stale pointer (the K2 rule,
            // applied to the subject).
            _logger.LogWarning("Signal access: the no-core Signal's subject pair is missing or not an ownership-parent table; denying.");
            return SignalAccessDecision.NotFound;
        }

        string entitySet;
        try
        {
            entitySet = await _entities.GetEntitySetNameAsync(subjectType!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the subject's entity set could not be looked up ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalAccessDecision.Unavailable;
        }

        // createdon exists on every Dataverse table; the value is not used, only whether the caller may read the row.
        DataverseUserResponse subject;
        try
        {
            subject = await _userClient.GetAsync($"{entitySet}({subjectId})?$select=createdon", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the subject read threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalAccessDecision.Unavailable;
        }

        if (!subject.IsSuccess)
        {
            return await DeniedAsync(subject, "subject", ct, faultIsUnavailable: true).ConfigureAwait(false);
        }

        return new SignalAccessDecision(SignalAccessOutcome.Allowed, lane, policyVersionId);
    }

    private async Task<SignalAccessDecision> DeniedAsync(
        DataverseUserResponse response, string what, CancellationToken ct, bool faultIsUnavailable = false)
    {
        if (response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured)
        {
            _logger.LogWarning("Signal access: the caller could not be resolved reading the {What} ({ErrorCode}).",
                what, response.ErrorCode);
            return SignalAccessDecision.CallerUnresolved;
        }

        // A 403 from Dataverse is "no right on this row" for a user it knows, and also the answer for a valid token whose
        // owner has no Dataverse user at all. WhoAmI needs no privilege, so asking it now tells the two apart: a caller who
        // cannot be identified is the single 403 (D-29), as in the events list; one who can is an ordinary no-read 404.
        if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && what != "caller identity")
        {
            var who = await _userClient.GetAsync("WhoAmI", ct).ConfigureAwait(false);
            if (!who.IsSuccess)
            {
                // Only "the caller has no Dataverse identity" is unresolved: Dataverse refusing them, or the same user-context
                // / OBO codes the first read maps. Throttling, a 5xx or a transport fault says nothing about WHO they are, so a
                // known caller under load must get the same 404 whether or not the row exists.
                if (who.ErrorCode is DataverseUserClientErrorCodes.AccessDenied
                    or DataverseUserClientErrorCodes.UserContextRequired
                    or DataverseUserClientErrorCodes.OboExchangeFailed
                    or DataverseUserClientErrorCodes.OboNotConfigured)
                {
                    _logger.LogWarning("Signal access: reading the {What} was denied and the caller has no Dataverse identity ({ErrorCode}).",
                        what, who.ErrorCode);
                    return SignalAccessDecision.CallerUnresolved;
                }

                _logger.LogWarning("Signal access: reading the {What} was denied and the caller's identity could not be checked ({ErrorCode}); denying as not found.",
                    what, who.ErrorCode);
            }
        }

        // 404/403 are the expected "cannot read"; anything else is a fault. Both are the same denial to the caller;
        // the log keeps them apart for the operator.
        if (response.ErrorCode is DataverseUserClientErrorCodes.NotFound or DataverseUserClientErrorCodes.AccessDenied)
        {
            _logger.LogDebug("Signal access: the caller cannot read the {What} ({ErrorCode}).", what, response.ErrorCode);
        }
        else
        {
            _logger.LogWarning("Signal access: reading the {What} failed ({ErrorCode}); denying.", what, response.ErrorCode);
            if (faultIsUnavailable)
            {
                // A throttle, a 5xx or a transport fault says nothing about whether the caller may read the row: not a denial (503).
                return SignalAccessDecision.Unavailable;
            }
        }

        return SignalAccessDecision.NotFound;
    }

    private static bool TryReadLane(JsonElement row, out DecisionLane lane)
    {
        lane = default;
        if (row.TryGetProperty("sprk_lane", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var raw) && Enum.IsDefined(typeof(DecisionLane), raw))
        {
            lane = (DecisionLane)raw;
            return true;
        }

        return false;
    }

    private static bool TryReadGuid(JsonElement row, string name, out Guid id)
    {
        id = Guid.Empty;
        return row.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out id)
            && id != Guid.Empty;
    }

    private static string? ReadString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
