namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — compound-OFF Null-Object peer for
/// <see cref="IComposePdfIntakeSource"/> (§F.1 asymmetric-registration rule / ADR-032). The Compose
/// load endpoint is mapped UNCONDITIONALLY while the real intake source registers inside the compound
/// AI gate (<c>Analysis:Enabled</c> + <c>DocumentIntelligence:Enabled</c>); without this peer, a PDF
/// load on a compound-OFF host would fail DI resolution as a 500 instead of a clear "PDF intake
/// unavailable" outcome. Resolves every parse to null with a loud log — the load path then fails the
/// open with the honest message (sibling precedent: <see cref="NullComposeTemplateSource"/>, 032 F1).
/// </summary>
public sealed class NullComposePdfIntakeSource : IComposePdfIntakeSource
{
    private readonly ILogger<NullComposePdfIntakeSource> _logger;

    public NullComposePdfIntakeSource(ILogger<NullComposePdfIntakeSource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<DocumentLayout?> ParseAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Compose PDF intake requested for {FileName} but AI document parsing is disabled " +
            "(compound gate OFF: Analysis:Enabled + DocumentIntelligence:Enabled required). " +
            "Returning null — the load will fail with a clear 'PDF intake unavailable' outcome.",
            fileName);
        return Task.FromResult<DocumentLayout?>(null);
    }
}
