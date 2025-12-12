using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.RecordMatching;

/// <summary>
/// Service for matching extracted document entities to Dataverse records using Azure AI Search.
/// </summary>
public class RecordMatchService : IRecordMatchService
{
    private readonly SearchClient _searchClient;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<RecordMatchService> _logger;

    // Scoring weights per spec Section 4.2.3
    private const double ReferenceMatchWeight = 0.50;  // Highest - explicit link
    private const double OrganizationMatchWeight = 0.25;
    private const double PersonMatchWeight = 0.15;
    private const double KeywordMatchWeight = 0.10;

    // Lookup field mapping for Dataverse entity types
    private static readonly Dictionary<string, string> LookupFieldMap = new()
    {
        ["sprk_matter"] = "sprk_matter",
        ["sprk_project"] = "sprk_project",
        ["sprk_invoice"] = "sprk_invoice"
    };

    public RecordMatchService(
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<RecordMatchService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.AiSearchEndpoint))
            throw new InvalidOperationException("AiSearchEndpoint is not configured.");
        if (string.IsNullOrWhiteSpace(_options.AiSearchKey))
            throw new InvalidOperationException("AiSearchKey is not configured.");

        var searchUri = new Uri(_options.AiSearchEndpoint);
        var credential = new AzureKeyCredential(_options.AiSearchKey);
        _searchClient = new SearchClient(searchUri, _options.AiSearchIndexName, credential);
    }

    /// <inheritdoc />
    public async Task<RecordMatchResponse> MatchAsync(RecordMatchRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Searching for record matches: {OrgCount} organizations, {PeopleCount} people, {RefCount} references, filter={Filter}",
            request.Organizations.Count(),
            request.People.Count(),
            request.ReferenceNumbers.Count(),
            request.RecordTypeFilter);

        // Build search text from extracted entities
        var searchTerms = BuildSearchTerms(request);

        if (string.IsNullOrWhiteSpace(searchTerms))
        {
            _logger.LogWarning("No search terms provided for record matching");
            return new RecordMatchResponse { Suggestions = [], TotalMatches = 0 };
        }

        var searchOptions = new SearchOptions
        {
            Size = request.MaxResults * 3, // Fetch more to allow for re-ranking
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Full,
            SearchMode = SearchMode.Any
        };

        // Apply record type filter
        if (!string.Equals(request.RecordTypeFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            searchOptions.Filter = $"recordType eq '{request.RecordTypeFilter}'";
        }

        // Select only needed fields
        searchOptions.Select.Add("id");
        searchOptions.Select.Add("recordType");
        searchOptions.Select.Add("recordName");
        searchOptions.Select.Add("recordDescription");
        searchOptions.Select.Add("organizations");
        searchOptions.Select.Add("people");
        searchOptions.Select.Add("referenceNumbers");
        searchOptions.Select.Add("keywords");
        searchOptions.Select.Add("dataverseRecordId");
        searchOptions.Select.Add("dataverseEntityName");

        try
        {
            var response = await _searchClient.SearchAsync<SearchIndexDocument>(searchTerms, searchOptions, cancellationToken);

            var candidates = new List<(SearchIndexDocument doc, double score, List<string> reasons)>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                if (result.Document == null) continue;

                var (score, reasons) = CalculateConfidenceScore(request, result.Document);

                if (score > 0)
                {
                    candidates.Add((result.Document, score, reasons));
                }
            }

            // Sort by score descending and take top results
            var suggestions = candidates
                .OrderByDescending(c => c.score)
                .Take(request.MaxResults)
                .Select(c => new RecordMatchSuggestion
                {
                    RecordId = c.doc.DataverseRecordId ?? c.doc.Id,
                    RecordType = c.doc.RecordType ?? "unknown",
                    RecordName = c.doc.RecordName ?? "Unknown Record",
                    ConfidenceScore = Math.Round(c.score, 2),
                    MatchReasons = c.reasons,
                    LookupFieldName = GetLookupFieldName(c.doc.RecordType)
                })
                .ToList();

            _logger.LogInformation(
                "Found {Count} record match suggestions from {Total} total matches",
                suggestions.Count,
                response.Value.TotalCount ?? 0);

            return new RecordMatchResponse
            {
                Suggestions = suggestions,
                TotalMatches = (int)(response.Value.TotalCount ?? 0)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for record matches");
            throw;
        }
    }

    private static string BuildSearchTerms(RecordMatchRequest request)
    {
        var terms = new List<string>();

        // Add organizations
        terms.AddRange(request.Organizations.Select(o => $"\"{o}\""));

        // Add people
        terms.AddRange(request.People.Select(p => $"\"{p}\""));

        // Add reference numbers (exact match preferred)
        terms.AddRange(request.ReferenceNumbers.Select(r => $"\"{r}\""));

        // Add keywords
        terms.AddRange(request.Keywords);

        return string.Join(" ", terms);
    }

    private (double score, List<string> reasons) CalculateConfidenceScore(
        RecordMatchRequest request,
        SearchIndexDocument document)
    {
        double totalScore = 0;
        var reasons = new List<string>();

        // Reference number matching (highest weight)
        var refScore = CalculateReferenceScore(request.ReferenceNumbers, document.ReferenceNumbers, reasons);
        totalScore += ReferenceMatchWeight * refScore;

        // Organization matching
        var orgScore = CalculateStringMatchScore(
            request.Organizations,
            document.Organizations,
            "Organization",
            reasons);
        totalScore += OrganizationMatchWeight * orgScore;

        // Person matching
        var personScore = CalculateStringMatchScore(
            request.People,
            document.People,
            "Person",
            reasons);
        totalScore += PersonMatchWeight * personScore;

        // Keyword matching
        var keywordScore = CalculateKeywordScore(
            request.Keywords,
            document.Keywords,
            reasons);
        totalScore += KeywordMatchWeight * keywordScore;

        // Normalize score to 0-1 range
        totalScore = Math.Min(1.0, totalScore);

        return (totalScore, reasons);
    }

    private static double CalculateReferenceScore(
        IEnumerable<string> requestRefs,
        IEnumerable<string>? documentRefs,
        List<string> reasons)
    {
        if (documentRefs == null || !documentRefs.Any())
            return 0;

        var docRefSet = documentRefs.Select(r => r.ToUpperInvariant()).ToHashSet();
        var matches = requestRefs
            .Where(r => docRefSet.Contains(r.ToUpperInvariant()))
            .ToList();

        if (matches.Count > 0)
        {
            foreach (var match in matches)
            {
                reasons.Add($"Reference: {match} (exact match)");
            }
            return 1.0; // Full score for any exact reference match
        }

        return 0;
    }

    private static double CalculateStringMatchScore(
        IEnumerable<string> requestValues,
        IEnumerable<string>? documentValues,
        string fieldLabel,
        List<string> reasons)
    {
        if (documentValues == null || !documentValues.Any())
            return 0;

        var docValues = documentValues.ToList();
        double totalScore = 0;
        int matchCount = 0;

        foreach (var reqValue in requestValues)
        {
            var bestMatch = FindBestMatch(reqValue, docValues);
            if (bestMatch.score > 0.7) // Threshold for considering a match
            {
                matchCount++;
                totalScore += bestMatch.score;
                reasons.Add($"{fieldLabel}: {bestMatch.value}");
            }
        }

        if (matchCount == 0)
            return 0;

        // Average score of matches, capped at 1.0
        return Math.Min(1.0, totalScore / Math.Max(1, requestValues.Count()));
    }

    private static (double score, string value) FindBestMatch(string target, IEnumerable<string> candidates)
    {
        double bestScore = 0;
        string bestValue = "";

        var targetLower = target.ToLowerInvariant();
        var targetWords = targetLower.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        foreach (var candidate in candidates)
        {
            var candidateLower = candidate.ToLowerInvariant();

            // Exact match
            if (candidateLower == targetLower)
            {
                return (1.0, candidate);
            }

            // Contains match
            if (candidateLower.Contains(targetLower) || targetLower.Contains(candidateLower))
            {
                var score = 0.9;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestValue = candidate;
                }
                continue;
            }

            // Word overlap (Jaccard similarity)
            var candidateWords = candidateLower.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var intersection = targetWords.Intersect(candidateWords).Count();
            var union = targetWords.Union(candidateWords).Count();

            if (union > 0)
            {
                var jaccardScore = (double)intersection / union;
                if (jaccardScore > bestScore)
                {
                    bestScore = jaccardScore;
                    bestValue = candidate;
                }
            }
        }

        return (bestScore, bestValue);
    }

    private static double CalculateKeywordScore(
        IEnumerable<string> requestKeywords,
        string? documentKeywords,
        List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(documentKeywords))
            return 0;

        var docKeywordSet = documentKeywords
            .ToLowerInvariant()
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .ToHashSet();

        var matches = requestKeywords
            .Where(k => docKeywordSet.Contains(k.ToLowerInvariant().Trim()))
            .ToList();

        if (matches.Count > 0)
        {
            reasons.Add($"Keywords: {string.Join(", ", matches)}");
            return Math.Min(1.0, (double)matches.Count / Math.Max(1, requestKeywords.Count()));
        }

        return 0;
    }

    private static string GetLookupFieldName(string? recordType)
    {
        if (string.IsNullOrWhiteSpace(recordType))
            return "sprk_matter"; // Default

        return LookupFieldMap.GetValueOrDefault(recordType, recordType);
    }
}
