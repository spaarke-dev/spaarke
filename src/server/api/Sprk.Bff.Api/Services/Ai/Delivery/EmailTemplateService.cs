using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Delivery;

/// <summary>
/// Service for fetching and rendering Dataverse email templates.
/// Integrates with Power Apps email templates stored in the 'template' entity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rendering a Dataverse <c>template</c> row (ontology task 195).</b> The row's <c>subject</c> and <c>body</c>
/// columns are generated XSL stylesheets with CDATA sections (<c>&lt;?xml …&gt;&lt;xsl:stylesheet …</c>); they are NOT
/// renderable text and are never read. The row also carries the finished, editor-facing HTML: <c>subjectsafehtml</c>
/// and <c>safehtml</c>, in which every merge field is a span
/// <c>&lt;span class="dataslug" data-value="{!entity:attribute;fallback}"&gt;…&lt;/span&gt;</c>. This service renders from those two
/// columns only: each placeholder becomes the regarding record's value when the placeholder's entity is the regarding
/// record's type and the value is readable, else its fallback text, else nothing. The subject is returned as plain
/// text (tags stripped, entities decoded); the body is HTML with every substituted value HTML-encoded.
/// </para>
/// <para>
/// The merge values arrive in <c>variables</c>, already read AS THE CALLER by the route (row and field-level security
/// applied), so an attribute the caller cannot read is simply absent and renders its fallback. This class never
/// reads a regarding record itself.
/// </para>
/// <para>
/// <see cref="RenderFromContent"/> is the separate <c>{{handlebars}}</c> path (AI output variables); it is unchanged.
/// </para>
/// </remarks>
public sealed class EmailTemplateService : IEmailTemplateService
{
    private readonly ITemplateEngine _templateEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailTemplateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string TemplateSelect = "templateid,title,ispersonal,templatetypecode,subjectsafehtml,safehtml";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// One merge field in a template's safe HTML: either the editor's slug span (its <c>data-value</c> carries the
    /// logical-name token) or a bare <c>{!…}</c> token left in the text. ONE pass, so a substituted value that itself
    /// contains <c>{!…}</c> text is never re-expanded.
    /// </summary>
    private static readonly Regex MergeField = new(
        @"<span\b[^>]*?\bdata-value\s*=\s*(?:""(?<dv>[^""]*)""|'(?<dv>[^']*)')[^>]*>(?<inner>.*?)</span>"
        + @"|(?<bare>\{![^{}]*\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline,
        RegexTimeout);

