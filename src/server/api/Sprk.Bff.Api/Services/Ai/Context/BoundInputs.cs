using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// The channel a resolved OPERAND renders through (ADR-043 Move 1). Both channels are produced by
/// the ONE <see cref="PromptSchemaRenderer"/> — the choice is a rendering detail of the operand's
/// source/shape, not a second producer.
/// </summary>
public enum OperandChannel
{
    /// <summary>No operand this invocation (prompt-only / args-less run — the relaxed no-file path).</summary>
    None = 0,

    /// <summary>A large file-grounding document → <c>## Document</c> (the shipped summarize path, unregressed).</summary>
    Document = 1,

    /// <summary>A volatile, per-action-typed structured operand → the single-source <c>## Input</c> channel.</summary>
    Input = 2,
}

/// <summary>
/// The declared-input kind an operand was resolved from (ADR-039 closed vocabulary + provenance,
/// NFR-07 identifier only). Drives observability/telemetry — never a runtime routing judgement.
/// </summary>
public enum OperandKind
{
    None = 0,

    /// <summary>File-sourced grounding document (session files → <see cref="OperandChannel.Document"/>).</summary>
    FileDocument = 1,

    /// <summary>The Action's declared <c>selectionText</c> arg.</summary>
    SelectionText = 2,

    /// <summary>The Action's declared <c>changesText</c> arg.</summary>
    ChangesText = 3,

    /// <summary>The Action's declared <c>documentText</c> arg (args-sourced, not a session file).</summary>
    DocumentText = 4,

    /// <summary>ADR-040 read-by-reference: the operand resolved from a prior <see cref="SessionOutput"/> by key.</summary>
    LedgerResolution = 5,

    /// <summary>A pre-resolved structured operand supplied by the caller (node engine <c>inputBinding</c> / narrator payload — E-12 fit).</summary>
    PreResolved = 6,
}

/// <summary>
/// The typed OPERAND a completion runs over, resolved by <see cref="IContextBinder"/> from an Action's
/// declared inputs. Exactly one of <see cref="Document"/> / <see cref="Input"/> is populated per
/// <see cref="Channel"/> (<see cref="OperandChannel.None"/> populates neither).
/// </summary>
public sealed record ResolvedOperand
{
    /// <summary>The rendering channel (which <see cref="PromptSchemaRenderer"/> section carries the operand).</summary>
    public required OperandChannel Channel { get; init; }

    /// <summary>The declared-input kind the operand came from (identifier/telemetry only, NFR-07).</summary>
    public OperandKind Kind { get; init; } = OperandKind.None;

    /// <summary>The file-grounding document when <see cref="Channel"/> is <see cref="OperandChannel.Document"/>.</summary>
    public DocumentText? Document { get; init; }

    /// <summary>The structured operand element when <see cref="Channel"/> is <see cref="OperandChannel.Input"/>.</summary>
    public JsonElement? Input { get; init; }

    /// <summary>The empty operand (prompt-only / relaxed no-file run).</summary>
    public static readonly ResolvedOperand None = new() { Channel = OperandChannel.None, Kind = OperandKind.None };
}

/// <summary>
/// The output of <see cref="IContextBinder.BindAsync"/>: the grounding CONTEXT (a frozen
/// <see cref="ContextEnvelope"/>, task-015) + the typed OPERAND, plus the fingerprint that was
/// written for the envelope (ADR-040 store-before-render). "One input model" = these two roles
/// resolved by one binder.
/// </summary>
public sealed record BoundInputs
{
    /// <summary>Grounding context (frozen task-015 contract, unchanged). The operand is NOT a slice here.</summary>
    public required ContextEnvelope Context { get; init; }

    /// <summary>The Action's declared operand, resolved into a render channel.</summary>
    public required ResolvedOperand Operand { get; init; }

    /// <summary>
    /// The <see cref="SessionContextFingerprint"/> entry written for <see cref="Context"/> (ids/counts
    /// only, NFR-07), or null when no session coordinates were supplied to write against.
    /// </summary>
    public SessionContextFingerprint? Fingerprint { get; init; }

