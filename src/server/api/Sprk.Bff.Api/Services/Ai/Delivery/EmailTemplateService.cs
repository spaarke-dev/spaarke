using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Delivery;

/// <summary>
/// Service for fetching and rendering Dataverse email templates.
/// Integrates with Power Apps email templates stored in the 'template' entity.
/// </summary>
/// <remarks>
/// <para>
/// Email templates in Dataverse contain subject and body with placeholder patterns.
/// This service fetches templates by ID or name and renders them using ITemplateEngine.
/// </para>
/// <para>
/// Supports standard Dataverse template placeholders as well as {{handlebars}} syntax
/// for AI output variables.
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

            return RenderTemplate(template, variables);
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

            return RenderTemplate(template, variables);
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

        var url = $"templates({templateId})?$select=title,subject,body,ispersonal,templatetypecode";
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
        var url = $"templates?$filter=title eq '{Uri.EscapeDataString(templateName)}'&$select=templateid,title,subject,body,ispersonal,templatetypecode&$top=1";
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

    private EmailTemplateResult RenderTemplate(DataverseEmailTemplate template, Dictionary<string, object?> variables)
    {
        _logger.LogDebug(
            "Rendering template '{TemplateTitle}' with {VariableCount} variables",
            template.Title, variables.Count);

        try
        {
            // Dataverse templates may use {!entity.field} syntax - convert to {{entity.field}}
            var normalizedSubject = NormalizePlaceholders(template.Subject ?? "");
            var normalizedBody = NormalizePlaceholders(template.Body ?? "");

            var renderedSubject = _templateEngine.Render(normalizedSubject, variables);
            var renderedBody = _templateEngine.Render(normalizedBody, variables);

            _logger.LogInformation(
                "Email template '{TemplateTitle}' rendered successfully",
                template.Title);

            return EmailTemplateResult.Ok(
                renderedSubject,
                renderedBody,
                true, // Dataverse email templates are typically HTML
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
    /// Converts Dataverse placeholder syntax to Handlebars syntax.
    /// Dataverse uses {!entity.field} while our TemplateEngine uses {{entity.field}}.
    /// </summary>
    private static string NormalizePlaceholders(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Convert {!variable} to {{variable}}
        return System.Text.RegularExpressions.Regex.Replace(
            input,
            @"\{!([^}]+)\}",
            "{{$1}}");
    }
}

/// <summary>
/// Interface for email template service.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Fetches an email template from Dataverse by ID and renders it with variables.
    /// </summary>
    Task<EmailTemplateResult> FetchAndRenderAsync(
        Guid templateId,
        Dictionary<string, object?> variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches an email template from Dataverse by name and renders it with variables.
    /// </summary>
    Task<EmailTemplateResult> FetchAndRenderByNameAsync(
        string templateName,
        Dictionary<string, object?> variables,
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

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("ispersonal")]
    public bool IsPersonal { get; init; }

    [JsonPropertyName("templatetypecode")]
    public int TemplateTypeCode { get; init; }
}

/// <summary>
/// OData collection response wrapper.
/// </summary>
internal sealed record ODataCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public List<T>? Value { get; init; }
}
