using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Task 073 (spaarkeai-compose-r7, FR-11 / LOW-10) — the runtime-failure causes an intake attempt can
/// hit, distinguished so a caller can surface a cause-specific message instead of one collapsed
/// "unavailable" outcome. Deliberately narrow: these are the causes observable from the EXISTING parse
/// stack's error text (Azure Document Intelligence circuit breaker / timeout / bad-format responses —
/// see <c>TextExtractorService.ExtractLayoutAsync</c>) without forking <c>Services/Ai</c> internals to
/// expose a typed cause. This is NOT the ADR-032 gate-off case — that stays on
/// <see cref="NullComposePdfIntakeSource"/>, a separate class with its own distinct "AI document parsing
/// is disabled" log line, untouched by this task.
/// </summary>
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
    /// causes above).</summary>
    Unknown,
}

/// <summary>
/// Task 073 — the discriminated result <see cref="ComposePdfIntakeSource.ParseWithDiagnosticsAsync"/>
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

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — <see cref="IComposePdfIntakeSource"/> implementation:
/// a thin composition over the EXISTING parse stack (<see cref="DocumentParserRouter"/> →
/// <see cref="DocumentIntelligenceService"/> → <c>ITextExtractor</c> <c>prebuilt-layout</c>), and
/// nothing more (spec §11 no parallel PDF subsystem). Never throws to the caller: any failure is
/// logged loudly and surfaces as null ("layout unavailable") via <see cref="ParseAsync"/>, which the
/// Compose load path converts into a clear user-facing failure — never a silent empty document
/// (honest-lossiness principle).
/// </summary>
/// <remarks>
/// Task 073 (FR-11 / LOW-10): <see cref="ParseAsync"/>'s null boundary used to collapse every failure
/// — circuit-open, timeout, corrupt file, anything else — into one generic logged message. That
/// discrimination now happens in <see cref="ParseWithDiagnosticsAsync"/>, which classifies the
/// exception's message text (the only signal the EXISTING stack exposes without forking
/// <c>DocumentIntelligenceService</c> / <c>TextExtractorService</c> internals — see
/// <c>TextExtractorService.ExtractLayoutAsync</c>'s distinct Failed() wordings for circuit-open,
/// timeout, and bad-format responses) into a <see cref="PdfIntakeFailureCause"/> + cause-specific
/// message, returned as a <see cref="PdfIntakeParseResult"/>. <see cref="ParseAsync"/> (the
/// <see cref="IComposePdfIntakeSource"/> member — PublicContracts, not owned by this task) delegates to
/// it unchanged in shape: still never throws (beyond caller-cancellation), still returns null on any
/// failure, so existing callers (<c>ComposeService.ProjectPdfToDocxAsync</c>) keep compiling and
/// behaving exactly as before. <see cref="ParseWithDiagnosticsAsync"/> is the new, additive surface for
/// callers that want the cause-specific message — wiring a consumer to it is a follow-on, not part of
/// this task (scope: this one file — see task 073 POML outputs).
/// </remarks>
public sealed class ComposePdfIntakeSource : IComposePdfIntakeSource
{
    private readonly DocumentParserRouter _parserRouter;
    private readonly ILogger<ComposePdfIntakeSource> _logger;

    // Substring markers matched against the wrapping InvalidOperationException's message text (see
    // DocumentIntelligenceService.ParseDocumentLayoutAsync, which prefixes the underlying
    // LayoutExtractionResult.ErrorMessage verbatim). Ordered — first match wins. Sourced from
    // TextExtractorService.ExtractLayoutAsync's own Failed() wordings (Services/Ai internals we do NOT
    // fork — we only read the text they already produce).
    private static readonly (string Marker, PdfIntakeFailureCause Cause)[] FailureMarkers =
    {
        ("temporarily unavailable due to repeated service failures", PdfIntakeFailureCause.CircuitOpen),
        ("circuit breaker", PdfIntakeFailureCause.CircuitOpen),
        ("took too long", PdfIntakeFailureCause.Timeout),
        ("timed out", PdfIntakeFailureCause.Timeout),
        ("invalid or unsupported", PdfIntakeFailureCause.Corrupt),
        ("format is invalid", PdfIntakeFailureCause.Corrupt),
    };

    public ComposePdfIntakeSource(
        DocumentParserRouter parserRouter,
        ILogger<ComposePdfIntakeSource> logger)
    {
        _parserRouter = parserRouter ?? throw new ArgumentNullException(nameof(parserRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DocumentLayout?> ParseAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var result = await ParseWithDiagnosticsAsync(pdfBytes, fileName, cancellationToken)
            .ConfigureAwait(false);
        return result.Layout;
    }

    /// <summary>
    /// Task 073 — the discriminated twin of <see cref="ParseAsync"/>: same parse attempt, same
    /// never-throws-except-cancellation contract, but on failure returns a classified
    /// <see cref="PdfIntakeFailureCause"/> + cause-specific message instead of collapsing to null.
    /// </summary>
    public async Task<PdfIntakeParseResult> ParseWithDiagnosticsAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            var layout = await _parserRouter.ParseDocumentLayoutAsync(
                pdfBytes, fileName, "application/pdf", cancellationToken);
            return PdfIntakeParseResult.Success(layout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller-initiated cancellation propagates
        }
        catch (Exception ex)
        {
            var (cause, message) = ClassifyFailure(ex, fileName);

            // Loud, not silent: the intake degrades to a cause-specific failure and the Compose load
            // path fails the open with a clear message — never mounts an empty editor over a
            // non-empty PDF.
            _logger.LogWarning(ex,
                "Compose PDF intake: structured layout extraction failed for {FileName} (cause={Cause})",
                fileName, cause);

            return PdfIntakeParseResult.Failure(cause, message);
        }
    }

    /// <summary>
    /// Classifies an intake failure by matching the exception's message text against the known,
    /// distinct wordings the EXISTING parse stack already produces per cause (see
    /// <see cref="FailureMarkers"/>). Any exception that matches none of them returns
    /// <see cref="PdfIntakeFailureCause.Unknown"/> with the same collapsed wording the facade produced
    /// before task 073 — an unrecognized cause is never mis-attributed to one of the three named ones.
    /// </summary>
    private static (PdfIntakeFailureCause Cause, string Message) ClassifyFailure(Exception ex, string fileName)
    {
        var text = ex.Message ?? string.Empty;

        foreach (var (marker, cause) in FailureMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (cause, BuildMessage(cause, fileName));
            }
        }

        return (PdfIntakeFailureCause.Unknown, BuildMessage(PdfIntakeFailureCause.Unknown, fileName));
    }

    private static string BuildMessage(PdfIntakeFailureCause cause, string fileName) => cause switch
    {
        PdfIntakeFailureCause.CircuitOpen =>
            $"PDF intake for '{fileName}' is temporarily unavailable: the document-parsing service " +
            "circuit breaker is open after repeated failures. Please try again in a few minutes.",

        PdfIntakeFailureCause.Timeout =>
            $"PDF intake for '{fileName}' timed out: the document-parsing service did not respond " +
            "within the configured timeout. Please try again.",

        PdfIntakeFailureCause.Corrupt =>
            $"PDF intake for '{fileName}' failed: the file appears to be corrupt or in an unsupported " +
            "format.",

        _ =>
            $"PDF intake failed: the document layout could not be extracted from '{fileName}'. " +
            "The file may be corrupt or the document-parsing service is unavailable.",
    };
}
