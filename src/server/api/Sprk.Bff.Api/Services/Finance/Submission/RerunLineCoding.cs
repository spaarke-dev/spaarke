using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (EB-D-186; owner Q3): the coding step of the re-run after Set firm. AI coding runs only on the lines whose code
/// set changes once the firm's own task codes count. Every other line keeps the coding intake stored, its observations and quotes included.
/// </summary>
/// <remarks>
/// <para><b>Which lines change.</b> Intake coded a firmless submission without FR-22 step 1 (task 095c-3), so the firm can change only that
/// step. <see cref="CodeSetResolver.ResolveFirmCodesAsync"/> asks it alone (Dataverse reads, no AI). When it names no set, or the set a line was
/// coded with, the line keeps its stored coding. When it names another set, the line is re-coded through the processor's own
/// <see cref="ILineCodingService"/> (047: the resolver, the <c>invoice-line-code</c> Action and the guard). No other AI path is used.</para>
/// <para><b>An uncoded invoice</b> (D-126) is re-coded only when step 1 now names a set. Otherwise its stored coding hold is raised again, as
/// the exception 047 raised at intake, so the processor holds it exactly as intake did.</para>
/// <para><b>Logs</b>: counts and the set id only (ADR-015).</para>
/// </remarks>
internal sealed class RerunLineCoding : ILineCodingService
{
    private readonly ILineCodingService _coding;
    private readonly Func<IReadOnlyList<InvoiceLineToCode>, CancellationToken, Task<ResolvedCodeSet?>> _firmCodeSet;
    private readonly StoredCoding _stored;
    private readonly ILogger _logger;

    /// <param name="coding">The processor's own coding service (the facade over the AI).</param>
    /// <param name="firmCodeSet">FR-22 step 1 alone: <see cref="CodeSetResolver.ResolveFirmCodesAsync"/>.</param>
    /// <param name="stored">The coding intake stored (<see cref="StoredSubmissionRead"/>).</param>
    /// <param name="logger">Counts only.</param>
    internal RerunLineCoding(
        ILineCodingService coding,
        Func<IReadOnlyList<InvoiceLineToCode>, CancellationToken, Task<ResolvedCodeSet?>> firmCodeSet,
        StoredCoding stored,
        ILogger logger)
    {
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _firmCodeSet = firmCodeSet ?? throw new ArgumentNullException(nameof(firmCodeSet));
        _stored = stored ?? throw new ArgumentNullException(nameof(stored));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The number of lines this step sent to <see cref="ILineCodingService"/> (0 when no code set changed).</summary>
    internal int RecodedLineCount { get; private set; }

    /// <inheritdoc />
    public async Task<InvoiceCodingResult> CodeLinesAsync(LineCodingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var firmSet = request.IncludeFirmCodes ? await _firmCodeSet(request.Lines, cancellationToken).ConfigureAwait(false) : null;

        if (_stored.Coded is not { } coded)
        {
            if (firmSet is null)
            {
                // No set before, none now: the stored hold, raised as 047 raised it (the processor maps it to the same pipeline finding).
                _logger.LogInformation("Re-run coding: the invoice stays uncoded ({HoldReason}); no line re-coded.", _stored.HoldReasonCode);
                throw new LineCodingException(
                    _stored.HoldReasonCode == SubmissionReasonCodes.CodingNoCodeSetHold ? LineCodingException.NoCodeSet : _stored.HoldCause!,
                    "The stored coding hold of the invoice still applies.");
            }

            return await RecodeAsync(request, request.Lines, firmSet, cancellationToken).ConfigureAwait(false);
        }

        var storedByKey = coded.Lines.ToDictionary(l => l.LineKey, StringComparer.Ordinal);
        if (request.Lines.Any(l => !storedByKey.ContainsKey(l.LineKey)))
        {
            throw new InvalidOperationException("The stored coding has no entry for a line of the re-run.");
        }

        var changed = firmSet is null
            ? []
            : request.Lines.Where(l => storedByKey[l.LineKey].CodeSetId != firmSet.CodeSetId).ToList();
        if (changed.Count == 0)
        {
            _logger.LogInformation("Re-run coding: no line's code set changes ({LineCount} lines keep their stored coding).", request.Lines.Count);
            return coded;
        }

        var recoded = await RecodeAsync(request, changed, firmSet!, cancellationToken).ConfigureAwait(false);
        var recodedByKey = recoded.Lines.ToDictionary(l => l.LineKey, StringComparer.Ordinal);
        return new InvoiceCodingResult
        {
            CodeSet = recoded.CodeSet,
            Lines = request.Lines.Select(l => recodedByKey.TryGetValue(l.LineKey, out var line) ? line : storedByKey[l.LineKey]).ToList(),
            ChunkCount = recoded.ChunkCount,
        };
    }

    private async Task<InvoiceCodingResult> RecodeAsync(
        LineCodingRequest request, IReadOnlyList<InvoiceLineToCode> lines, ResolvedCodeSet firmSet, CancellationToken ct)
    {
        RecodedLineCount = lines.Count;
        var result = await _coding.CodeLinesAsync(request with { Lines = lines }, ct).ConfigureAwait(false);

        // The service resolves the set again (step 1 first); a different answer means the reference data moved between the two reads. A fault,
        // never a mix of sets: the evaluation writes nothing and the job retries.
        if (result.CodeSet.CodeSetId != firmSet.CodeSetId)
        {
            throw new InvalidOperationException("The code set resolved for the re-coded lines differs from the firm's code set.");
        }

        _logger.LogInformation("Re-run coding: {Recoded} of {LineCount} lines re-coded with code set {CodeSetId}.", lines.Count, request.Lines.Count, firmSet.CodeSetId);
        return result;
    }
}
