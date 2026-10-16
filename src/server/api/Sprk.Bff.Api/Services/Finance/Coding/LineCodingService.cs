using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>
/// <see cref="ILineCodingService"/>: the orchestration of FR-22 line coding. Resolves the code set once for the invoice
/// (<see cref="CodeSetResolver"/>), sends the lines that have a narrative to <see cref="IInvoiceAi.CodeLinesAsync"/> in chunks of
/// <c>sprk_codingchunksize</c> with the allowed codes and their guidance text, and passes every returned entry through
/// <see cref="LineCodingGuard"/> before it is reported.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunks.</b> Sequential, in input order. The size is the settings value (default 25), clamped to 1-200 (the range the column
/// allows) so a bad value cannot send an empty or unbounded prompt. A chunk's allowed lists are pruned to what its lines need:
/// a chunk of Expense lines sends no task or activity codes, a chunk of Fee lines no expense codes. This is a token saving only;
/// the guard still checks against the full resolved set.
/// </para>
/// <para>
/// <b>What is sent.</b> Lines whose narrative is empty are not sent (the Action cannot code them and an observation needs quoted
/// text); they come back as <see cref="LineCodingStatus.NoNarrative"/> with the firm's codes retained. Narratives and codes are
/// content: they are never logged (ADR-015); logs carry ids, counts and the code-set source only.
/// </para>
/// <para>
/// <b>Tier and model.</b> Nothing here names a model, tier or temperature: they live in the <c>invoice-line-code</c> Action row
/// (D-52, ADR-039). The consumer type is <see cref="ConsumerTypes.InvoiceLineCode"/>, resolved by the facade.
/// </para>
/// </remarks>
public sealed class LineCodingService : ILineCodingService
{
    /// <summary>The Action (and consumer type) code; the D-64 correction record stores it with <see cref="ActionVersion"/>.</summary>
    public const string ActionCode = ConsumerTypes.InvoiceLineCode;

    /// <summary>The Action version (INVOICE-LINE-CODE@v1).</summary>
    public const int ActionVersion = 1;

    internal const int MinChunkSize = 1;
    internal const int MaxChunkSize = 200;

    private readonly CodeSetResolver _resolver;
    private readonly IEbillingSettingsReader _settings;
    private readonly IInvoiceAi _ai;
    private readonly ILogger<LineCodingService> _logger;

    public LineCodingService(
        CodeSetResolver resolver,
        IEbillingSettingsReader settings,
        IInvoiceAi ai,
        ILogger<LineCodingService> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InvoiceCodingResult> CodeLinesAsync(LineCodingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CoreRecord);
        ArgumentNullException.ThrowIfNull(request.Lines);
        RequireUniqueKeys(request.Lines);

        var codeSet = await _resolver.ResolveAsync(request.CoreRecord, request.Lines, cancellationToken).ConfigureAwait(false);
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var chunkSize = Math.Clamp(settings.CodingChunkSize, MinChunkSize, MaxChunkSize);

        var toSend = request.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Narrative)).ToList();
        var returnedByKey = new Dictionary<string, List<InvoiceLineCoding>>(StringComparer.Ordinal);
        var chunkCount = 0;
        var unexpected = 0;

        foreach (var chunk in toSend.Chunk(chunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _ai.CodeLinesAsync(BuildChunkRequest(chunk, codeSet, request.TenantId), cancellationToken)
                .ConfigureAwait(false);
            chunkCount++;

            // Only an entry for a line of THIS chunk counts: the model saw only these narratives, so an entry keyed to any other
            // line was not coded from that line's narrative and must neither be applied to it nor make its own answer ambiguous.
            var chunkKeys = chunk.Select(l => l.LineKey).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in result.Lines)
            {
                if (!chunkKeys.Contains(entry.LineKey))
                {
                    unexpected++;
                    continue;
                }

                if (!returnedByKey.TryGetValue(entry.LineKey, out var entries))
                {
                    returnedByKey[entry.LineKey] = entries = [];
                }

                entries.Add(entry);
            }
        }

        var lines = new List<LineCodingResult>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Narrative))
            {
                lines.Add(LineCodingGuard.Uncoded(line, LineCodingStatus.NoNarrative, codeSet));
            }
            else if (returnedByKey.TryGetValue(line.LineKey, out var entries) && entries.Count == 1)
            {
                lines.Add(LineCodingGuard.Apply(line, entries[0], codeSet));
            }
            else
            {
                lines.Add(LineCodingGuard.Uncoded(line, LineCodingStatus.NotReturned, codeSet));
            }
        }

        _logger.LogInformation(
            "Line coding: {LineCount} lines, {ChunkCount} chunks of up to {ChunkSize}, code set {CodeSetId} via {Source}; "
            + "{Refused} codes refused, {Mismatch} firm-code mismatches, {NotReturned} lines not returned, {NoNarrative} without narrative, "
            + "{Unexpected} entries ignored for lines outside their chunk",
            lines.Count,
            chunkCount,
            chunkSize,
            codeSet.CodeSetId,
            codeSet.Source,
            lines.Sum(l => l.RefusedCodes.Count),
            lines.Count(l => l.HasFirmCodeMismatch),
            lines.Count(l => l.Status == LineCodingStatus.NotReturned),
            lines.Count(l => l.Status == LineCodingStatus.NoNarrative),
            unexpected);

        return new InvoiceCodingResult { CodeSet = codeSet, Lines = lines, ChunkCount = chunkCount };
    }

    private static InvoiceCodeLinesRequest BuildChunkRequest(InvoiceLineToCode[] chunk, ResolvedCodeSet codeSet, string? tenantId)
    {
        var hasFee = chunk.Any(l => l.LineType == InvoiceLineType.Fee);
        var hasExpense = chunk.Any(l => l.LineType == InvoiceLineType.Expense);

        return new InvoiceCodeLinesRequest
        {
            Lines = chunk,
            AllowedTaskCodes = hasFee ? codeSet.TaskCodes : [],
            AllowedActivityCodes = hasFee ? codeSet.ActivityCodes : [],
            AllowedExpenseCodes = hasExpense ? codeSet.ExpenseCodes : [],
            TenantId = tenantId,
        };
    }

    private static void RequireUniqueKeys(IReadOnlyList<InvoiceLineToCode> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.LineKey) || !seen.Add(line.LineKey))
            {
                throw new ArgumentException("Every line needs a unique, non-empty LineKey.", nameof(lines));
            }
        }
    }
}
