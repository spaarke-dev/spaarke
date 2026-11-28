using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Intake.Engine;

// The BFF side of the intake engine job's contract (spaarke-intake-pipeline-r1 task 014, ADR-004 A2). The other side is
// src/server/services/Sprk.Intake.EngineJob/entrypoint/envelope.py (task 001): a change to a name, a key or a path here is a change
// there. Messages carry ids and closed codes only (NFR-03, ADR-015); every blob path is DERIVED from the run id by this file.

/// <summary>The engine job's closed set of operations (envelope.py <c>OPERATIONS</c>).</summary>
public static class IntakeEngineOperations
{
    /// <summary><c>segment_clauses.py</c>: the draft's clause list (<c>clauses.json</c>), run BEFORE the edit-plan AI.</summary>
    public const string Segment = "segment";

    /// <summary>dry-run, redline, summary and check: the three .docx outputs, the check report and the refused-edit report.</summary>
    public const string Redline = "redline";

    public static bool IsKnown(string? operation) => operation is Segment or Redline;

    /// <summary>The order the NDA harness dispatches them in; a dispatch later in this order is "ahead of" an earlier one.</summary>
    internal static int Order(string operation) => operation == Segment ? 1 : 2;

    /// <summary>The run stage the operation belongs to (the NDA harness's Analyse and Build): what a hold or failure shows.</summary>
    internal static IntakeRunStage StageOf(string operation) => operation == Segment ? IntakeRunStage.Analyse : IntakeRunStage.Build;
}

/// <summary>
/// The <see cref="Harnesses.IntakeHarnessResultReference.Kind"/> of the step result a completion stores; its
/// <c>Reference</c> is the dispatch's output prefix (<see cref="IntakeEngineDispatchId.OutPrefix"/>), never a path from a message.
/// </summary>
public static class IntakeEngineResultKinds
{
    /// <summary>A completed <c>segment</c>: <c>clauses.json</c> is under the prefix.</summary>
    public const string Segment = "engine-segment";

    /// <summary>A completed <c>redline</c> whose <c>check_outputs</c> passed (a failed check never stores a result): the .docx files, <c>check-report.json</c> and <c>refused-report.json</c> are under the prefix.</summary>
    public const string RedlinePassed = "engine-redline-passed";
}

/// <summary>The job types of the engine's three messages.</summary>
public static class IntakeEngineJobTypes
{
    /// <summary>BFF → <c>intake-engine-requests</c> (envelope.py <c>REQUEST_JOB_TYPE</c>).</summary>
    public const string Request = "IntakeEngineRun";

    /// <summary>Engine → <c>intake-engine-completions</c> (envelope.py <c>COMPLETION_JOB_TYPE</c>); handled by <c>IntakeEngineCompletionJobHandler</c>.</summary>
    public const string Completed = "IntakeEngineCompleted";

    /// <summary>BFF → its own jobs queue, scheduled (the hard timeout); handled by <c>IntakeEngineTimeoutJobHandler</c>.</summary>
    public const string Timeout = "IntakeEngineTimeout";
}

/// <summary>The outcome codes an engine completion may carry (engines.py), and the check outcomes of a completed redline.</summary>
public static class IntakeEngineOutcomes
{
    public const string Completed = "completed";
    public const string PlanDoesNotFit = "plan_does_not_fit";
    public const string InputInvalid = "input_invalid";
    public const string ContractError = "contract_error";

    public const string CheckPassed = "passed";
    public const string CheckFailed = "failed";
}

/// <summary>The closed codes a run is held or failed with by the engine plumbing; shown as admin-attention text and logged, never content.</summary>
public static class IntakeEngineCodes
{
    /// <summary>No staging endpoint is configured on this stamp (no engine deployed): nothing was staged or sent.</summary>
    public const string NotConfigured = "intake.engine_not_configured";

    /// <summary><c>check_outputs</c> failed on the engine's outputs (FR-09c): held, nothing is sent for approval.</summary>
    public const string CheckFailed = "intake.engine_check_failed";

    /// <summary>The edit plan breaks the engine contract (or, in the engine's all-or-nothing mode, an edit does not fit): held.</summary>
    public const string PlanDoesNotFit = "intake.engine_plan_does_not_fit";

