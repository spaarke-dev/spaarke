using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 3.7 — attachment→document association (email-communication-intelligence-r1 061 UAT / F1). Closes the
/// gap the UAT surfaced: "the attached file (e.g. <c>PAT 09270W</c>) is already filed to matter X" contributed
/// NOTHING to the email's association. This rung matches an incoming attachment to an EXISTING
/// <c>sprk_document</c> and surfaces that document's OWN record links (<c>sprk_matter</c> /
/// <c>sprk_relatedmatter</c> / <c>sprk_project</c> / <c>sprk_invoice</c> / …) as association candidates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Matching key.</b> Exact <c>sprk_filename</c> match on the incoming attachment name (available on the
/// envelope at capture) — shipping today with no schema change. It ALSO queries the AI-populated
/// <c>sprk_globalsearchextender</c> keyword/summary field (the "augment Power Apps global Document search"
/// project): distinctive attachment tokens are OR'd in as <c>Like</c> filters, so the moment that field is
/// populated this rung matches by document CONTENT too — no code change needed. (Content-hash matching was
/// rejected: no hash field on <c>sprk_document</c>, no attachment bytes on the envelope at capture, and a
/// re-attached file is a new SPE item — see the 061 UAT F1 feasibility note.)
/// </para>
/// <para>
/// <b>Surface-for-review, never auto-file.</b> A document's matter is INDIRECT evidence of the email's matter,
/// so — exactly like <see cref="RecordNameMatch"/> — this rung emits at a suggest-band confidence and its
/// <see cref="RungKind.DocumentAssociation"/> kind is deliberately NOT in the mapper's auto-file-eligible or
/// deterministic-write sets. It can only ADD Suggested candidates for the reviewer; it can never cause an
/// auto-file. Cost-gated (NFR-08: no attachments ⇒ zero queries) and non-fatal (NFR-04: any failure ⇒ no-match).
/// Registered unconditionally (ADR-010).
/// </para>
/// </remarks>
public sealed class AttachmentDocumentAssociationRung : IAssociationRung
{
    private readonly IGenericEntityService _entityService;
    private readonly ILogger<AttachmentDocumentAssociationRung> _logger;

    /// <summary>Suggest-band confidence (below the 0.85 auto-file threshold): an attached document's matter is a
    /// review candidate, never an auto-file (the association is the document's, not proven to be the email's).</summary>
    private const double MatchConfidence = 0.65;

    /// <summary>A filename shorter than this (or a bare-generic one) is too weak a key — skip it.</summary>
    private const int MinFileNameLength = 8;

    private const int MaxAttachments = 10;   // NFR-08 cost cap on distinct filenames queried
    private const int MaxKeywords = 5;        // NFR-08 cost cap on globalsearchextender terms
    private const int MinKeywordLength = 6;   // only distinctive tokens seed the content-field search
    private const int MaxDocuments = 25;      // cap retrieved documents

    /// <summary>Distinctive content tokens for the <c>sprk_globalsearchextender</c> seam.</summary>
    private static readonly Regex KeywordPattern =
        new(@"\b[A-Za-z][A-Za-z0-9\-]{5,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// <c>sprk_document</c> record-link field → the target entity logical name. The communication regarding
    /// field is resolved from <see cref="RegardingFieldMap"/> (ADR-024 source of truth). "Related" links point
    /// at the same target entity as their primary counterpart (a related matter is still a matter).
    /// </summary>
    private static readonly IReadOnlyList<(string DocumentField, string TargetEntity)> DocumentLinkFields =
    [
        ("sprk_matter", "sprk_matter"),
        ("sprk_relatedmatter", "sprk_matter"),
        ("sprk_project", "sprk_project"),
        ("sprk_relatedproject", "sprk_project"),
        ("sprk_invoice", "sprk_invoice"),
        ("sprk_workassignment", "sprk_workassignment"),
    ];

    public AttachmentDocumentAssociationRung(
        IGenericEntityService entityService,
        ILogger<AttachmentDocumentAssociationRung> logger)
    {
        _entityService = entityService;
        _logger = logger;
    }

    public RungKind Kind => RungKind.DocumentAssociation;
    public int Order => 3;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var fileNames = CollectFileNames(message);
        var keywords = CollectKeywords(message);

        // NFR-08 cost gate: nothing to match on ⇒ zero Dataverse work.
        if (fileNames.Count == 0 && keywords.Count == 0)
            return Array.Empty<RungMatch>();

        try
        {
            var startTs = Stopwatch.GetTimestamp();

            var documents = await QueryDocumentsAsync(fileNames, keywords, ct).ConfigureAwait(false);
            var matches = BuildMatches(documents);

            var elapsed = Stopwatch.GetElapsedTime(startTs);
            _logger.LogInformation(
                "Rung 3.7 (attachment→document) fired | FileNames: {FileNames}, Keywords: {Keywords}, Documents: {Docs}, Matches: {Matches}, ElapsedMs: {ElapsedMs}",
                fileNames.Count, keywords.Count, documents.Count, matches.Count, (long)elapsed.TotalMilliseconds);

            return matches;
        }
        catch (Exception ex)
        {
            // NFR-04: a document-lookup failure degrades to no-match; never throws into the capture path.
            _logger.LogWarning(ex, "Rung 3.7 (attachment→document) — document query failed; degrading to no-match.");
            return Array.Empty<RungMatch>();
        }
    }

