using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> decided.</summary>
public enum SignalAccessOutcome
{
    /// <summary>The caller may read the Signal and its core record (or owns a no-core Do item).</summary>
    Allowed,

    /// <summary>
    /// Not found, not readable, or not decidable. One outcome for all of them (the 097/#1312 uniform 404): the route is
    /// not an existence oracle, and a fault is never a Read-shaped consolation prize.
    /// </summary>
    NotFound,

    /// <summary>The caller could not be resolved to a Dataverse identity (no token, OBO refused): the single 403 (D-29).</summary>
    CallerUnresolved,
}

/// <summary>The decision, plus what the caller was allowed to learn about the Signal.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Lane">The Signal's lane; set only when <see cref="Outcome"/> is <see cref="SignalAccessOutcome.Allowed"/>.</param>
/// <param name="PolicyVersionId">The policy version that raised the Signal; set only when allowed.</param>
public sealed record SignalAccessDecision(SignalAccessOutcome Outcome, DecisionLane? Lane, Guid? PolicyVersionId)
{
    public static readonly SignalAccessDecision NotFound = new(SignalAccessOutcome.NotFound, null, null);
    public static readonly SignalAccessDecision CallerUnresolved = new(SignalAccessOutcome.CallerUnresolved, null, null);
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
/// record is owner-only and only in the Do lane (D-35): the caller must be the Signal's owner.</para>
/// <para><b>Fail closed, one denial.</b> A row the caller cannot read, a malformed core-record pair, a catalog row that
/// does not resolve, a Dataverse fault of any kind: all <see cref="SignalAccessOutcome.NotFound"/>. Only a caller who
/// cannot be resolved at all is <see cref="SignalAccessOutcome.CallerUnresolved"/>.</para>
/// <para>The catalog row and entity-set name are configuration and metadata, not matter data, so they are read with the
/// BFF's own <see cref="IGenericEntityService"/>; nothing read that way is returned to the caller.</para>
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
                $"sprk_signals({signalId})?$select=sprk_lane,_sprk_policyversion_value,_sprk_corerecordtype_value,sprk_corerecordid,_ownerid_value",
                ct).ConfigureAwait(false);

            if (!signal.IsSuccess)
            {
                return Denied(signal, "signal");
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

            var entitySet = await _entities.GetEntitySetNameAsync(logicalName, ct).ConfigureAwait(false);

            // createdon exists on every Dataverse table; the value is not used, only whether the caller may read the row.
            var core = await _userClient.GetAsync($"{entitySet}({coreId})?$select=createdon", ct).ConfigureAwait(false);
            if (!core.IsSuccess)
            {
                return Denied(core, "core record");
            }

            return new SignalAccessDecision(SignalAccessOutcome.Allowed, lane, policyVersionId);
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
        // D-35: no core record means owner-only, and only in the Do lane.
        if (lane != DecisionLane.Do || !TryReadGuid(signalRow, "_ownerid_value", out var ownerId))
        {
            return SignalAccessDecision.NotFound;
        }

        // Under the caller's token WhoAmI cannot name anyone but the caller; no token is the single 403.
        var who = await _userClient.GetAsync("WhoAmI", ct).ConfigureAwait(false);
        if (!who.IsSuccess)
        {
            return Denied(who, "caller identity");
        }

        return who.Body is { ValueKind: JsonValueKind.Object } body
            && TryReadGuid(body, "UserId", out var callerId)
            && callerId == ownerId
                ? new SignalAccessDecision(SignalAccessOutcome.Allowed, lane, policyVersionId)
                : SignalAccessDecision.NotFound;
    }

    private SignalAccessDecision Denied(DataverseUserResponse response, string what)
    {
        if (response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured)
        {
            _logger.LogWarning("Signal access: the caller could not be resolved reading the {What} ({ErrorCode}).",
                what, response.ErrorCode);
            return SignalAccessDecision.CallerUnresolved;
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
