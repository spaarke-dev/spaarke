using System.Diagnostics;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// Response contract (ebilling-r1 task 107, FR-34). Camel-cased on the wire by the minimal-API defaults.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The three states of the related work product answer.</summary>
public static class RelatedWorkStatus
{
    /// <summary><see cref="RelatedWorkDto.Items"/> holds at least one document.</summary>
    public const string Available = "available";

    /// <summary>Nothing to show; <see cref="RelatedWorkDto.Reason"/> says why. Not an error (FR-34: an empty state).</summary>
    public const string Empty = "empty";

    /// <summary>A source could not be read or the caller's rights could not be determined; <see cref="RelatedWorkDto.Code"/> says which. No document is shown.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>Why the answer is empty (stable; the UI keys on them).</summary>
public static class RelatedWorkReason
{
    public const string NotAFeeLine = "not-a-fee-line";
    public const string NoTimekeeper = "no-timekeeper";
    public const string NoLinkedContact = "no-linked-contact";
    public const string NoCoreRecord = "no-core-record";
    public const string NoLineDate = "no-line-date";
    public const string NoNarrative = "no-narrative";

    /// <summary>No document on the core record by this author inside the window that the caller can read. Deliberately the same answer when documents exist but are unreadable.</summary>
    public const string NoneInWindow = "none-in-window";

    /// <summary>Readable candidates exist but none is in the files index yet (or none matched), so none could be ranked.</summary>
    public const string NoneIndexed = "none-indexed";
}

/// <param name="DocumentId">The <c>sprk_document</c> id.</param>
/// <param name="Name">The document's <c>sprk_documentname</c>.</param>
/// <param name="CreatedOn">The document's <c>createdon</c> (UTC), the date the window was applied to.</param>
/// <param name="Score">The search's combined relevance score for the line narrative (higher is more similar).</param>
public sealed record RelatedWorkItem(Guid DocumentId, string? Name, DateTime CreatedOn, double Score);

/// <summary>The related work product of one fee line (FR-34).</summary>
/// <param name="Reason">Set when <paramref name="Status"/> is <c>empty</c>.</param>
/// <param name="Code">Set when <paramref name="Status"/> is <c>unavailable</c> (<c>related-work.*</c>).</param>
/// <param name="WindowDays">The settings window (plus or minus days around the line date) that was applied; null when it never got that far.</param>
/// <param name="Truncated">More documents the CALLER can read than <see cref="InvoiceRelatedWorkService.MaxCandidates"/> exist; the most recent were considered. Never set by documents the caller cannot read.</param>
public sealed record RelatedWorkDto(
    Guid InvoiceId,
    Guid LineId,
    string Status,
    string? Reason,
    string? Code,
    string? Message,
    int? WindowDays,
    bool Truncated,
    IReadOnlyList<RelatedWorkItem> Items);

/// <summary>
/// The Invoice Review "Related work" pane (FR-34): for one fee line, the documents on the line's core record created within the line date
/// plus or minus the settings window, authored by the line's timekeeper (through the timekeeper's linked external contact), ranked by
/// semantic similarity to the line narrative. On demand, read-only, deterministic apart from the ranking.
/// </summary>
/// <remarks>
/// <para><b>Scope is the line, never the request.</b> The caller supplies an invoice and a line. The route has authorized Read on the invoice as
/// the caller; the line must belong to that invoice (otherwise <c>null</c>, the uniform 404). The core record is the line's OWN lookup
/// (<c>sprk_matter</c> / <c>sprk_project</c> / <c>sprk_regardingworkassignment</c>, D-75), the author is the line's roster timekeeper's linked
/// contact; no core id, container id, author or document id is accepted from the request.</para>
/// <para><b>Candidates come from Dataverse, search only ranks them.</b> The files index carries no author field and no reliable core-record
/// filter for the window, so the candidate set is built from <c>sprk_document</c> (core record lookup, <c>sprk_createdbycontact</c>,
/// <c>createdon</c> window), each candidate is checked for Read AS THE CALLER (<see cref="AuthorizationService.GetCallerRecordAccessAsync"/> on
/// <c>sprk_documents</c> with the <c>read</c> operation, the same question <c>AddDocumentAuthorizationFilter("read")</c> asks), and the search runs in
/// <c>documentIds</c> scope over the readable ids only. A document of another core record is never a candidate; an unreadable one is
/// never searched or returned and never counted in any message or flag (an all-unreadable window answers exactly as an empty one). Documents are
/// checked newest first, in pages, until more than <see cref="MaxCandidates"/> are readable or <see cref="MaxCandidateChecks"/> have been asked
/// about, so a run of restricted documents does not hide readable older ones; if the search ranks fewer documents than it could (long documents can
/// take every chunk), the unranked readable ones are searched again, at most <see cref="MaxExtraSearches"/> times.</para>
/// <para><b>Fail closed.</b> No caller id or token, a rights lookup that throws, or one that is <c>Faulted</c> and did not grant Read makes the
/// whole answer <c>unavailable</c> (<c>related-work.access-check-failed</c>); the unfiltered candidate list is never used. A faulted lookup
/// that did grant Read (the degraded read-probe) counts as readable, as the route filters treat it.</para>
/// <para><b>Minimum text (NFR-11, ADR-015).</b> Only the line narrative is sent to search (cut to the search contract's 1000 characters).
/// Logs carry ids, counts and timings, never the narrative or any document text; the response carries no narrative and no snippet.</para>
/// </remarks>
public sealed class InvoiceRelatedWorkService
{
    /// <summary>The most READABLE candidate documents searched (the search contract allows 100). More readable ones than this set <c>truncated</c>.</summary>
    public const int MaxCandidates = 50;