    // ── Inputs ───────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> CollectFileNames(NormalizedMessage message)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        void Add(string? name)
        {
            var n = name?.Trim();
            if (!string.IsNullOrWhiteSpace(n) && n.Length >= MinFileNameLength && seen.Add(n) && names.Count < MaxAttachments)
                names.Add(n);
        }

        foreach (var a in message.Attachments)
            Add(a.Name);
        foreach (var a in message.AttachmentTexts)
            Add(a.FileName);

        return names;
    }

    private static IReadOnlyList<string> CollectKeywords(NormalizedMessage message)
    {
        // Distinctive tokens from subject + attachment text for the sprk_globalsearchextender content seam.
        // The field is unpopulated today (⇒ these match nothing yet); this wires the capability so it lights up
        // when the "augment global Document search" project populates the field. No effect on filename matching.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keywords = new List<string>();
        var text = string.Join(' ', new[] { message.Subject, message.AttachmentText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(text))
            return keywords;

        try
        {
            foreach (Match m in KeywordPattern.Matches(text))
            {
                var token = m.Value.Trim();
                if (token.Length >= MinKeywordLength && seen.Add(token))
                {
                    keywords.Add(token);
                    if (keywords.Count >= MaxKeywords)
                        break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Defensive (NFR-04): pathological input yields no keywords.
        }

        return keywords;
    }

    // ── Query ────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<Entity>> QueryDocumentsAsync(
        IReadOnlyList<string> fileNames, IReadOnlyList<string> keywords, CancellationToken ct)
    {
        var columns = new List<string> { "sprk_filename", "sprk_documentname", "sprk_globalsearchextender" };
        columns.AddRange(DocumentLinkFields.Select(f => f.DocumentField));

        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            TopCount = MaxDocuments,
        };

        // active documents AND (filename match OR globalsearchextender keyword match)
        query.Criteria = new FilterExpression(LogicalOperator.And);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

        var matchFilter = new FilterExpression(LogicalOperator.Or);
        if (fileNames.Count > 0)
            matchFilter.AddCondition("sprk_filename", ConditionOperator.In, fileNames.Cast<object>().ToArray());
        foreach (var kw in keywords)
            matchFilter.AddCondition("sprk_globalsearchextender", ConditionOperator.Like, $"%{kw}%");
        query.Criteria.AddFilter(matchFilter);

        var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities;
    }

    // ── Map ──────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<RungMatch> BuildMatches(IReadOnlyList<Entity> documents)
    {
        // Dedup by (regarding field, target id): the same matter can be reached via several documents/links.
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        foreach (var doc in documents)
        {
            var docId = doc.Id;
            var docName = doc.GetAttributeValue<string>("sprk_filename")
                          ?? doc.GetAttributeValue<string>("sprk_documentname")
                          ?? docId.ToString("D");

            foreach (var (documentField, targetEntity) in DocumentLinkFields)
            {
                if (doc.GetAttributeValue<EntityReference>(documentField) is not { } link || link.Id == Guid.Empty)
                    continue;

                var regardingField = RegardingFieldMap.FieldFor(targetEntity);
                if (string.IsNullOrWhiteSpace(regardingField))
                    continue;

                var provenance =
                    $"attachment-document:document={docId:D}:name=\"{RungProvenanceFormat.EscapeValue(docName)}\":" +
                    $"link={documentField}:target={targetEntity}";

                var match = new RungMatch
                {
                    RegardingFieldName = regardingField,
                    Target = new EntityReference(targetEntity, link.Id) { Name = link.Name },
                    Confidence = MatchConfidence,
                    Provenance = provenance,
                    Rung = RungKind.DocumentAssociation,
                };

                var key = (regardingField, link.Id);
                if (!best.ContainsKey(key))
                    best[key] = match;
            }
        }

        return best.Values.ToArray();
    }
}
