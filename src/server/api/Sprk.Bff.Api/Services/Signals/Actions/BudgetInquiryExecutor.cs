using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What the decision commit route (task 043) hands the Inquiry executor. The body arrives as text from the
/// wizard (ADR-013: the executor makes no AI call).</summary>
/// <param name="MatterId">The matter the inquiry is filed under (<c>sprk_regardingmatter</c>).</param>
/// <param name="To">Recipient addresses (at least one).</param>
/// <param name="Subject">Email subject; also the service request's name.</param>
/// <param name="Body">Email body, as the human edited and confirmed it in the wizard.</param>
/// <param name="Confirmed">True only when the wizard's Confirm step ran (D-52). Anything else makes the executor do nothing.</param>
/// <param name="OutsideFirmId">The outside firm (<c>sprk_organization</c>) the inquiry is to. Only the caller knows which of the matter's two firm slots it is; it is written to <c>sprk_regardingorganization</c> so the disposition is queryable per outside firm.</param>
public sealed record BudgetInquiryRequest(
    Guid MatterId, IReadOnlyList<string> To, string Subject, string Body, bool Confirmed, Guid? OutsideFirmId = null);

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
/// <remarks>Task 160 (D-181): <paramref name="facts"/> adds, at send time, the record link chosen by recipient and the record's name for the
/// footer. Optional so the test seams that build a sender without it keep working; DI always supplies it.</remarks>
public class InquiryEmailSender(CommunicationService communication, IHttpContextAccessor http, InquiryRecordFacts? facts = null)
{
    /// <summary>Sends the inquiry email, associated with the matter and the service request. Throws on failure.</summary>
    /// <returns>The <c>sprk_communication</c> id when the core tracked one.</returns>
    public virtual async Task<Guid?> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
    {
        var context = http.HttpContext
            ?? throw new InvalidOperationException("The inquiry email is sent as the signed-in user and needs an HTTP request.");

        var send = await PrepareAsync(request, serviceRequestId, ct).ConfigureAwait(false);
        var response = await communication.SendAsync(send, context, ct).ConfigureAwait(false);
        return response.CommunicationId;
    }

    /// <summary>
    /// The send request as <see cref="SendAsync"/> hands it to the communication core: the record facts (label and link lines, read
    /// at send time) folded into <see cref="Build"/>. Its own method so the test reaches the real wiring without a Graph send.
    /// </summary>
    internal async Task<SendCommunicationRequest> PrepareAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
    {
        // The commit route sets MatterId only for a matter core, so the inquiry's core record is always that matter.
        var extras = facts is null
            ? null
            : await facts.ResolveAsync(BudgetInquiryExecutor.MatterTable, request.MatterId, request.To ?? [], ct).ConfigureAwait(false);
        return Build(request, serviceRequestId, extras);
    }

    /// <summary>
    /// The send request. The service request is the FIRST (primary) association because
    /// <c>CommunicationService</c> maps only <c>associations[0]</c> to <c>sprk_regarding*</c>: that sets
    /// <c>sprk_communication.sprk_regardingservicerequest</c>, so a reply links to the inquiry through the association
    /// ladder and the footer names the inquiry. Chosen over a follow-up PATCH of
    /// <c>sprk_servicerequest.sprk_regardingcommunication</c> because that is a second write that could fail AFTER the
    /// email went out. The matter stays reachable through <c>sprk_servicerequest.sprk_regardingmatter</c> and is still the
    /// second association (counted, not mapped). Plain text: the wizard's body is plain text.
    /// <para>Task 160 (D-181; owner 2026-10-23): the service request's <c>EntityName</c> names the RECORD, "{core name} ({core number})"
    /// with no "Budget inquiry:" prefix (the subject already says so), because it is both the footer's record reference and
    /// <c>sprk_communication.sprk_regardingrecordname</c>. When the record's name could not be read it is the inquiry's own name (its
    /// subject), never a logical name and GUID. The link lines follow the body.</para>
    /// </summary>
    internal static SendCommunicationRequest Build(BudgetInquiryRequest request, Guid serviceRequestId, InquiryEmailExtras? extras = null)
    {
        var to = request.To?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToArray() ?? [];
        if (to.Length == 0)
            throw new ArgumentException("An inquiry email needs at least one recipient.", nameof(request));

        var lines = extras?.LinkLines ?? [];
        return new SendCommunicationRequest
        {
            To = to,
            Subject = request.Subject,
            Body = lines.Count > 0 ? $"{request.Body}\n\n{string.Join("\n", lines)}" : request.Body,
            BodyFormat = BodyFormat.PlainText,
            SendMode = SendMode.User,
            Associations =
            [
                new CommunicationAssociation
                {
                    EntityType = "sprk_servicerequest",
                    EntityId = serviceRequestId,
                    EntityName = extras?.RecordLabel ?? InquiryRecordFacts.RecordLabel(request.Subject, null),
                },
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = request.MatterId },
            ],
        };
    }
}