    /// <summary>The engine broke its own documented contract (an unknown code, bad stderr, an unreadable check): held.</summary>
    public const string ContractError = "intake.engine_contract_error";

    /// <summary>The engine could not read the draft (not a usable .docx): the run can never succeed, so it Fails.</summary>
    public const string InputInvalid = "intake.engine_input_invalid";

    /// <summary>No completion arrived within the hard timeout (NFR-05): the run Fails with this code, "engine timeout".</summary>
    public const string Timeout = "intake.engine_timeout";
}

/// <summary>
/// One dispatch of the engine job: a run, an operation and a dispatch attempt. Its <see cref="Key"/> is the request's idempotency
/// key (<c>IntakeEngine:{runId}:{operation}:{dispatchAttempt}</c>, envelope.py <c>idempotency_key</c>) and the run's
/// <c>sprk_engineexecutionid</c>; every staging path is derived from it, never read from a message.
/// </summary>
/// <remarks>
/// One run dispatches <c>segment</c> and then <c>redline</c> under the same attempt, so the key carries the operation. A second pass
/// over the same operation (the refused-edit rewrite, a re-drive after a held check) uses the next attempt: an attempt is spent once
/// its completion was handled, and its staged inputs and outputs are never overwritten.
/// </remarks>
public readonly record struct IntakeEngineDispatchId(Guid RunId, string Operation, int DispatchAttempt)
{
    /// <summary>The engine's own bound (envelope.py <c>MAX_DISPATCH_ATTEMPT</c>).</summary>
    public const int MaxDispatchAttempt = 1000;

    /// <summary>True when the operation is known, the run id is set and the attempt is in 1..<see cref="MaxDispatchAttempt"/>.</summary>
    public bool IsValid => RunId != Guid.Empty && IntakeEngineOperations.IsKnown(Operation) && DispatchAttempt is >= 1 and <= MaxDispatchAttempt;

    /// <summary>The engine request's idempotency key and the run's execution id.</summary>
    public string Key => $"IntakeEngine:{RunId:D}:{Operation}:{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The scheduled timeout message's idempotency key.</summary>
    public string TimeoutKey => $"IntakeEngineTimeout:{RunId:D}:{Operation}:{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The run state marker that the continuation of this dispatch's completion was enqueued.</summary>
    public string ContinuationMarker => $"engine-continued:{Operation}:{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}";

    // Staging layout (envelope.py): the BFF writes in/, the job writes out/ and markers/. dispatch/ is the BFF's, under in/.

    /// <summary>The draft, one per run: <c>{runId}/in/source.docx</c>.</summary>
    public string Source => $"{RunId:D}/in/source.docx";

    /// <summary>The edit plan of one redline attempt: <c>{runId}/in/redline/{attempt}/plan.json</c>.</summary>
    public string Plan => $"{RunId:D}/in/redline/{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}/plan.json";

    /// <summary>Written once the request was sent: <c>{runId}/in/dispatch/{operation}/{attempt}.json</c> (a repeat dispatch sends nothing).</summary>
    public string DispatchMarker => $"{RunId:D}/in/dispatch/{Operation}/{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}.json";

    /// <summary>Where the job writes this dispatch's outputs: <c>{runId}/out/{operation}/{attempt}/</c>. The run's stored step result references it.</summary>
    public string OutPrefix => $"{RunId:D}/out/{Operation}/{DispatchAttempt.ToString(CultureInfo.InvariantCulture)}/";

    /// <summary>One named output of this dispatch (<c>clauses.json</c>, <c>check-report.json</c>, <c>refused-report.json</c>, the .docx files).</summary>
    public string Output(string name) => OutPrefix + name;

    /// <summary>
    /// Position in a run's dispatch history: later attempts are "ahead", and within an attempt <c>redline</c> follows <c>segment</c>.
    /// A harness numbers its dispatches so this order is the order it sends them in.
    /// </summary>
    internal (int Attempt, int Operation) Position => (DispatchAttempt, IntakeEngineOperations.Order(Operation));

    /// <summary>Reads a key written by <see cref="Key"/>; anything else (null, another shape, a bad run id or attempt) is false.</summary>
    public static bool TryParseKey(string? key, out IntakeEngineDispatchId id)
    {
        id = default;
        var parts = key?.Split(':');
        if (parts is not { Length: 4 } || parts[0] != "IntakeEngine"
            || !Guid.TryParseExact(parts[1], "D", out var runId)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var attempt))
        {
            return false;
        }

        var parsed = new IntakeEngineDispatchId(runId, parts[2], attempt);
        if (!parsed.IsValid || parsed.Key != key)
        {
            return false;
        }

        id = parsed;
        return true;
    }

    /// <summary>A deterministic job id for a message of this dispatch (a re-sent message is recognisably the same job).</summary>
    internal Guid JobIdFor(string jobType)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{jobType}|{Key}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (name-based) in the Guid byte layout
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}

