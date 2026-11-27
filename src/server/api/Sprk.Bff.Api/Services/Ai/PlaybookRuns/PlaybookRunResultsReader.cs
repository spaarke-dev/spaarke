using System.Text.Json;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.PlaybookRuns;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037b (D-037-results, D-037-engine; spec FR-05, NFR-02, NFR-03; ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). In the BFF beside the detached runner whose
// IPlaybookRunAi.ReadResultsAsCallerAsync it serves: a static helper (no DI registration, no package, no background work).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: AnalysisEndpoints.ReadAnalysisAsCallerAsync (spaarke_get_analysis) reads the analysis through
//     IAnalysisOrchestrationService.GetAnalysisAsync, whose loader reads the row app-only (AnalysisDocumentLoader) and a Redis copy; the
//     analysis-read rule (AnalysisAuthorizationFilter.IsAnalysisReadableAsync) and IDataverseUserClient are reused as they are.
//   Extension: the route's read cannot be reused, because task 037's constraint forbids an app-only read of output content; the rule is
//     reused unchanged and only the row read is the caller's own OBO GET. The detached runner (698 lines, the run table) keeps the owner
//     check and the run link; the reads of the results live here so the runner keeps one responsibility.
//   Cost of doing nothing: a Copilot run's results, saved to the caller's analysis by task 037a, cannot be shown in Copilot (owner Q17).
// =====================================================================================================================

/// <summary>
/// Reads a playbook run's results as the caller (task 037b): the analysis the run saved to (the analysis-read rule, then the caller's own
/// OBO read of the row), the playbook's name and the run's source documents the caller can read. Every refusal is <c>null</c> (the one
/// uniform not-found); a fault throws.
/// </summary>
/// <remarks>
/// <para><b>No app-only read of content.</b> The working document, the analysis name, the playbook name and the document names come only
/// from <see cref="IDataverseUserClient"/>. The analysis-read rule reads the analysis's anchor ids app-only (ids, never content), as it does
/// for <c>spaarke_get_analysis</c>; the external document narrowing re-reads document ids app-only and returns a hide decision only.</para>
/// <para><b>Logs.</b> Status codes, counts and the analysis id at most; never a name, a document's text or the working document.</para>
/// </remarks>
internal static class PlaybookRunResultsReader
{
    /// <summary>The fixed label of this surface in the external narrowing's log lines.</summary>
    internal const string NarrowingSurface = "mcp.playbook-run-results";

    /// <summary>The Web API entity set of <c>sprk_analysis</c>.</summary>
    internal const string AnalysisEntitySet = "sprk_analysises";

    internal const string WorkingDocumentColumn = "sprk_workingdocument";
    internal const string AnalysisDocumentColumn = "_sprk_documentid_value";
    internal const string StatusColumn = "statuscode";

    /// <summary>The <c>statuscode</c> the run's save sets when it finalizes the analysis (<c>WorkingDocumentService.FinalizeAnalysisAsync</c>).</summary>
    internal const int FinalizedStatus = 2;