    /// <summary>
    /// The session AS RETURNED by the fingerprint append (fingerprint-inclusive), or null when no
    /// fingerprint was written. The caller MUST route its output write on top of THIS instance — the
    /// fingerprint write and the output write are both read-modify-write on the session, so writing the
    /// output over a pre-fingerprint snapshot would clobber the just-written fingerprint (ADR-040 append-only).
    /// </summary>
    public ChatSession? UpdatedSession { get; init; }
}

/// <summary>
/// The inputs <see cref="IContextBinder.BindAsync"/> resolves. Carries operand SOURCES (resolved by
/// precedence into one <see cref="ResolvedOperand"/>), grounding CONTEXT sources (assembled into the
/// <see cref="ContextEnvelope"/>), and the session coordinates the fingerprint writes against. All
/// members are optional — the request shape is the UNION that fits all three completion consumers
/// (ADR-043 anti-recurrence: designed against every consumer upfront, not fitted to the first).
/// </summary>
public sealed record ContextBindingRequest
{
    // ── Operand sources (resolved by the precedence documented on ContextBinder.BindAsync) ──

    /// <summary>
    /// A pre-resolved structured operand (node engine <c>inputBinding</c> element / narrator serialized
    /// payload). Highest operand precedence — passes straight through to the <c>## Input</c> channel.
    /// The E-12 fit: consumers 2 and 3 supply THIS; consumer 1 (dispatch) leaves it null.
    /// </summary>
    public JsonElement? PreResolvedInput { get; init; }

    /// <summary>The declared input schema (<see cref="Binding.InputSchemaJson"/>) — the operand vocabulary the args are read against.</summary>
    public string? InputSchemaJson { get; init; }

    /// <summary>Dispatch args (verbatim) carrying declared operand-field values and/or a <c>ledger_resolution</c> reference.</summary>
    public JsonElement? Args { get; init; }

    /// <summary>Prior session outputs (ADR-040 ledger) resolved by key for a <c>ledger_resolution</c> operand.</summary>
    public IReadOnlyList<SessionOutput>? LedgerOutputs { get; init; }

    /// <summary>A file-sourced grounding document (summarize path → <c>## Document</c>). Lowest operand precedence.</summary>
    public DocumentText? FileDocument { get; init; }

    // ── Grounding context sources (assembled into the ContextEnvelope; all optional) ──

    /// <summary>Assembled User-slice fragment (current-turn message + preferences).</summary>
    public string? UserFragment { get; init; }

    /// <summary>Assembled Workspace-slice fragment (environment facts / host workspace).</summary>
    public string? WorkspaceFragment { get; init; }

    /// <summary>Assembled Business-slice fragment (host-record identity + schema card).</summary>
    public string? BusinessFragment { get; init; }

    /// <summary>Server-resolved caller contact id (claims→contact) — reference, not free text.</summary>
    public string? CallerContactId { get; init; }

    /// <summary>Ledger conversation tail as references (ADR-040 facade — never copied payloads).</summary>
    public IReadOnlyList<LedgerEntryReference>? ConversationTail { get; init; }

    /// <summary>Record/User memory-item references (FR-A0-03 / task 016).</summary>
    public IReadOnlyList<MemoryItemReference>? MemoryItems { get; init; }

    // ── Fingerprint sink coordinates (ADR-040 store-before-render; NFR-07 ids/counts only) ──

    /// <summary>Tenant id the fingerprint writes against. Null → no fingerprint written.</summary>
    public string? TenantId { get; init; }

    /// <summary>Session id the fingerprint writes against. Null → no fingerprint written.</summary>
    public string? SessionId { get; init; }

    /// <summary>1-based turn the context grounded (aligns with the <see cref="SessionOutput.Turn"/> the operand's output will take).</summary>
    public int Turn { get; init; }
}
