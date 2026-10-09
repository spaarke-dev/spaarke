using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What the decision commit route (task 043) hands the Inquiry executor. The body arrives as text from the
/// wizard (ADR-013: the executor makes no AI call).</summary>
/// <param name="MatterId">The matter the inquiry is filed under (<c>sprk_regardingmatter</c>).</param>
/// <param name="To">Recipient addresses (at least one).</param>
/// <param name="Subject">Email subject; also the service request's name.</param>
/// <param name="Body">Email body, as the human edited and confirmed it in the wizard.</param>
/// <param name="Confirmed">True only when the wizard's Confirm step ran (D-52). Anything else makes the executor do nothing.</param>
public sealed record BudgetInquiryRequest(Guid MatterId, IReadOnlyList<string> To, string Subject, string Body, bool Confirmed);

/// <summary>How an Inquiry execution ended. The commit route records this in the Decision Record's step list.</summary>
public enum BudgetInquiryOutcome
{
    /// <summary>The service request exists and the email was sent.</summary>
    Sent,
    /// <summary>Nothing was written or sent: the wizard did not confirm.</summary>
    NotConfirmed,
    /// <summary>Nothing was written or sent: the request is malformed.</summary>
    Invalid,
    /// <summary>Nothing was written or sent: the caller lacks Create on the service request or AppendTo on the matter.</summary>
    Refused,
    /// <summary>Dataverse failed creating the service request; nothing was sent.</summary>
    CreateFailed,
    /// <summary>The service request WAS created but the email was not sent; <see cref="BudgetInquiryResult.ServiceRequestId"/> says which.</summary>
    SendFailed,
}

/// <summary>The executor's answer. <see cref="ServiceRequestId"/> is set whenever a row was written, even when the send failed.</summary>
public sealed record BudgetInquiryResult(
    BudgetInquiryOutcome Outcome,
    Guid? ServiceRequestId = null,
    Guid? CommunicationId = null,
    string? Error = null)
{
    public bool Succeeded => Outcome == BudgetInquiryOutcome.Sent;
}

/// <summary>
/// The inquiry email send: <see cref="CommunicationService.SendAsync"/> as the signed-in user (OBO), the same core
/// <c>POST /api/communications/send</c> uses. Concrete (ADR-010); <see cref="SendAsync"/> is virtual only as the test seam
/// for the Graph boundary, the <see cref="CommunicationService"/> precedent.
/// </summary>
public class InquiryEmailSender(CommunicationService communication, IHttpContextAccessor http)
{
    /// <summary>Sends the inquiry email, associated with the matter and the service request. Throws on failure.</summary>
    /// <returns>The <c>sprk_communication</c> id when the core tracked one.</returns>
    public virtual async Task<Guid?> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
    {
        var context = http.HttpContext
            ?? throw new InvalidOperationException("The inquiry email is sent as the signed-in user and needs an HTTP request.");

        var response = await communication.SendAsync(
            new SendCommunicationRequest
            {
                To = [.. request.To],
                Subject = request.Subject,
                Body = request.Body,
                SendMode = SendMode.User,
                Associations =
                [
                    new CommunicationAssociation { EntityType = "sprk_matter", EntityId = request.MatterId },
                    new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = serviceRequestId },
                ],
            },
            context,
            ct).ConfigureAwait(false);

        return response.CommunicationId;
    }
}