    /// <summary>A token segment naming one attribute: <c>entity:attribute</c> or a lookup's <c>entity:attribute/@name</c>.</summary>
    private static readonly Regex AttributeSegment = new(
        @"^\s*(?<entity>[A-Za-z0-9_]+):(?<attr>[A-Za-z0-9_]+)(?<path>/@[A-Za-z]+)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex HtmlTag = new("<[^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    public EmailTemplateService(
        ITemplateEngine templateEngine,
        IHttpClientFactory httpClientFactory,
        ILogger<EmailTemplateService> logger)
    {
        _templateEngine = templateEngine;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EmailTemplateResult> FetchAndRenderAsync(
        Guid templateId,
        Dictionary<string, object?> variables,
        string? regardingEntityType,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching email template {TemplateId} from Dataverse", templateId);

        try
        {
            // Fetch template from Dataverse
            var template = await FetchTemplateAsync(templateId, dataverseUrl, accessToken, cancellationToken);
            if (template == null)
            {
                return EmailTemplateResult.Fail($"Email template not found: {templateId}");
            }

            return RenderTemplate(template, variables, regardingEntityType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch email template {TemplateId}", templateId);
            return EmailTemplateResult.Fail($"Failed to fetch template: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<EmailTemplateResult> FetchAndRenderByNameAsync(
        string templateName,
        Dictionary<string, object?> variables,
        string? regardingEntityType,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching email template by name: {TemplateName}", templateName);

        try
        {
            var template = await FetchTemplateByNameAsync(templateName, dataverseUrl, accessToken, cancellationToken);
            if (template == null)
            {
                return EmailTemplateResult.Fail($"Email template not found: {templateName}");
            }

            return RenderTemplate(template, variables, regardingEntityType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch email template by name: {TemplateName}", templateName);
            return EmailTemplateResult.Fail($"Failed to fetch template: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public EmailTemplateResult RenderFromContent(
        string subject,
        string body,
        Dictionary<string, object?> variables,
        bool isHtml = true)
    {
        _logger.LogDebug("Rendering email template with {VariableCount} variables", variables.Count);

        try
        {
            var renderedSubject = _templateEngine.Render(subject, variables);
            var renderedBody = _templateEngine.Render(body, variables);

            return EmailTemplateResult.Ok(
                renderedSubject,
                renderedBody,
                isHtml,
                new Dictionary<string, object?>
                {
                    ["variablesUsed"] = _templateEngine.GetVariableNames(subject).Concat(_templateEngine.GetVariableNames(body)).Distinct().ToList(),
                    ["renderedAt"] = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render email template");
            return EmailTemplateResult.Fail($"Template rendering failed: {ex.Message}");
        }
    }

    private async Task<DataverseEmailTemplate?> FetchTemplateAsync(
        Guid templateId,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Dataverse");
        client.BaseAddress = new Uri(dataverseUrl.TrimEnd('/') + "/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var url = $"templates({templateId})?$select={TemplateSelect}";
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Failed to fetch template {TemplateId}: {StatusCode}",
                templateId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<DataverseEmailTemplate>(JsonOptions, cancellationToken);
    }

    private async Task<DataverseEmailTemplate?> FetchTemplateByNameAsync(
        string templateName,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Dataverse");
        client.BaseAddress = new Uri(dataverseUrl.TrimEnd('/') + "/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Query by title (name)
        var url = $"templates?$filter=title eq '{Uri.EscapeDataString(templateName)}'&$select={TemplateSelect}&$top=1";
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Failed to query templates by name '{TemplateName}': {StatusCode}",
                templateName, response.StatusCode);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<DataverseEmailTemplate>>(JsonOptions, cancellationToken);
        return result?.Value?.FirstOrDefault();
    }

    private EmailTemplateResult RenderTemplate(
        DataverseEmailTemplate template,
        Dictionary<string, object?> variables,
        string? regardingEntityType)
    {
        _logger.LogDebug(
            "Rendering template '{TemplateTitle}' with {VariableCount} variables",
            template.Title, variables.Count);

        // The XSL `subject` / `body` columns are never rendered (see the class remarks). A row with no safe HTML cannot
        // be rendered to finished text: refuse it in the route's existing 400 shape rather than send XSL or debris.
        if (string.IsNullOrWhiteSpace(template.SafeHtml))
        {
            _logger.LogWarning(
                "Email template '{TemplateTitle}' ({TemplateId}) has no safehtml body; refusing the render",
                template.Title, template.TemplateId);
            return EmailTemplateResult.Fail("Template rendering failed: the template has no renderable body.");
        }

        try
        {
            var values = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase);

            var renderedSubject = RenderSubject(template.SubjectSafeHtml, values, regardingEntityType);
            var renderedBody = ResolveMergeFields(template.SafeHtml, values, regardingEntityType);

            _logger.LogInformation(
                "Email template '{TemplateTitle}' rendered successfully",
                template.Title);

            return EmailTemplateResult.Ok(
                renderedSubject,
                renderedBody,
                true, // the safehtml column is HTML
                new Dictionary<string, object?>
                {
                    ["templateId"] = template.TemplateId,
                    ["templateTitle"] = template.Title,
                    ["renderedAt"] = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render template '{TemplateTitle}'", template.Title);
            return EmailTemplateResult.Fail($"Template rendering failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The subject is plain text: merge fields resolved, any markup in <c>subjectsafehtml</c> stripped, entities
    /// decoded, whitespace collapsed to single spaces.
    /// </summary>
    private static string RenderSubject(
        string? subjectSafeHtml,
        Dictionary<string, object?> values,
        string? regardingEntityType)
    {
        if (string.IsNullOrWhiteSpace(subjectSafeHtml))
            return string.Empty;

        var resolved = ResolveMergeFields(subjectSafeHtml, values, regardingEntityType);
        var text = WebUtility.HtmlDecode(HtmlTag.Replace(resolved, string.Empty));
        return Whitespace.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Replaces every merge field in <paramref name="html"/> with its finished text, HTML-encoded (a record value such as
    /// <c>Smith &amp; Sons</c> or <c>&lt;b&gt;</c> must not become markup). A span that is not a Dataverse merge field is left
    /// as it is.
    /// </summary>
    private static string ResolveMergeFields(
        string html,
        Dictionary<string, object?> values,
        string? regardingEntityType)
    {
        return MergeField.Replace(html, match =>
        {
            var token = match.Groups["dv"].Success
                ? WebUtility.HtmlDecode(match.Groups["dv"].Value)
                : WebUtility.HtmlDecode(match.Groups["bare"].Value);

            var trimmed = token.Trim();
            if (!trimmed.StartsWith("{!", StringComparison.Ordinal) || !trimmed.EndsWith('}'))
                return match.Value; // a span with some other data-value

            return WebUtility.HtmlEncode(ResolveToken(trimmed[2..^1], values, regardingEntityType));
        });
    }

    /// <summary>
    /// Resolves one token body <c>entity:attribute[;entity:attribute…][;fallback]</c> to plain text. Leading segments
    /// that name an attribute are looked up when their entity is the regarding record's type, and the FIRST one with a
    /// non-empty value wins (Dataverse's own rule for a "data field values" slot: the first field that has a value,
    /// else the default); the remaining segments are the fallback, used when no value is readable. No value and no
    /// fallback is the empty string.
    /// </summary>
    private static string ResolveToken(string body, Dictionary<string, object?> values, string? regardingEntityType)
    {
        var segments = body.Split(';');
        var attributes = new List<Match>();
        var index = 0;
        while (index < segments.Length)
        {
            var m = AttributeSegment.Match(segments[index]);
            if (!m.Success)
                break;
            attributes.Add(m);
            index++;
        }

        string fallback;
        if (attributes.Count > 0)
            fallback = string.Join(";", segments.Skip(index)).Trim();
        else
            fallback = segments.Length > 1 ? segments[^1].Trim() : string.Empty; // a display-name token: nothing to look up

        foreach (var attribute in attributes)
        {
            if (regardingEntityType is null
                || !string.Equals(attribute.Groups["entity"].Value, regardingEntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue; // a placeholder for another entity (the sender, the owner...): nothing to read it from
            }

            var path = attribute.Groups["path"].Value;
            if (path.Length > 0 && !string.Equals(path, "/@name", StringComparison.OrdinalIgnoreCase))
                continue; // only a lookup's name is available

            if (values.TryGetValue(attribute.Groups["attr"].Value, out var raw)
                && FormatValue(raw) is { Length: > 0 } text)
            {
                return text;
            }
        }

        return fallback;
    }

    /// <summary>
    /// A merge value as email text. The route supplies the caller's formatted text (a string) for every attribute Dataverse
    /// annotated, which is used as is. This formatter is the fallback for an attribute with NO annotation, whose raw value
    /// keeps its Dataverse shape (a decimal with ten places, a <see cref="DateTime"/>) and must not reach a recipient as
    /// "25000.0000000000" or an ISO stamp.
    /// </summary>
    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string s => s.Trim(),
        bool b => b ? "Yes" : "No",
        DateTime dt => FormatDate(dt),
        DateTimeOffset dto => FormatDate(dto.UtcDateTime),
        decimal d => d.ToString(d == decimal.Truncate(d) ? "#,0" : "#,0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string FormatDate(DateTime value)
    {
        if (value.TimeOfDay == TimeSpan.Zero)
            return value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

        var text = value.ToString("MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
        return value.Kind == DateTimeKind.Utc ? text + " UTC" : text;
    }
}

/// <summary>
/// Interface for email template service.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Fetches an email template from Dataverse by ID and renders it to a finished plain-text subject and HTML body.
    /// <paramref name="variables"/> are the regarding record's attributes (logical name to value), read as the caller;
    /// <paramref name="regardingEntityType"/> is that record's entity logical name, which decides which of the template's
    /// placeholders they may fill (a placeholder for any other entity renders its fallback). Both are empty/null when the
    /// request names no regarding record.
    /// </summary>
    Task<EmailTemplateResult> FetchAndRenderAsync(
        Guid templateId,
        Dictionary<string, object?> variables,
        string? regardingEntityType,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches an email template from Dataverse by name and renders it as <see cref="FetchAndRenderAsync"/> does.
    /// </summary>
    Task<EmailTemplateResult> FetchAndRenderByNameAsync(
        string templateName,
        Dictionary<string, object?> variables,
        string? regardingEntityType,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders email content from subject and body strings with variables.
    /// Use this when template content is provided directly (not fetched from Dataverse).
    /// </summary>
    EmailTemplateResult RenderFromContent(
        string subject,
        string body,
        Dictionary<string, object?> variables,
        bool isHtml = true);
}

/// <summary>
/// Result of email template rendering.
/// </summary>
public record EmailTemplateResult
{
    /// <summary>Whether rendering succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Rendered email subject.</summary>
    public string? Subject { get; init; }

    /// <summary>Rendered email body.</summary>
    public string? Body { get; init; }

    /// <summary>Whether body is HTML format.</summary>
    public bool IsHtml { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Rendering metadata.</summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    public static EmailTemplateResult Ok(string subject, string body, bool isHtml, Dictionary<string, object?>? metadata = null) => new()
    {
        Success = true,
        Subject = subject,
        Body = body,
        IsHtml = isHtml,
        Metadata = metadata
    };

    public static EmailTemplateResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Dataverse email template entity record.
/// </summary>
internal sealed record DataverseEmailTemplate
{
    [JsonPropertyName("templateid")]
    public Guid TemplateId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>The subject as editor HTML (merge fields are <c>dataslug</c> spans). The XSL <c>subject</c> column is not read.</summary>
    [JsonPropertyName("subjectsafehtml")]
    public string? SubjectSafeHtml { get; init; }

    /// <summary>The body as editor HTML (merge fields are <c>dataslug</c> spans). The XSL <c>body</c> column is not read.</summary>
    [JsonPropertyName("safehtml")]
    public string? SafeHtml { get; init; }

    [JsonPropertyName("ispersonal")]
    public bool IsPersonal { get; init; }

    /// <summary>
    /// The entity the template is for, as the Web API returns it: a logical-name STRING (e.g. <c>"contact"</c>,
    /// <c>"systemuser"</c>), not a number. Typed <c>int</c> before 2026-10-06, which failed every template read
    /// with a JsonException (found by unified-access-control-r2's task 161 live gate).
    /// </summary>
    [JsonPropertyName("templatetypecode")]
    public string? TemplateTypeCode { get; init; }
}

/// <summary>
/// OData collection response wrapper.
/// </summary>
internal sealed record ODataCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public List<T>? Value { get; init; }
}