    /// <summary>
    /// The most documents whose Read is asked about (each is one cached rights lookup), newest first. A budget, not a count of anything shown:
    /// when it runs out the answer is the readable ones found so far, flagged exactly as a complete answer would be.
    /// </summary>
    public const int MaxCandidateChecks = 200;

    /// <summary>The most extra searches made for readable candidates the first search did not rank (long documents can take every chunk of one).</summary>
    public const int MaxExtraSearches = 2;

    /// <summary>The most documents returned.</summary>
    public const int MaxResults = 10;

    /// <summary>The settings column allows 0 to 90 days (<c>EbillingSettings.RelatedWorkWindowDays</c>); clamped again here.</summary>
    public const int MaxWindowDays = 90;

    private const int MaxQueryLength = 1000;
    private const string DocumentReadOperation = "read";

    public const string CodeUnavailable = "related-work.unavailable";
    public const string CodeTimeout = "related-work.timeout";
    public const string CodeAccessCheckFailed = "related-work.access-check-failed";
    public const string CodeSearchUnavailable = "related-work.search-unavailable";

    internal const string DocumentEntity = "sprk_document";
    internal const string DocumentName = "sprk_documentname";
    internal const string DocumentCreatedByContact = "sprk_createdbycontact";
    internal const string DocumentMatter = "sprk_matter";
    internal const string DocumentProject = "sprk_project";
    internal const string DocumentWorkAssignment = "sprk_workassignment";
    internal const string TimekeeperLinkedContact = "sprk_contact";
    internal const string LineNarrative = "sprk_description";
    internal const string LineEventDate = "sprk_eventdate";
    internal const string LineResolvedTimekeeper = "sprk_resolvedtimekeeper";
    internal const string LineMatter = "sprk_matter";
    internal const string LineProject = "sprk_project";
    internal const string LineWorkAssignment = "sprk_regardingworkassignment";

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly AuthorizationService _authorization;
    private readonly ISemanticSearchService _search;
    private readonly ILogger<InvoiceRelatedWorkService> _logger;