/// <summary>
/// The Inquiry (spec FR-36; task 070): creates a <c>sprk_servicerequest</c> with direction Outbound AS THE CALLER and
/// sends the inquiry email. Called by the decision commit route as its LAST external step; it writes no Decision Record
/// and closes no Signal (D-17), opens no chat session (D-52), sets no response-due date (D-56) and has no SLA (D-20).
/// </summary>
/// <remarks>
/// The create is the repo's G5 pattern (<see cref="OwnedChildWrite.CreateAsync"/>, unified-access-control-r2): Create on the
/// table and AppendTo on the matter (NFR-10) are asked AS THE CALLER, then the application writes the row owned by the team
/// <see cref="IRecordOwnershipResolver"/> names, so the row is never left owned by the caller in an ordinary business unit.
/// Every refusal precedes any write or send. A send failure after the row exists is reported with the row id, never hidden.
/// </remarks>
public sealed class BudgetInquiryExecutor(
    IDataverseUserClient user,
    IRecordOwnershipResolver ownership,
    IFieldMappingDataverseService appOnly,
    InquiryEmailSender email,
    IHttpContextAccessor http,
    ILogger<BudgetInquiryExecutor> logger)
{
    public const string ActionCode = "send-budget-inquiry";
    internal const string Table = "sprk_servicerequest";
    /// <summary>sprk_servicerequest.sprk_direction: Outbound (task 001).</summary>
    internal const int DirectionOutbound = 100000001;
    private const int NameMax = 850;

    private static readonly string[] Columns =
        ["sprk_name", "sprk_direction", "sprk_regardingmatter", "sprk_regardingrecordid", "sprk_regardingrecordtypelogicalname"];

    public async Task<BudgetInquiryResult> ExecuteAsync(BudgetInquiryRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            return new(BudgetInquiryOutcome.NotConfirmed, Error: "The inquiry was not confirmed; nothing was created or sent.");

        if (request.MatterId == Guid.Empty
            || request.To is not { Count: > 0 } || request.To.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return new(BudgetInquiryOutcome.Invalid, Error: "An inquiry needs a matter, at least one recipient, a subject and a message.");

        var item = new DataverseWriteItemMapper.MappedItem(BuildBody(request), Columns)
        {
            Lookups = [new("sprk_regardingmatter", "sprk_matter", "sprk_matters", request.MatterId)],
        };

        OwnedChildWrite.Outcome owned;
        try
        {
            owned = await OwnedChildWrite.CreateAsync(
                user, ownership, appOnly, Table, item, serverSetLookupColumns: null, CallerObjectId(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Budget inquiry service request not created | MatterId: {MatterId}", request.MatterId);
            return new(BudgetInquiryOutcome.CreateFailed, Error: "The service request could not be created.");
        }

        if (owned.CreatedId is not { } serviceRequestId)
        {
            logger.LogInformation("Budget inquiry refused before any send | MatterId: {MatterId}", request.MatterId);
            var reason = owned.Denied ?? owned.SecureFilingRefused ?? owned.OwnerRefusal?.Reason
                ?? owned.PlanRefusal?.Reason ?? owned.ClientFailure?.ErrorMessage;
            return new(BudgetInquiryOutcome.Refused, Error: reason ?? "The inquiry was not sent.");
        }

        // Email last: the row exists, so a failed send is reported WITH its id.
        try
        {
            var communicationId = await email.SendAsync(request, serviceRequestId, ct).ConfigureAwait(false);
            return new(BudgetInquiryOutcome.Sent, serviceRequestId, communicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Budget inquiry email not sent | ServiceRequestId: {ServiceRequestId}", serviceRequestId);
            return new(BudgetInquiryOutcome.SendFailed, serviceRequestId, Error: "The service request was created but the email was not sent.");
        }
    }

    /// <summary>The service request body. Deliberately has no <c>sprk_responseduedate</c> (D-56) and no disposition.</summary>
    internal static string BuildBody(BudgetInquiryRequest request)
    {
        var subject = request.Subject.Trim();
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = subject.Length > NameMax ? subject[..NameMax] : subject,
            ["sprk_direction"] = DirectionOutbound,
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({request.MatterId:D})",
            ["sprk_regardingrecordid"] = request.MatterId.ToString("D"),
            ["sprk_regardingrecordtypelogicalname"] = "sprk_matter",
        };
        return JsonSerializer.Serialize(fields);
    }

    private Guid? CallerObjectId() =>
        http.HttpContext is { } context
        && Guid.TryParse(CallerResolution.ResolveObjectId(context.User), out var oid) && oid != Guid.Empty
            ? oid
            : null;
}