    /// <summary>Reads the results of <paramref name="run"/>, whose owner and link the caller of this method has already established.</summary>
    internal static async Task<PlaybookRunResults?> ReadAsync(
        HttpContext http,
        PlaybookRunView run,
        PlaybookRunLink link,
        IPlaybookRunAi runs,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var dataverse = http.RequestServices?.GetService<IDataverseUserClient>()
                        ?? throw new InvalidOperationException("The user-context Dataverse client is not available.");
        var pending = run.Phase == PlaybookRunPhase.Running;
        var documentIds = link.DocumentIds.Where(id => id != Guid.Empty).Distinct().ToArray();

        if (link.Regarding is { } regarding
            && string.Equals(regarding.EntityLogicalName, PlaybookRunRecordStore.AnalysisEntity, StringComparison.OrdinalIgnoreCase))
        {
            return await ReadAnalysisResultsAsync(http, dataverse, run, regarding.RecordId, documentIds, pending, runs, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        // No analysis (a run started before task 037a, or outside the Copilot start): its first document is its subject. A caller who can no
        // longer read it gets the same not-found as an unknown run; a run with no documents has no subject to check.
        var (listed, moreDocuments) = Capped(documentIds);
        var documents = await ReadDocumentsAsync(http, dataverse, listed, runs, logger, cancellationToken).ConfigureAwait(false);
        if (documentIds.Length > 0 && !documents.Any(d => d.DocumentId == documentIds[0]))
        {
            return null;
        }

        var playbookName = await ReadPlaybookNameAsync(dataverse, run.PlaybookId, cancellationToken).ConfigureAwait(false);
        return new PlaybookRunResults(
            run,
            pending ? PlaybookRunResultsState.Pending : PlaybookRunResultsState.None,
            AnalysisId: null,
            playbookName,
            WorkingDocument: null,
            pending ? [] : documents,
            runs.RunConsoleLink(http, documentIds.Length > 0 ? documentIds[0] : null),
            pending ? 0 : moreDocuments);
    }

    /// <summary>
    /// Task 037 review F-D: the first <see cref="PlaybookRunLimits.MaxDocuments"/> of a run's documents (the only ones queried and listed),
    /// and how many more the run has. A Copilot run never has more; a REST run has no cap.
    /// </summary>
    internal static (Guid[] Listed, int More) Capped(Guid[] documentIds) =>
        documentIds.Length <= PlaybookRunLimits.MaxDocuments
            ? (documentIds, 0)
            : (documentIds[..PlaybookRunLimits.MaxDocuments], documentIds.Length - PlaybookRunLimits.MaxDocuments);

    private static async Task<PlaybookRunResults?> ReadAnalysisResultsAsync(
        HttpContext http,
        IDataverseUserClient dataverse,
        PlaybookRunView run,
        Guid analysisId,
        Guid[] documentIds,
        bool pending,
        IPlaybookRunAi runs,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // The analysis-read rule, as spaarke_get_analysis and GET /api/ai/analysis/{id} apply it: Read on every anchor (the run's document),
        // the creator branch for an analysis with none, and the external document narrowing. A refusal is the uniform not-found.
        if (!await AnalysisAuthorizationFilter.IsAnalysisReadableAsync(http, analysisId, logger).ConfigureAwait(false))
        {
            return null;
        }

        // The caller's own read of the row: Dataverse security on the analysis itself. The working document is read only once the run
        // has finished (a running run returns its state, no content).
        var select = pending
            ? $"sprk_name,{AnalysisDocumentColumn}"
            : $"sprk_name,{AnalysisDocumentColumn},{WorkingDocumentColumn},{StatusColumn}";
        var response = await dataverse.GetAsync($"{AnalysisEntitySet}({analysisId:D})?$select={select}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (IsRefusal(response))
            {
                logger.LogInformation("Playbook run results: the caller's read of the analysis was refused - Status: {Status}", response.StatusCode);
                return null;
            }

            throw new InvalidOperationException($"The caller's read of the analysis failed with status {response.StatusCode} ({response.ErrorCode}).");
        }

        var row = response.Body;
        var anchor = ReadGuid(row, AnalysisDocumentColumn);
        var workingDocument = pending ? null : ReadString(row, WorkingDocumentColumn);
        var state = pending
            ? PlaybookRunResultsState.Pending
            : !string.IsNullOrWhiteSpace(workingDocument) ? PlaybookRunResultsState.Available
            : IsUnsaved(run, row) ? PlaybookRunResultsState.Unsaved
            : PlaybookRunResultsState.None;

        // The analysis's own document first, then the run's others, each shown only when the caller reads it.
        var ordered = anchor is { } first ? documentIds.Prepend(first).Distinct().ToArray() : documentIds;
        var (listed, moreDocuments) = Capped(ordered);
        var documents = pending
            ? []
            : await ReadDocumentsAsync(http, dataverse, listed, runs, logger, cancellationToken).ConfigureAwait(false);
        var playbookName = await ReadPlaybookNameAsync(dataverse, run.PlaybookId, cancellationToken).ConfigureAwait(false);
        var consoleLink = http.RequestServices?.GetService<HandoffUrlBuilder>()?.BuildAnalysisWorkspaceUrl(analysisId, anchor ?? Guid.Empty);

        return new PlaybookRunResults(
            run,
            state,
            analysisId,
            playbookName,
            state == PlaybookRunResultsState.Available ? workingDocument : null,
            documents,
            consoleLink,
            pending ? 0 : moreDocuments);
    }

    /// <summary>
    /// The documents of <paramref name="documentIds"/> the caller can read, in that order: ONE OBO query (Dataverse returns only the rows the
    /// caller may read), then the external document narrowing (an internal systemuser is not narrowed and nothing is read for them).
    /// </summary>
    private static async Task<IReadOnlyList<PlaybookRunSourceDocument>> ReadDocumentsAsync(
        HttpContext http,
        IDataverseUserClient dataverse,
        Guid[] documentIds,
        IPlaybookRunAi runs,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (documentIds.Length == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", documentIds.Select(id => $"sprk_documentid eq {id:D}"));
        var response = await dataverse
            .GetAsync($"sprk_documents?$select=sprk_documentid,sprk_documentname&$filter={filter}", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (IsRefusal(response))
            {
                return [];
            }

            throw new InvalidOperationException($"The caller's read of the run's documents failed with status {response.StatusCode} ({response.ErrorCode}).");
        }

        var names = new Dictionary<Guid, string>();
        if (response.Body is { ValueKind: JsonValueKind.Object } body
            && body.TryGetProperty("value", out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (ReadGuid(row, "sprk_documentid") is { } id)
                {
                    names[id] = ReadString(row, "sprk_documentname") ?? string.Empty;
                }
            }
        }

        var readable = documentIds.Where(names.ContainsKey).ToArray();
        if (readable.Length == 0)
        {
            return [];
        }

        var visibility = await InternalPlaneDocumentNarrowing
            .ForCallerAsync(http, readable.Select(id => (string?)id.ToString("D")), NarrowingSurface, logger)
            .ConfigureAwait(false);

        return readable
            .Where(id => visibility.IsShown(id.ToString("D")))
            .Select(id => new PlaybookRunSourceDocument(id, names[id], runs.RunConsoleLink(http, id)))
            .ToArray();
    }

    /// <summary>The playbook's name as the caller reads it; <c>null</c> when the read is refused or fails (the name is a label, not the subject).</summary>
    private static async Task<string?> ReadPlaybookNameAsync(IDataverseUserClient dataverse, Guid playbookId, CancellationToken cancellationToken)
    {
        if (playbookId == Guid.Empty)
        {
            return null;
        }

        var response = await dataverse.GetAsync($"sprk_analysisplaybooks({playbookId:D})?$select=sprk_name", cancellationToken).ConfigureAwait(false);
        return response.IsSuccess ? ReadString(response.Body, "sprk_name") : null;
    }

    /// <summary>
    /// Task 037 review F-A: a run that COMPLETED and whose analysis (with no working document) was never finalized: its save failed
    /// (<c>AnalysisResultPersistence.SaveNodeRunAsync</c> skips the finalize when the write fails). A failed or cancelled run never
    /// finalizes, so it stays "none" (its status already says it failed); an analysis whose status is not read stays "none".
    /// </summary>
    private static bool IsUnsaved(PlaybookRunView run, JsonElement? row) =>
        run.Phase == PlaybookRunPhase.Completed
        && ReadInt(row, StatusColumn) is { } status
        && status != FinalizedStatus;

    /// <summary>A refusal by Dataverse security (403) or a row the caller cannot see (404): the uniform not-found, never a fault.</summary>
    private static bool IsRefusal(DataverseUserResponse response) =>
        response.StatusCode is StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound;

    private static string? ReadString(JsonElement? row, string column) =>
        row is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(column, out var cell)
        && cell.ValueKind == JsonValueKind.String
            ? cell.GetString()
            : null;

    private static int? ReadInt(JsonElement? row, string column) =>
        row is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(column, out var cell)
        && cell.ValueKind == JsonValueKind.Number
        && cell.TryGetInt32(out var number)
            ? number
            : null;

    private static Guid? ReadGuid(JsonElement? row, string column) =>
        Guid.TryParse(ReadString(row, column), out var id) && id != Guid.Empty ? id : null;
}
