using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — <see cref="IComposePdfIntakeSource"/> implementation:
/// a thin composition over the EXISTING parse stack (<see cref="DocumentParserRouter"/> →
/// <see cref="DocumentIntelligenceService"/> → <c>ITextExtractor</c> <c>prebuilt-layout</c>), and
/// nothing more (spec §11 no parallel PDF subsystem). Never throws to the caller: any failure is
/// logged loudly and surfaces as null ("layout unavailable"), which the Compose load path converts
/// into a clear user-facing failure — never a silent empty document (honest-lossiness principle).
/// </summary>
public sealed class ComposePdfIntakeSource : IComposePdfIntakeSource
{
    private readonly DocumentParserRouter _parserRouter;
    private readonly ILogger<ComposePdfIntakeSource> _logger;

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
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            return await _parserRouter.ParseDocumentLayoutAsync(
                pdfBytes, fileName, "application/pdf", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller-initiated cancellation propagates
        }
        catch (Exception ex)
        {
            // Loud, not silent: the intake degrades to "unavailable" (null) and the Compose load
            // path fails the open with a clear message — never mounts an empty editor over a
            // non-empty PDF.
            _logger.LogWarning(ex,
                "Compose PDF intake: structured layout extraction failed for {FileName}", fileName);
            return null;
        }
    }
}
