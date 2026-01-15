using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for sending emails via Microsoft Graph.
/// Uses TemplateEngine for variable substitution in email fields.
/// </summary>
/// <remarks>
/// <para>
/// Email configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "to": ["user@example.com", "{{node1.output.recipientEmail}}"],
///   "cc": [],
///   "subject": "Analysis Complete: {{node1.output.documentName}}",
///   "body": "{{node1.output.summary}}",
///   "isHtml": true,
///   "saveToSentItems": true
/// }
/// </code>
/// <para>
/// Uses On-Behalf-Of (OBO) authentication via IGraphClientFactory
/// to send email as the current user.
/// </para>
/// </remarks>
public sealed class SendEmailNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SendEmailNodeExecutor> _logger;

    public SendEmailNodeExecutor(
        ITemplateEngine templateEngine,
        IGraphClientFactory graphClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SendEmailNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _graphClientFactory = graphClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.SendEmail
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("SendEmail node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<EmailNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse email configuration");
            }
            else
            {
                if (config.To is null || config.To.Length == 0)
                {
                    errors.Add("At least one recipient (To) is required");
                }
                if (string.IsNullOrWhiteSpace(config.Subject))
                {
                    errors.Add("Email subject is required");
                }
                if (string.IsNullOrWhiteSpace(config.Body))
                {
                    errors.Add("Email body is required");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid email configuration JSON: {ex.Message}");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing SendEmail node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            // Validate first
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse configuration
            var config = JsonSerializer.Deserialize<EmailNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render template fields
            var subject = _templateEngine.Render(config.Subject!, templateContext);
            var body = _templateEngine.Render(config.Body!, templateContext);
            var toRecipients = config.To!
                .Select(to => _templateEngine.Render(to, templateContext))
                .Where(to => !string.IsNullOrWhiteSpace(to))
                .ToList();
            var ccRecipients = (config.Cc ?? Array.Empty<string>())
                .Select(cc => _templateEngine.Render(cc, templateContext))
                .Where(cc => !string.IsNullOrWhiteSpace(cc))
                .ToList();

            _logger.LogDebug(
                "Sending email with subject: {Subject} to {RecipientCount} recipients",
                subject,
                toRecipients.Count);

            // Build Graph message
            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = config.IsHtml ? BodyType.Html : BodyType.Text,
                    Content = body
                },
                ToRecipients = toRecipients.Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = email }
                }).ToList(),
                CcRecipients = ccRecipients.Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = email }
                }).ToList()
            };

            // Get HttpContext for OBO authentication
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "HttpContext not available for email sending",
                    NodeErrorCodes.InternalError,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Get Graph client with OBO authentication
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Send the email
            await graphClient.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = config.SaveToSentItems ?? true
            }, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SendEmail node {NodeId} completed - email sent to {RecipientCount} recipients",
                context.Node.Id,
                toRecipients.Count);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    sent = true,
                    subject = subject,
                    recipientCount = toRecipients.Count,
                    sentAt = DateTimeOffset.UtcNow
                },
                textContent: $"Email sent: {subject}",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (ServiceException ex)
        {
            _logger.LogError(
                ex,
                "SendEmail node {NodeId} failed with Graph error: {ErrorCode} - {ErrorMessage}",
                context.Node.Id,
                ex.ResponseStatusCode,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to send email: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SendEmail node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to send email: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

        return templateContext;
    }
}

/// <summary>
/// Configuration for SendEmail node from ConfigJson.
/// </summary>
internal sealed record EmailNodeConfig
{
    public string[]? To { get; init; }
    public string[]? Cc { get; init; }
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public bool IsHtml { get; init; } = true;
    public bool? SaveToSentItems { get; init; }
}