/// <summary>The payload of the engine request: the closed set <c>{operation, runId, dispatchAttempt}</c> (envelope.py <c>_PAYLOAD</c>).</summary>
public sealed record IntakeEngineRequestPayload(string Operation, Guid RunId, int DispatchAttempt);

/// <summary>The payload of the scheduled timeout: the same three ids.</summary>
public sealed record IntakeEngineTimeoutPayload(string Operation, Guid RunId, int DispatchAttempt);

/// <summary>
/// What the BFF reads from an engine completion (UNTRUSTED: it comes from the container that parses untrusted documents): the
/// dispatch it names, the outcome and the check outcome, each checked against its closed set. Nothing else in the message is read,
/// and no path in it is ever used (ADR-028).
/// </summary>
public sealed record IntakeEngineCompletion(IntakeEngineDispatchId Dispatch, string Outcome, string? CheckOutcome, int RefusedEdits)
{
    private static readonly string[] Outcomes =
        [IntakeEngineOutcomes.Completed, IntakeEngineOutcomes.PlanDoesNotFit, IntakeEngineOutcomes.InputInvalid, IntakeEngineOutcomes.ContractError];

    /// <summary>The refused-edit count is logged only, so it is bounded.</summary>
    private const int MaxLoggedCount = 10_000;

    /// <summary>Reads the payload; false (and no value) when the dispatch ids or the outcome are not in their closed sets.</summary>
    public static bool TryParse(JsonDocument? payload, out IntakeEngineCompletion? completion)
    {
        completion = null;
        if (payload is null || payload.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var root = payload.RootElement;
        if (!TryString(root, "operation", out var operation) || !TryString(root, "runId", out var runIdText)
            || !TryString(root, "outcome", out var outcome)
            || !root.TryGetProperty("dispatchAttempt", out var attemptElement)
            || attemptElement.ValueKind != JsonValueKind.Number || !attemptElement.TryGetInt32(out var attempt)
            || !Guid.TryParseExact(runIdText, "D", out var runId))
        {
            return false;
        }

        var dispatch = new IntakeEngineDispatchId(runId, operation, attempt);
        if (!dispatch.IsValid || !Outcomes.Contains(outcome, StringComparer.Ordinal))
        {
            return false;
        }

        // checkOutcome is null for a segment and for every outcome but a completed redline; any other value is treated as absent
        // by the handler (a completed redline without a valid check outcome is a contract error, never a pass).
        var check = TryString(root, "checkOutcome", out var checkText)
            && (checkText == IntakeEngineOutcomes.CheckPassed || checkText == IntakeEngineOutcomes.CheckFailed)
            ? checkText
            : null;

        var refused = root.TryGetProperty("refusedEdits", out var refusedElement) && refusedElement.ValueKind == JsonValueKind.Array
            ? Math.Min(refusedElement.GetArrayLength(), MaxLoggedCount)
            : 0;

        completion = new IntakeEngineCompletion(dispatch, outcome, check, refused);
        return true;
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}

/// <summary>The JSON options the engine's messages use (camelCase, as <c>JobSubmissionService</c> serialises a <c>JobContract</c>).</summary>
internal static class IntakeEngineJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>A correlation id the engine accepts (envelope.py <c>_CORRELATION_RE</c>): <c>^[A-Za-z0-9._:|-]{1,128}$</c>.</summary>
    internal static bool IsEngineSafeCorrelationId(string? value) =>
        value is { Length: >= 1 and <= 128 } && value.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or ':' or '|' or '-');
}
