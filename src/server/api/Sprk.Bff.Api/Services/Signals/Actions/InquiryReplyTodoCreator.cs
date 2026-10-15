using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What <see cref="InquiryReplyTodoCreator.OnReplyArrivedAsync"/> did.</summary>
public enum InquiryReplyTodoOutcome
{
    /// <summary>The "Record the outcome" To Do was created.</summary>
    Created,
    /// <summary>The Inquiry already has its To Do (an earlier reply raised it); nothing was written.</summary>
    AlreadyExists,
    /// <summary>The communication is not an inbound reply to an open outbound service request; nothing was written.</summary>
    NotApplicable,
    /// <summary>The To Do could not be created (no owner resolved, a Dataverse fault); logged, never thrown.</summary>
    Failed,
}

/// <summary>
/// Task 071 (decision D-111): when an INCOMING reply is associated with an OPEN OUTBOUND service request, raises ONE
/// "Record the outcome of the budget inquiry" To Do for the matter's Assigned To Internal contact, due today. The person
/// who reads the reply then records the disposition (<see cref="InquiryDispositionService.RecordDispositionAsync"/>), which
/// completes the To Do. Nothing here chooses the disposition (ADR-013).
/// </summary>
/// <remarks>
/// <para><b>Idempotent per service request, atomically.</b> The To Do's id is derived from the service request
/// (<see cref="InquiryDispositionService.TodoIdFor"/>), so a second reply, or two replies racing, cannot create a second
/// one: the second create is a duplicate key and is treated as "already exists". The same id lets recording the disposition
/// complete it without a search.</para>
/// <para><b>The create is the pipeline's, so it follows the pipeline's rule for a server-created To Do</b>
/// (<c>TaskActionCore</c> / <c>OfficeService.CreateTodoAsync</c>, uac-r2 invariants): there is no caller to check, the
/// owner is decided by <see cref="IRecordOwnershipResolver"/> (the named Secure team under a secure matter, else the
/// matter's business-unit team: never an individual, I-6), the core-ancestor stamp is derived by the server, and the PERSON
/// the To Do is for is <c>sprk_assignedto</c>: the matter's Assigned To Internal contact, else its first attorney
/// (<see cref="AssignedToDefaults"/>), else blank with a <c>inquiry_todo_unassigned</c> warning, in which case the
/// matter's team still owns it.</para>
/// <para><b>Regarding.</b> The To Do is filed against the MATTER (ADR-024 allows one specific regarding lookup, and the
/// matter is the root the ownership and access rules work from). The Inquiry it is about rides in the first line of
/// <c>sprk_notes</c> (<see cref="NotesKey"/>) because the typed <c>sprk_regardingservicerequest</c> lookup cannot also be set.</para>
/// <para><b>Best effort for the pipeline</b>: never throws (a failure must not fail email capture); the outcome and a log
/// line say what happened.</para>
/// </remarks>
public sealed class InquiryReplyTodoCreator(
    IGenericEntityService entities,
    CoreAncestorResolver coreAncestors,
    IRecordOwnershipResolver ownership,
    ICommunicationDataverseService recordTypes,
    TimeProvider clock,
    ILogger<InquiryReplyTodoCreator> logger)
{
    internal const string TodoName = "Record the outcome of the budget inquiry";

    /// <summary>The first line of the To Do's notes: <c>serviceRequestId=</c> and the Inquiry's id. The UI reads the Inquiry from it.</summary>
    public const string NotesKey = "serviceRequestId=";
    private const string Todo = "sprk_todo";

    public async Task<InquiryReplyTodoOutcome> OnReplyArrivedAsync(Guid communicationId, CancellationToken ct)
    {
        try
        {
            return await CreateIfNeededAsync(communicationId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Inquiry outcome To Do not created | CommunicationId: {CommunicationId}", communicationId);
            return InquiryReplyTodoOutcome.Failed;
        }
    }

    private async Task<InquiryReplyTodoOutcome> CreateIfNeededAsync(Guid communicationId, CancellationToken ct)
    {
        var reply = await entities.RetrieveAsync(
            "sprk_communication", communicationId, ["sprk_direction", "sprk_regardingservicerequest"], ct).ConfigureAwait(false);
        if (reply.GetAttributeValue<OptionSetValue>("sprk_direction")?.Value != InquiryDispositionService.CommunicationIncoming
            || reply.GetAttributeValue<EntityReference>("sprk_regardingservicerequest") is not { } requestRef)
            return InquiryReplyTodoOutcome.NotApplicable;

        var serviceRequestId = requestRef.Id;
        var request = await entities.RetrieveAsync(
            InquiryDispositionService.ServiceRequest, serviceRequestId,
            ["sprk_name", "sprk_direction", "sprk_disposition", "statecode", "sprk_regardingmatter"], ct).ConfigureAwait(false);
        if (request.GetAttributeValue<OptionSetValue>("sprk_direction")?.Value != BudgetInquiryExecutor.DirectionOutbound
            || request.GetAttributeValue<OptionSetValue>("sprk_disposition") is not null
            || request.GetAttributeValue<OptionSetValue>("statecode")?.Value == InquiryDispositionService.StateInactive)
            return InquiryReplyTodoOutcome.NotApplicable;

        var todoId = InquiryDispositionService.TodoIdFor(serviceRequestId);
        if (await ExistsAsync(todoId, ct).ConfigureAwait(false))
            return InquiryReplyTodoOutcome.AlreadyExists;

        var name =request.GetAttributeValue<string>("sprk_name")?.Trim();

        var entity = new Entity(Todo, todoId);
        entity["sprk_name"] = TodoName;
        entity["sprk_description"] = $"A reply to the budget inquiry{(string.IsNullOrEmpty(name) ? "" : $" \"{name}\"")} has arrived. "
            + "Read it and record how the inquiry turned out: write-off, budget revised, scope approved or no action.";
        entity["statecode"] = new OptionSetValue(0);   // Active
        entity["statuscode"] = new OptionSetValue(1);  // Open

        // Regarding the MATTER (the standard root: the lookup, the ADR-024 pair and the server's core-ancestor stamp, written by
        // the one builder every server-created To Do uses). A stamp that fails throws, and nothing is created (NFR-01): an
        // unstamped To Do is unreachable.
        if (request.GetAttributeValue<EntityReference>("sprk_regardingmatter") is not { } matterRef)
        {
            logger.LogWarning("Inquiry outcome To Do not created: service request {ServiceRequestId} names no matter", serviceRequestId);
            return InquiryReplyTodoOutcome.Failed;
        }

        var nameField = RegardingNameFields.PrimaryNameField("sprk_matter");
        var matter = await entities.RetrieveAsync(
            "sprk_matter", matterRef.Id,
            AssignedToDefaults.ResponsibleContactColumns.Concat(nameField is null ? [] : [nameField]).ToArray(), ct).ConfigureAwait(false);
        var matterName = (nameField is null ? null : matter.GetAttributeValue<string>(nameField))?.Trim() ?? string.Empty;

        await new TodoRegardingBuilder(recordTypes, coreAncestors, logger)
            .ApplyResolverFieldsAsync(entity, "sprk_matter", matterRef.Id, matterName, ct).ConfigureAwait(false);

        // The service request the UI calls the disposition route with: a machine-readable first line of the notes. The
        // typed sprk_regardingservicerequest lookup cannot ALSO be set: ADR-024 allows one specific regarding lookup.
        entity["sprk_notes"] = $"{NotesKey}{serviceRequestId:D}";

        // The person it is FOR: the matter's Assigned To Internal contact (then first attorney), else blank + warning.
        var assignee = AssignedToDefaults.ResponsibleContactOf(matter);

        if (assignee is { } contactId)
        {
            entity[AssignedToDefaults.AssignedToAttribute] = new EntityReference("contact", contactId);
        }
        else
        {
            logger.LogWarning(
                "inquiry_todo_unassigned: the matter of service request {ServiceRequestId} has no Assigned To Internal or first "
                + "attorney contact; sprk_assignedto is left blank and the matter's team owns the To Do",
                serviceRequestId);
        }

        // Due today: the assignee's today (D-25), else UTC.
        var day = await new DataverseRecipientDays(entities, clock.GetUtcNow()).ForAsync(assignee, null, ct).ConfigureAwait(false);
        entity["sprk_duedate"] = DateTime.SpecifyKind(day.Today.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        // Owner: the one resolver (secure-if-any), exactly as a server-created task is owned. Never an individual.
        var owner = await ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(entity, new RecordOwnershipParent("sprk_matter", matterRef.Id)),
            ct).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            logger.LogWarning("Inquiry outcome To Do not created: no owner resolved ({Code}: {Reason}) | ServiceRequestId: {ServiceRequestId}",
                owner.RefusalCode, owner.Reason, serviceRequestId);
            return InquiryReplyTodoOutcome.Failed;
        }

        entity["ownerid"] = new EntityReference("team", owner.OwningTeamId!.Value);
        owner.StampCreatorOn(entity);

        try
        {
            await entities.CreateAsync(entity, ct).ConfigureAwait(false);
            return InquiryReplyTodoOutcome.Created;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The same id already exists (an earlier reply raised it, or one raced this one): idempotent. Anything else is real.
            if (await ExistsAsync(todoId, ct).ConfigureAwait(false))
                return InquiryReplyTodoOutcome.AlreadyExists;
            throw;
        }
    }

    private async Task<bool> ExistsAsync(Guid todoId, CancellationToken ct)
    {
        try
        {
            var found = await entities.RetrieveAsync(Todo, todoId, ["sprk_todoid"], ct).ConfigureAwait(false);
            return found is not null && found.Id == todoId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