    public InvoiceRelatedWorkService(
        IGenericEntityService entities,
        IEbillingSettingsReader settings,
        AuthorizationService authorization,
        ISemanticSearchService search,
        ILogger<InvoiceRelatedWorkService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The time the whole answer may take. A source not answered by then makes the answer <c>unavailable</c> (<c>related-work.timeout</c>).</summary>
    internal TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The related work product of <paramref name="lineId"/>, or <c>null</c> when the line does not exist or does not belong to
    /// <paramref name="invoiceId"/> (the uniform 404). <paramref name="caller"/> is who is asking; <paramref name="tenantId"/> is the caller's
    /// tenant (the search is tenant-isolated).
    /// </summary>
    public async Task<RelatedWorkDto?> GetAsync(
        Guid invoiceId, Guid lineId, InvoiceContextService.InvoiceCaller caller, string? tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var stopwatch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Deadline);

        try
        {
            var result = await BuildAsync(invoiceId, lineId, caller, tenantId, deadline.Token);
            _logger.LogInformation(
                "Related work for line {LineId} of invoice {InvoiceId}: {Status} {Reason}{Code}, {Count} documents in {ElapsedMs} ms",
                lineId, invoiceId, result?.Status, result?.Reason, result?.Code, result?.Items.Count ?? 0, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            var timedOut = deadline.IsCancellationRequested;
            _logger.LogWarning(ex, "Related work for line {LineId} of invoice {InvoiceId} is unavailable (timedOut={TimedOut})", lineId, invoiceId, timedOut);
            return Unavailable(invoiceId, lineId, timedOut ? CodeTimeout : CodeUnavailable, null,
                timedOut ? "Related work took too long to load." : "Related work could not be loaded.");
        }
    }

    private async Task<RelatedWorkDto?> BuildAsync(
        Guid invoiceId, Guid lineId, InvoiceContextService.InvoiceCaller caller, string? tenantId, CancellationToken ct)
    {
        // 1. The line, which must belong to the authorized invoice.
        var line = await ReadLineAsync(invoiceId, lineId, ct);
        if (line is null)
        {
            return null;
        }

        // 2. The cases the answer is empty by construction (FR-34: fee lines only, with an author and a place to look).
        if (line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value != (int)InvoiceLineCostType.Fee)
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NotAFeeLine, null);
        }

