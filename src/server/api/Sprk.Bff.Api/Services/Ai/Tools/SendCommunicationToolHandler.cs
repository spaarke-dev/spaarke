using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// AI tool handler that enables playbooks to send email communications.
/// Delegates to CommunicationService for Graph sendMail + Dataverse tracking.
/// Auto-discovered by AddToolFramework assembly scanning.
/// </summary>
public class SendCommunicationToolHandler : IAiToolHandler
{
    private readonly CommunicationService _communicationService;
    private readonly ILogger<SendCommunicationToolHandler> _logger;

    public const string ToolNameConst = "send_communication";
    public string ToolName => ToolNameConst;

    public SendCommunicationToolHandler(
        CommunicationService communicationService,
        ILogger<SendCommunicationToolHandler> logger)
    {
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends an email communication via CommunicationService.
    /// Expected parameters:
    /// - to: string (required, comma/semicolon-separated email addresses)
    /// - subject: string (required)
    /// - body: string (required, HTML content)
    /// - cc: string (optional, comma/semicolon-separated email addresses)
    /// - regardingEntity: string (optional, Dataverse entity logical name e.g. "sprk_matter")
    /// - regardingId: string/Guid (optional, Dataverse record ID)
    /// </summary>
    public async Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        try
        {
            // Extract required parameters
            var toRaw = parameters.GetString("to");
            var subject = parameters.GetString("subject");
            var body = parameters.GetString("body");

            if (string.IsNullOrWhiteSpace(toRaw))
                return PlaybookToolResult.CreateError("Parameter 'to' is required");
            if (string.IsNullOrWhiteSpace(subject))
                return PlaybookToolResult.CreateError("Parameter 'subject' is required");
            if (string.IsNullOrWhiteSpace(body))
                return PlaybookToolResult.CreateError("Parameter 'body' is required");

            // Parse recipients
            var to = ParseEmailList(toRaw);
            if (to.Length == 0)
                return PlaybookToolResult.CreateError("At least one valid email recipient is required in 'to'");

            // Extract optional CC recipients
            string[]? cc = null;
            if (parameters.TryGetValue<string>("cc", out var ccRaw) && !string.IsNullOrWhiteSpace(ccRaw))
            {
                cc = ParseEmailList(ccRaw);
            }

            // Build associations from regarding parameters
            CommunicationAssociation[]? associations = null;
            if (parameters.TryGetValue<string>("regardingEntity", out var regardingEntity) &&
                !string.IsNullOrWhiteSpace(regardingEntity) &&
                parameters.TryGetGuid("regardingId", out var regardingId) &&
                regardingId != Guid.Empty)
            {
                associations =
                [
                    new CommunicationAssociation
                    {
                        EntityType = regardingEntity,
                        EntityId = regardingId
                    }
                ];
            }

            _logger.LogInformation(
                "Playbook sending communication | To: {RecipientCount}, Subject: {Subject}",
                to.Length,
                subject);

            var request = new SendCommunicationRequest
            {
                To = to,
                Cc = cc,
                Subject = subject,
                Body = body,
                BodyFormat = BodyFormat.HTML,
                CommunicationType = CommunicationType.Email,
                Associations = associations
            };

            var response = await _communicationService.SendAsync(request, httpContext: null, ct);

            return PlaybookToolResult.CreateSuccess(new
            {
                CommunicationId = response.CommunicationId,
                Status = response.Status.ToString(),
                SentAt = response.SentAt,
                From = response.From,
                CorrelationId = response.CorrelationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendCommunication tool handler failed");
            return PlaybookToolResult.CreateError($"Send communication failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Splits a raw email string by comma or semicolon delimiters,
    /// trims whitespace, and removes empty entries.
    /// </summary>
    private static string[] ParseEmailList(string raw)
        => raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
              .Select(e => e.Trim())
              .Where(e => !string.IsNullOrWhiteSpace(e))
              .ToArray();
}
