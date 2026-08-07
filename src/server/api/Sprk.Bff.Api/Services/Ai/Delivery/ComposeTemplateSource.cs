using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Delivery;

/// <summary>
/// Task 031 (spaarkeai-compose-r6, FR-05) — <see cref="IComposeTemplateSource"/> implementation: REUSE of
/// the two existing template capabilities, composed and nothing more (§11 no parallel subsystem):
/// <list type="bullet">
///   <item><b>Storage</b> — the native Dataverse <c>template</c> entity, fetched with the SAME
///   IHttpClientFactory("Dataverse") + caller-token Web-API pattern as the sibling
///   <see cref="EmailTemplateService"/>; the <c>.dotx</c> binary is the record's annotation attachment
///   (the "templates stored in Dataverse as attachments" pattern the design cites).</item>
///   <item><b>Variables</b> — the existing <see cref="IWordTemplateService.GenerateFromTemplateAsync"/>
///   (<c>ITemplateEngine</c>/Handlebars text-node render incl. headers/footers). Skipped entirely when no
///   variables are supplied, keeping variable-free house templates byte-faithful.</item>
/// </list>
/// Native <c>documenttemplate</c> is NOT used (FR-05 LOCKED). This class does NO OOXML part-merge — that is
/// <c>ComposeTemplatePartMergeEngine</c> (task 030).
/// </summary>
public sealed class ComposeTemplateSource : IComposeTemplateSource
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] TemplateFileExtensions = { ".dotx", ".docx" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWordTemplateService _wordTemplateService;
    private readonly ILogger<ComposeTemplateSource> _logger;

    public ComposeTemplateSource(
        IHttpClientFactory httpClientFactory,
        IWordTemplateService wordTemplateService,
        ILogger<ComposeTemplateSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _wordTemplateService = wordTemplateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ComposeResolvedTemplate?> ResolveAsync(
        string templateIdOrName,
        Dictionary<string, object?>? variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdOrName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var client = CreateClient(dataverseUrl, accessToken);

        var record = await FetchTemplateRecordAsync(client, templateIdOrName, cancellationToken);
        if (record is null)
        {
            _logger.LogWarning("Compose template not found in the template entity: {Template}", templateIdOrName);
            return null;
        }

        var attachment = await FetchTemplateAttachmentAsync(client, record.TemplateId, cancellationToken);
        if (attachment is null)
        {
            _logger.LogWarning(
                "Compose template {Template} ({TemplateId}) has no .dotx/.docx annotation attachment",
                record.Title, record.TemplateId);
            return null;
        }

        var bytes = Convert.FromBase64String(attachment.DocumentBody);
        var rendered = false;

        if (variables is { Count: > 0 })
        {
            // REUSE: the existing Word text-node {{variable}} render (headers/footers included).
            var result = await _wordTemplateService.GenerateFromTemplateAsync(
                bytes, variables, attachment.FileName, cancellationToken);
            if (result.Success && result.FileBytes is not null)
            {
                bytes = result.FileBytes;
                rendered = true;
            }
            else
            {
                // Loud, not silent: the merge proceeds on the UNRENDERED template rather than failing the
                // save path outright — the caller surfaces the warning (operator principle).
                _logger.LogWarning(
                    "Compose template {Template}: variable rendering failed ({Error}); merging the unrendered template",
                    record.Title, result.Error);
            }
        }

        return new ComposeResolvedTemplate(bytes, record.Title ?? templateIdOrName, attachment.FileName, rendered);
    }

    private HttpClient CreateClient(string dataverseUrl, string accessToken)
    {
        var client = _httpClientFactory.CreateClient("Dataverse");
        client.BaseAddress = new Uri(dataverseUrl.TrimEnd('/') + "/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<TemplateRecord?> FetchTemplateRecordAsync(
        HttpClient client, string templateIdOrName, CancellationToken ct)
    {
        if (Guid.TryParse(templateIdOrName, out var id))
        {
            var response = await client.GetAsync($"templates({id})?$select=templateid,title", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TemplateRecord>(JsonOptions, ct);
        }

        // Exact-title lookup (mirrors EmailTemplateService's fetch-by-name convention).
        var escaped = templateIdOrName.Replace("'", "''");
        var listResponse = await client.GetAsync(
            $"templates?$select=templateid,title&$filter=title eq '{escaped}'&$top=1", ct);
        if (!listResponse.IsSuccessStatusCode) return null;
        var list = await listResponse.Content.ReadFromJsonAsync<TemplateRecordList>(JsonOptions, ct);
        return list?.Value?.FirstOrDefault();
    }

    private async Task<TemplateAttachment?> FetchTemplateAttachmentAsync(
        HttpClient client, Guid templateId, CancellationToken ct)
    {
        // The .dotx lives as an annotation (note attachment) on the template record — the established
        // Dataverse pattern for binary template storage. Newest matching attachment wins.
        var response = await client.GetAsync(
            $"annotations?$select=filename,documentbody,modifiedon" +
            $"&$filter=_objectid_value eq {templateId} and isdocument eq true" +
            $"&$orderby=modifiedon desc", ct);
        if (!response.IsSuccessStatusCode) return null;

        var list = await response.Content.ReadFromJsonAsync<TemplateAttachmentList>(JsonOptions, ct);
        return list?.Value?.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.FileName)
            && !string.IsNullOrEmpty(a.DocumentBody)
            && TemplateFileExtensions.Any(ext =>
                a.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record TemplateRecord
    {
        [JsonPropertyName("templateid")] public Guid TemplateId { get; init; }
        [JsonPropertyName("title")] public string? Title { get; init; }
    }

    private sealed record TemplateRecordList
    {
        [JsonPropertyName("value")] public List<TemplateRecord>? Value { get; init; }
    }

    private sealed record TemplateAttachment
    {
        [JsonPropertyName("filename")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("documentbody")] public string DocumentBody { get; init; } = string.Empty;
    }

    private sealed record TemplateAttachmentList
    {
        [JsonPropertyName("value")] public List<TemplateAttachment>? Value { get; init; }
    }
}