/// <summary>
/// The Inquiry (spec FR-36; task 070): creates a <c>sprk_servicerequest</c> with direction Outbound AS THE CALLER and
/// sends the inquiry email. Called by the decision commit route as its LAST external step; it writes no Decision Record
/// and closes no Signal (D-17), opens no chat session (D-52), sets no response-due date (D-56) and has no SLA (D-20).
/// </summary>
/// <remarks>
/// The create is the ONE server create (<see cref="RecordCreationService"/>, as the caller; field-mapping-server-write-path-r1
/// task 017): Create on the table and AppendTo on the matter (NFR-10) are asked AS THE CALLER, then the application writes the
/// row owned by the team <see cref="IRecordOwnershipResolver"/> names, so the row is never left owned by the caller in an
/// ordinary business unit; it carries its I-1 stamp (<c>sprk_regardingmatter</c>) at create.
/// Every refusal precedes any write or send. A send failure after the row exists is reported with the row id, never hidden.
/// </remarks>
public sealed class BudgetInquiryExecutor(
    IDataverseUserClient user,
    IRecordOwnershipResolver ownership,
    InquiryEmailSender email,
    IHttpContextAccessor http,
    ILogger<BudgetInquiryExecutor> logger)
{
    public const string ActionCode = "send-budget-inquiry";
    internal const string Table = "sprk_servicerequest";
    /// <summary>sprk_servicerequest.sprk_direction: Outbound (task 001).</summary>
    internal const int DirectionOutbound = 100000001;
    private const int NameMax = 850;

    private static readonly string[] BaseColumns =
        ["sprk_name", "sprk_direction", "sprk_regardingmatter", "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardingrecordtypelogicalname"];

    private const string RecordTypeSet = "sprk_recordtype_refs";
    internal const string MatterTable = "sprk_matter";

    /// <summary>
    /// Everything that can refuse the inquiry, WITHOUT writing or sending: confirmation, shape, the caller's Create on the
    /// table and AppendTo on the matter (and firm), and the secure-matter decision. Returns the refusal
    /// (<see cref="BudgetInquiryOutcome.NotConfirmed"/>, <see cref="BudgetInquiryOutcome.Invalid"/> or
    /// <see cref="BudgetInquiryOutcome.Refused"/>, the same outcomes <see cref="ExecuteAsync"/> gives), or <c>null</c> when
    /// the inquiry would proceed. The commit route calls it before its first write so a refusal leaves nothing behind.
    /// </summary>
    /// <remarks>The secure-matter test mirrors <see cref="OwnedChildWrite.CreateAsync"/> for this table (a non-child table
    /// cannot be created under a secure record); if that core changes, change it here (pinned by a parity test).</remarks>
    public async Task<BudgetInquiryResult?> PreflightAsync(BudgetInquiryRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
            return invalid;

        var typed = await ResolveMatterTypeAsync(ct).ConfigureAwait(false);
        if (typed.Refusal is not null)
            return typed.Refusal;

        var item = BuildItem(request, typed.TypeId);
        var me = await CallerWriteAuthorization.WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is not null)
            return new(BudgetInquiryOutcome.Refused, Error: me.Failure.ErrorMessage ?? "The calling user could not be identified.");

        var check = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me.SystemUserId, Table, item, null, ct).ConfigureAwait(false);
        if (!check.Succeeded)
            return new(BudgetInquiryOutcome.Refused,
                Error: check.Denied ?? check.ClientFailure?.ErrorMessage ?? "You do not have permission to send this inquiry.");

        var owner = await ownership.ResolveOwnerAsync(
            OwnedChildWrite.OwnershipOf(Table, item, CallerObjectId(), me.SystemUserId) with // 194b R (B3): the one builder
            {
                RequestedBy = RecordRequester.Of(me.SystemUserId),
            },
            ct).ConfigureAwait(false);

        if (!owner.IsOwned)
            return new(BudgetInquiryOutcome.Refused, Error: owner.Reason ?? "no owner was resolved for the inquiry");
        if (owner.IsSecureOwner)
            return new(BudgetInquiryOutcome.Refused,
                Error: "A service request cannot be created under a secure matter here. It was NOT created.");

        return null;
    }

    private static BudgetInquiryResult? Validate(BudgetInquiryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            return new(BudgetInquiryOutcome.NotConfirmed, Error: "The inquiry was not confirmed; nothing was created or sent.");

        if (request.MatterId == Guid.Empty || request.OutsideFirmId == Guid.Empty
            || request.To is not { Count: > 0 } || request.To.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return new(BudgetInquiryOutcome.Invalid, Error: "An inquiry needs a matter, at least one recipient, a subject and a message.");

        return null;
    }

    /// <summary>
    /// The <c>sprk_recordtype_ref</c> catalog row for <c>sprk_matter</c>, read AS THE CALLER (the same read the pair rule makes
    /// of the type row). A <c>sprk_servicerequest</c> is a core table with no pair agreement column, so the ADR-024 pair
    /// (<c>sprk_regardingrecordid</c>) is accepted only TYPED (<see cref="CallerWriteAuthorization.PairTargetAsync"/>); an untyped pair
    /// is refused 400 Invalid, however the lookup beside it agrees.
    /// </summary>
    private async Task<(Guid TypeId, BudgetInquiryResult? Refusal)> ResolveMatterTypeAsync(CancellationToken ct)
    {
        var rows = await user.GetAsync(
            $"{RecordTypeSet}?$select=sprk_recordtype_refid&$filter=sprk_recordlogicalname eq '{MatterTable}' and statecode eq 0&$top=2", ct)
            .ConfigureAwait(false);
        if (!rows.IsSuccess)
            return (Guid.Empty, new(BudgetInquiryOutcome.Refused, Error: rows.ErrorMessage ?? "The matter's record type could not be read."));

        if (rows.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() == 1
            && value[0].TryGetProperty("sprk_recordtype_refid", out var id) && id.TryGetGuid(out var typeId) && typeId != Guid.Empty)
            return (typeId, null);

        return (Guid.Empty, new(BudgetInquiryOutcome.Refused, Error: "The matter's record type is not cataloged, so the inquiry cannot be filed. It was NOT created."));
    }

    private static DataverseWriteItemMapper.MappedItem BuildItem(BudgetInquiryRequest request, Guid matterTypeId)
    {
        var lookups = new List<DataverseWriteItemMapper.MappedLookup>
        {
            new("sprk_regardingmatter", MatterTable, "sprk_matters", request.MatterId),
            new("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeSet, matterTypeId),
        };
        var columns = BaseColumns.ToList();
        if (request.OutsideFirmId is { } firm)
        {
            lookups.Add(new("sprk_regardingorganization", "sprk_organization", "sprk_organizations", firm));
            columns.Add("sprk_regardingorganization");
        }

        return new DataverseWriteItemMapper.MappedItem(BuildBody(request, matterTypeId), columns) { Lookups = lookups };
    }

    public async Task<BudgetInquiryResult> ExecuteAsync(BudgetInquiryRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
            return invalid;

        var typed = await ResolveMatterTypeAsync(ct).ConfigureAwait(false);
        if (typed.Refusal is not null)
            return typed.Refusal;

        var item = BuildItem(request, typed.TypeId);

        // Field-mapping-server-write-path-r1 task 017 (2d-3): the ONE server create, as the caller (ADR-028: WhoAmI, every
        // check, the owner, the I-1 stamp and the post-create steps are the service's). Resolved from the request's services,
        // as DecisionRouteCores.CreateChildAsync does: the service is internal and this executor's constructor is public.
        // No request (or no service) creates nothing (NFR-01, no fallback); the email below needs the request too.
        RecordCreationOutcome created;
        try
        {
            var context = http.HttpContext
                ?? throw new InvalidOperationException("The service request is created as the signed-in user and needs an HTTP request.");
            created = await context.RequestServices.GetRequiredService<RecordCreationService>().CreateAsync(
                    new RecordCreationRequest(Table, new RecordCreationPayload.Mapped(item))
                    {
                        CorrelationId = context.TraceIdentifier,
                    },
                    RecordCreationPrincipal.ForCaller(user, CallerObjectId()),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Budget inquiry service request not created | MatterId: {MatterId}", request.MatterId);
            return new(BudgetInquiryOutcome.CreateFailed, Error: "The service request could not be created.");
        }

        if (created.Refusal is { } refusal)
        {
            logger.LogInformation(
                "Budget inquiry refused before any send | MatterId: {MatterId} {Kind} {Code} correlationId={CorrelationId}",
                request.MatterId, refusal.Kind, refusal.Code, created.CorrelationId);
            return RefusalResult(refusal, created.CorrelationId);
        }

        if (created.CreatedId is not { } serviceRequestId)
        {
            // Neither a row nor a refusal: nothing is sent (fail closed).
            logger.LogInformation("Budget inquiry refused before any send | MatterId: {MatterId}", request.MatterId);
            return new(BudgetInquiryOutcome.Refused, Error: "The inquiry was not sent.");
        }

        // Email last: the row exists, so the outcome from here on ALWAYS carries its id. The send is deliberately not
        // cancellable: a cancelled caller must not leave an email that may or may not have gone out and no id to list.
        try
        {
            var communicationId = await email.SendAsync(request, serviceRequestId, CancellationToken.None).ConfigureAwait(false);
            return new(BudgetInquiryOutcome.Sent, serviceRequestId, communicationId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Budget inquiry email not sent | ServiceRequestId: {ServiceRequestId}", serviceRequestId);
            return new(BudgetInquiryOutcome.SendFailed, serviceRequestId, Error: "The service request was created but the email was not sent.");
        }
    }

    /// <summary>
    /// Task 017 (2d-3; ADR-019, NFR-07): a <see cref="RecordCreationService"/> refusal in this executor's EXISTING outcome
    /// contract, by kind (the Office precedent, <c>OfficeService.MapQuickCreateRefusal</c>). A check or owner decision that
    /// faulted (<see cref="RecordCreationRefusalKind.Unavailable"/>, the I-1 stamp's <c>stamp_unavailable</c> included) is
    /// <see cref="BudgetInquiryOutcome.CreateFailed"/>, as the old core's throw was; every other refusal is
    /// <see cref="BudgetInquiryOutcome.Refused"/>, as the old core's refusals were. The text is the shared map's
    /// (<see cref="RecordCreationProblemMap"/>): the uniform not-found, and fixed words for a fault — never Dataverse's.
    /// </summary>
    internal static BudgetInquiryResult RefusalResult(RecordCreationRefusal refusal, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var detail = RecordCreationProblemMap.ToProblemDetails(
            refusal, Table, string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId).Detail;
        return new(
            refusal.Kind == RecordCreationRefusalKind.Unavailable ? BudgetInquiryOutcome.CreateFailed : BudgetInquiryOutcome.Refused,
            Error: detail);
    }

    /// <summary>The service request body. Deliberately has no <c>sprk_responseduedate</c> (D-56) and no disposition.</summary>
    internal static string BuildBody(BudgetInquiryRequest request, Guid matterTypeId)
    {
        var subject = request.Subject.Trim();
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = subject.Length > NameMax ? subject[..NameMax] : subject,
            ["sprk_direction"] = DirectionOutbound,
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({request.MatterId:D})",
            ["sprk_regardingrecordid"] = request.MatterId.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/{RecordTypeSet}({matterTypeId:D})",
            ["sprk_regardingrecordtypelogicalname"] = "sprk_matter",
        };
        if (request.OutsideFirmId is { } firm)
            fields["sprk_RegardingOrganization@odata.bind"] = $"/sprk_organizations({firm:D})";
        return JsonSerializer.Serialize(fields);
    }

    private Guid? CallerObjectId() =>
        http.HttpContext is { } context
        && Guid.TryParse(CallerResolution.ResolveObjectId(context.User), out var oid) && oid != Guid.Empty
            ? oid
            : null;
}
