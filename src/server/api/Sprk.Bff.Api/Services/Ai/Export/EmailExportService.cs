using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// Email export service using Microsoft Graph API.
/// Sends analysis results via email from the user's mailbox.
/// </summary>
public partial class EmailExportService : IExportService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<EmailExportService> _logger;
    private readonly AnalysisOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public EmailExportService(
        IGraphClientFactory graphClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EmailExportService> logger,
        IOptions<AnalysisOptions> options,
        IServiceProvider serviceProvider)
    {
        _graphClientFactory = graphClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Email;

    /// <inheritdoc />
    public async Task<ExportFileResult> ExportAsync(ExportContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending email for analysis {AnalysisId}", context.AnalysisId);

        // Validate configuration
        if (!_options.EnableEmailExport)
        {
            _logger.LogWarning("Email export is disabled");
            return ExportFileResult.Fail("Email export is not enabled");
        }

        // Get recipients from options
        var recipients = context.Options?.EmailTo;
        if (recipients == null || recipients.Length == 0)
        {
            return ExportFileResult.Fail("No email recipients specified");
        }

        try
        {
            // Get Graph client for user context
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                _logger.LogError("HttpContext is not available for email export");
                return ExportFileResult.Fail("Cannot send email: user context not available");
            }

            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Build email message
            var message = await BuildEmailMessageAsync(context, cancellationToken);

            // Send email
            await graphClient.Me.SendMail.PostAsync(new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            }, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Email sent successfully for analysis {AnalysisId} to {RecipientCount} recipients",
                context.AnalysisId, recipients.Length);

            return ExportFileResult.OkAction(new Dictionary<string, object?>
            {
                ["MessageId"] = message.Id,
                ["Recipients"] = recipients,
                ["Subject"] = message.Subject,
                ["SentAt"] = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email for analysis {AnalysisId}", context.AnalysisId);
            return ExportFileResult.Fail($"Failed to send email: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public ExportValidationResult Validate(ExportContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Content))
        {
            errors.Add("Analysis content is required for export");
        }

        if (string.IsNullOrWhiteSpace(context.Title))
        {
            errors.Add("Analysis title is required for export");
        }

        if (!_options.EnableEmailExport)
        {
            errors.Add("Email export is not enabled");
        }

        if (context.Options?.EmailTo == null || context.Options.EmailTo.Length == 0)
        {
            errors.Add("At least one email recipient is required");
        }
        else
        {
            // Validate email addresses
            foreach (var email in context.Options.EmailTo)
            {
                if (!IsValidEmail(email))
                {
                    errors.Add($"Invalid email address: {email}");
                }
            }
        }

        return errors.Count > 0
            ? ExportValidationResult.Invalid([.. errors])
            : ExportValidationResult.Valid();
    }

    private async Task<Message> BuildEmailMessageAsync(ExportContext context, CancellationToken cancellationToken)
    {
        var options = context.Options ?? new ExportOptions();

        // Build subject
        var subject = options.EmailSubject ?? $"Analysis: {context.Title}";

        // Build HTML body
        var htmlBody = BuildHtmlEmailBody(context);

        // Build recipients
        var toRecipients = options.EmailTo!
            .Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            })
            .ToList();

        var ccRecipients = options.EmailCc?
            .Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            })
            .ToList() ?? [];

        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = htmlBody
            },
            ToRecipients = toRecipients,
            CcRecipients = ccRecipients
        };

        // Add attachments if requested
        if (options.IncludeAnalysisFile)
        {
            var attachments = await GenerateAttachmentsAsync(context, options, cancellationToken);
            if (attachments.Count > 0)
            {
                message.Attachments = attachments;
            }
        }

        return message;
    }

    private string BuildHtmlEmailBody(ExportContext context)
    {
        var sb = new StringBuilder();

        // Email styling
        sb.AppendLine("""
            <html>
            <head>
                <style>
                    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color: #333; line-height: 1.6; }
                    .header { background-color: #0078D4; color: white; padding: 20px; border-radius: 4px 4px 0 0; }
                    .header h1 { margin: 0; font-size: 24px; }
                    .metadata { background-color: #f5f5f5; padding: 12px 20px; font-size: 14px; color: #666; }
                    .content { padding: 20px; background-color: #fff; }
                    .summary { background-color: #e6f2fb; padding: 15px; border-left: 4px solid #0078D4; margin: 15px 0; }
                    .footer { padding: 15px 20px; font-size: 12px; color: #888; border-top: 1px solid #eee; }
                    table { border-collapse: collapse; width: 100%; margin: 15px 0; }
                    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                    th { background-color: #0078D4; color: white; }
                </style>
            </head>
            <body>
            """);

        // Header
        sb.AppendLine($"""
            <div class="header">
                <h1>{HtmlEncode(context.Title)}</h1>
            </div>
            """);

        // Metadata
        sb.AppendLine($"""
            <div class="metadata">
                <strong>Generated:</strong> {DateTime.UtcNow:MMMM d, yyyy} &nbsp;|&nbsp;
                <strong>Source:</strong> {HtmlEncode(context.SourceDocumentName ?? "N/A")}
            </div>
            """);

        sb.AppendLine("<div class=\"content\">");

        // Summary section
        if (!string.IsNullOrWhiteSpace(context.Summary))
        {
            sb.AppendLine($"""
                <h3>Executive Summary</h3>
                <div class="summary">{HtmlEncode(context.Summary)}</div>
                """);
        }

        // Main content (strip HTML if present, preserve paragraphs)
        var cleanContent = StripHtmlTags(context.Content);
        var paragraphs = cleanContent.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        sb.AppendLine("<h3>Analysis Content</h3>");
        foreach (var paragraph in paragraphs)
        {
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                sb.AppendLine($"<p>{HtmlEncode(paragraph.Trim())}</p>");
            }
        }

        // Entities table
        if (context.Entities != null && HasEntities(context.Entities))
        {
            sb.AppendLine("<h3>Extracted Entities</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Type</th><th>Values</th></tr>");

            if (context.Entities.Organizations.Count > 0)
                sb.AppendLine($"<tr><td>Organizations</td><td>{HtmlEncode(string.Join(", ", context.Entities.Organizations))}</td></tr>");
            if (context.Entities.People.Count > 0)
                sb.AppendLine($"<tr><td>People</td><td>{HtmlEncode(string.Join(", ", context.Entities.People))}</td></tr>");
            if (context.Entities.Dates.Count > 0)
                sb.AppendLine($"<tr><td>Dates</td><td>{HtmlEncode(string.Join(", ", context.Entities.Dates))}</td></tr>");
            if (context.Entities.Amounts.Count > 0)
                sb.AppendLine($"<tr><td>Amounts</td><td>{HtmlEncode(string.Join(", ", context.Entities.Amounts))}</td></tr>");
            if (context.Entities.References.Count > 0)
                sb.AppendLine($"<tr><td>References</td><td>{HtmlEncode(string.Join(", ", context.Entities.References))}</td></tr>");

            sb.AppendLine("</table>");
        }

        // Clauses table
        if (context.Clauses?.Clauses.Count > 0)
        {
            sb.AppendLine("<h3>Contract Clauses</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Type</th><th>Description</th><th>Risk</th></tr>");

            foreach (var clause in context.Clauses.Clauses)
            {
                var riskColor = clause.RiskLevel?.ToUpperInvariant() switch
                {
                    "HIGH" or "CRITICAL" => "#D13438",
                    "MEDIUM" => "#FF8C00",
                    "LOW" => "#107C10",
                    _ => "#666"
                };

                sb.AppendLine($"""
                    <tr>
                        <td>{HtmlEncode(clause.Type)}</td>
                        <td>{HtmlEncode(clause.Description ?? "-")}</td>
                        <td style="color: {riskColor}; font-weight: bold;">{HtmlEncode(clause.RiskLevel ?? "-")}</td>
                    </tr>
                    """);
            }

            sb.AppendLine("</table>");
        }

        sb.AppendLine("</div>");

        // Footer
        sb.AppendLine("""
            <div class="footer">
                This analysis was generated by Spaarke AI.
                For questions or feedback, please contact your administrator.
            </div>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    private async Task<List<Attachment>> GenerateAttachmentsAsync(
        ExportContext context,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var attachments = new List<Attachment>();

        // Determine which format to attach
        var attachFormat = options.AttachmentFormat;

        // Resolve export services from DI to avoid circular dependency
        var exportServices = _serviceProvider.GetServices<IExportService>();
        var targetFormat = attachFormat switch
        {
            SaveDocumentFormat.Pdf => ExportFormat.Pdf,
            SaveDocumentFormat.Docx => ExportFormat.Docx,
            _ => (ExportFormat?)null
        };

        IExportService? attachService = targetFormat.HasValue
            ? exportServices.FirstOrDefault(s => s.Format == targetFormat.Value)
            : null;

        if (attachService == null)
        {
            _logger.LogWarning("Attachment format {Format} service not available", attachFormat);
            return attachments;
        }

        try
        {
            var result = await attachService.ExportAsync(context, cancellationToken);

            if (result.Success && result.FileBytes != null)
            {
                attachments.Add(new FileAttachment
                {
                    Name = result.FileName ?? $"analysis.{attachFormat.ToString().ToLowerInvariant()}",
                    ContentType = result.ContentType,
                    ContentBytes = result.FileBytes,
                    OdataType = "#microsoft.graph.fileAttachment"
                });

                _logger.LogDebug("Added {Format} attachment: {FileName}", attachFormat, result.FileName);
            }
            else
            {
                _logger.LogWarning("Failed to generate {Format} attachment: {Error}", attachFormat, result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating {Format} attachment", attachFormat);
        }

        return attachments;
    }

    private static bool HasEntities(AnalysisEntities entities)
    {
        return entities.Organizations.Count > 0 ||
               entities.People.Count > 0 ||
               entities.Dates.Count > 0 ||
               entities.Amounts.Count > 0 ||
               entities.References.Count > 0;
    }

    private static string HtmlEncode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }

    private static string StripHtmlTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = HtmlTagRegex().Replace(input, "");
        result = result.Replace("&nbsp;", " ")
                       .Replace("&amp;", "&")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&quot;", "\"");
        return result;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
