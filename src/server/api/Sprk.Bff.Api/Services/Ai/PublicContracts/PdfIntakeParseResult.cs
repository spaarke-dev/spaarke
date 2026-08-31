namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Task 073 (spaarkeai-compose-r7, FR-11 / LOW-10) — the runtime-failure causes an intake attempt can
/// hit, distinguished so a caller can surface a cause-specific message instead of one collapsed
/// "unavailable" outcome. Deliberately narrow: these are the causes observable from the EXISTING parse
/// stack's error text (Azure Document Intelligence circuit breaker / timeout / bad-format responses —
/// see <c>TextExtractorService.ExtractLayoutAsync</c>) without forking <c>Services/Ai</c> internals to
/// expose a typed cause. This is NOT the ADR-032 gate-off case — that stays on
/// <see cref="NullComposePdfIntakeSource"/>, whose <see cref="IComposePdfIntakeSource.ParseWithDiagnosticsAsync"/>
/// returns <see cref="Unknown"/> carrying the distinct "AI document parsing is disabled" message (the
/// gate-off universe stays distinct via that MESSAGE, not a fourth-plus-one enum member).
/// </summary>
/// <remarks>
/// Task 050 (FR-11 end-to-end): moved from <c>Services/Ai/ComposePdfIntakeSource.cs</c> into
/// <c>PublicContracts</c> so the facade contract (<see cref="IComposePdfIntakeSource.ParseWithDiagnosticsAsync"/>)
/// is self-contained — <c>Services/Compose</c> (<c>ComposeService</c>) now consumes the cause through the
/// facade namespace only, never reaching an AI-internal type (ADR-013). The move became possible when the
/// facade's prior sole owner (<c>spaarke-ai-architecture-redesign-r2</c>) closed.
/// </remarks>
public enum PdfIntakeFailureCause
{
    /// <summary>The Document Intelligence circuit breaker is open after repeated recent failures;
    /// the caller should retry later rather than immediately.</summary>
    CircuitOpen,

    /// <summary>The parse call exceeded the configured Document Intelligence timeout.</summary>
    Timeout,

    /// <summary>The document itself could not be parsed — invalid, unsupported, or corrupt format.</summary>
    Corrupt,

    /// <summary>An intake failure whose cause did not match a known pattern. Carries the same
    /// collapsed wording the facade produced before task 073 — the safe default for any failure this
    /// classifier cannot yet distinguish (never silently mis-attributed to one of the three named
    /// causes above). Also the cause the ADR-032 gate-off <see cref="NullComposePdfIntakeSource"/> rides,
    /// carrying the distinct "AI document parsing is disabled" message.</summary>
    Unknown,
}

/// <summary>
/// Task 073 — the discriminated result <see cref="IComposePdfIntakeSource.ParseWithDiagnosticsAsync"/>
/// returns: either a successfully-parsed <see cref="Layout"/>, or a <see cref="FailureCause"/> +
/// cause-specific <see cref="FailureMessage"/>. Exactly one of (<see cref="Layout"/>) / (<see
/// cref="FailureCause"/>, <see cref="FailureMessage"/>) is populated.
/// </summary>
public sealed record PdfIntakeParseResult
{
    /// <summary>The parsed layout on success; null on any failure.</summary>
    public DocumentLayout? Layout { get; init; }

    /// <summary>The classified failure cause; null on success.</summary>
    public PdfIntakeFailureCause? FailureCause { get; init; }

    /// <summary>The cause-specific, user-presentable failure message; null on success.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>True when <see cref="Layout"/> was extracted.</summary>
    public bool Succeeded => Layout is not null;

    public static PdfIntakeParseResult Success(DocumentLayout layout) => new() { Layout = layout };

    public static PdfIntakeParseResult Failure(PdfIntakeFailureCause cause, string message) =>
        new() { FailureCause = cause, FailureMessage = message };
}