        if (line.GetAttributeValue<EntityReference>(LineResolvedTimekeeper) is not { } timekeeperRef)
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NoTimekeeper, null);
        }

        var contactId = await ReadLinkedContactAsync(timekeeperRef.Id, ct);
        if (contactId is null)
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NoLinkedContact, null);
        }

        // FR-05: exactly one core record. None or two at once leaves nowhere unambiguous to look.
        var cores = CoreOf(line);
        if (cores.Count != 1)
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NoCoreRecord, null);
        }

        var (coreColumn, coreId) = cores[0];

        if (line.GetAttributeValue<DateTime?>(LineEventDate) is not { } eventDate)
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NoLineDate, null);
        }

        var narrative = line.GetAttributeValue<string>(LineNarrative)?.Trim();
        if (string.IsNullOrEmpty(narrative))
        {
            return Empty(invoiceId, lineId, RelatedWorkReason.NoNarrative, null);
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Related work for line {LineId}: the caller's token carries no tenant id, so the tenant-isolated search cannot run", lineId);
            return Unavailable(invoiceId, lineId, CodeSearchUnavailable, null, "Related work could not be searched.");
        }

        // 3. The window (settings, D-59), applied to the document's createdon as whole UTC days, both ends inclusive.
        var settings = await _settings.GetAsync(ct);
        var windowDays = Math.Clamp(settings.RelatedWorkWindowDays, 0, MaxWindowDays);
        var from = DateTime.SpecifyKind(eventDate.Date, DateTimeKind.Utc).AddDays(-windowDays);
        var toExclusive = DateTime.SpecifyKind(eventDate.Date, DateTimeKind.Utc).AddDays(windowDays + 1);

        // 4. Candidates: this core record, this author, this window - newest first, at most the check budget of them.
        var candidates = await ReadCandidatesAsync(coreColumn, coreId, contactId.Value, from, toExclusive, ct);

        // 5. Only what the CALLER can read goes any further. Newest first, until more than MaxCandidates are readable or the budget is spent,
        //    so a run of documents the caller cannot read never hides the readable ones behind it.
        if (string.IsNullOrWhiteSpace(caller.UserId) || string.IsNullOrWhiteSpace(caller.AccessToken))
        {
            _logger.LogWarning("Related work has no caller identity or token to check documents with; nothing is shown");
            return Unavailable(invoiceId, lineId, CodeAccessCheckFailed, windowDays,
                "Your access to the related documents could not be checked, so none are shown.");
        }

        var scan = await CallerReadCheck.ScanAsync(
            _authorization, caller, FinanceAuthorizationFilter.DocumentEntitySet, DocumentReadOperation,
            candidates, c => c.Id, stopAtReadable: MaxCandidates + 1, MaxCandidateChecks, _logger, "Related work", ct);

        if (scan.Failed)
        {
            return Unavailable(invoiceId, lineId, CodeAccessCheckFailed, windowDays,
                "Your access to the related documents could not be checked, so none are shown.");
        }

        if (scan.Readable.Count == 0)
        {
            // Same answer whether the window is empty or holds only documents the caller cannot read (or the budget ran out on them).
            return Empty(invoiceId, lineId, RelatedWorkReason.NoneInWindow, windowDays);
        }

        // "More than we searched" is a fact about documents the caller CAN read; unreadable ones never set it.
        var truncated = scan.Readable.Count > MaxCandidates;
        var readable = scan.Readable.Take(MaxCandidates).ToList();

        // 6. Rank the readable candidates by similarity to the narrative (documentIds scope, the narrative is the only text sent).
        var query = narrative.Length > MaxQueryLength ? narrative[..MaxQueryLength] : narrative;
        var byId = readable.ToDictionary(c => c.Id);
        var best = new Dictionary<Guid, double>();
        var remaining = readable.Select(c => c.Id).ToList();
        var wanted = Math.Min(MaxResults, readable.Count);

        try
        {
            // The search counts CHUNKS, not documents: a few long documents can fill the limit and push the rest out. While fewer documents
            // than wanted are ranked, search again over the readable ones not yet ranked (at most MaxExtraSearches more times).
            for (var round = 0; round <= MaxExtraSearches && remaining.Count > 0; round++)
            {
                var response = await _search.SearchAsync(
                    new SemanticSearchRequest
                    {
                        Query = query,
                        Scope = SearchScope.DocumentIds,
                        DocumentIds = remaining.Select(id => id.ToString("D")).ToList(),
                        Options = new SearchOptions { Limit = 50, IncludeHighlights = false },
                    },
                    tenantId,
                    ct);

                // The search can only return ids it was given; the intersection is kept anyway so nothing outside the readable set can ever surface.
                var found = 0;
                foreach (var hit in response.Results)
                {
                    if (!Guid.TryParse(hit.DocumentId, out var id) || !byId.ContainsKey(id))
                    {
                        continue;
                    }

                    if (best.TryGetValue(id, out var score))
                    {
                        best[id] = Math.Max(score, hit.CombinedScore);
                    }
                    else
                    {
                        best[id] = hit.CombinedScore;
                        found++;
                    }
                }

                if (found == 0 || best.Count >= wanted)
                {
                    break;
                }

                remaining = remaining.Where(id => !best.ContainsKey(id)).ToList();
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Related work search for line {LineId} failed ({Candidates} candidates)", lineId, readable.Count);
            return Unavailable(invoiceId, lineId, CodeSearchUnavailable, windowDays, "Related work could not be searched.");
        }

        var ranked = best
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Key)
            .Take(MaxResults)
            .Select(r => new RelatedWorkItem(r.Key, byId[r.Key].Name, byId[r.Key].CreatedOn, r.Value))
            .ToList();

        return ranked.Count == 0
            ? Empty(invoiceId, lineId, RelatedWorkReason.NoneIndexed, windowDays, truncated)
            : new RelatedWorkDto(invoiceId, lineId, RelatedWorkStatus.Available, null, null, null, windowDays, truncated, ranked);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<Entity?> ReadLineAsync(Guid invoiceId, Guid lineId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceTotalsService.LineEntity)
        {
            ColumnSet = new ColumnSet(
                InvoiceTotalsService.LineCostType, LineNarrative, LineEventDate, LineResolvedTimekeeper, LineMatter, LineProject, LineWorkAssignment),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_billingeventid", ConditionOperator.Equal, lineId);
        query.Criteria.AddCondition(InvoiceTotalsService.LineInvoiceLookup, ConditionOperator.Equal, invoiceId);

        var page = await _entities.RetrieveMultipleAsync(query, ct);
        return page.Entities.FirstOrDefault();
    }

    private async Task<Guid?> ReadLinkedContactAsync(Guid timekeeperId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceContextService.TimekeeperEntity)
        {
            ColumnSet = new ColumnSet(TimekeeperLinkedContact),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_timekeeperid", ConditionOperator.Equal, timekeeperId);

        var page = await _entities.RetrieveMultipleAsync(query, ct);
        return page.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>(TimekeeperLinkedContact)?.Id;
    }

    private static List<(string Column, Guid Id)> CoreOf(Entity line)
    {
        var cores = new List<(string, Guid)>();
        foreach (var (lineColumn, documentColumn) in new[]
                 {
                     (LineMatter, DocumentMatter), (LineProject, DocumentProject), (LineWorkAssignment, DocumentWorkAssignment),
                 })
        {
            if (line.GetAttributeValue<EntityReference>(lineColumn) is { } reference)
            {
                cores.Add((documentColumn, reference.Id));
            }
        }

        return cores;
    }

    private sealed record Candidate(Guid Id, string? Name, DateTime CreatedOn);

    private async Task<IReadOnlyList<Candidate>> ReadCandidatesAsync(
        string coreColumn, Guid coreId, Guid contactId, DateTime from, DateTime toExclusive, CancellationToken ct)
    {
        var query = new QueryExpression(DocumentEntity)
        {
            ColumnSet = new ColumnSet(DocumentName, "createdon", coreColumn, DocumentCreatedByContact),
            TopCount = MaxCandidateChecks,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(coreColumn, ConditionOperator.Equal, coreId);
        query.Criteria.AddCondition(DocumentCreatedByContact, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, from);
        query.Criteria.AddCondition("createdon", ConditionOperator.LessThan, toExclusive);

        // Most recent first; the id is the tiebreak so equal timestamps keep one order.
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("sprk_documentid", OrderType.Ascending);

        var page = await _entities.RetrieveMultipleAsync(query, ct);

        // The query is the authority on the core record and the author; they are re-checked on the rows so that a row of another core record
        // or another author can never become a candidate, whatever the source returns.
        return page.Entities
            .Where(e => e.GetAttributeValue<EntityReference>(coreColumn)?.Id == coreId
                        && e.GetAttributeValue<EntityReference>(DocumentCreatedByContact)?.Id == contactId)
            .Select(e => new Candidate(e.Id, e.GetAttributeValue<string>(DocumentName), e.GetAttributeValue<DateTime>("createdon")))
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Answers
    // ═══════════════════════════════════════════════════════════════════════════

    private static RelatedWorkDto Empty(Guid invoiceId, Guid lineId, string reason, int? windowDays, bool truncated = false) =>
        new(invoiceId, lineId, RelatedWorkStatus.Empty, reason, null, null, windowDays, truncated, []);

    private static RelatedWorkDto Unavailable(
        Guid invoiceId, Guid lineId, string code, int? windowDays, string message, bool truncated = false) =>
        new(invoiceId, lineId, RelatedWorkStatus.Unavailable, null, code, message, windowDays, truncated, []);
}
