using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Communication.CommunicationRecordAuthorizationHost;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// unified-access-control-r2 task 161 — every <c>/api/communications</c> route that acts on a caller-named record
/// asks Dataverse, AS THE CALLER, about that exact record before the handler runs
/// (<see cref="CommunicationRecordAuthorizationFilter"/>). Exercised through the REAL endpoint mappers
/// (<see cref="CommunicationRecordAuthorizationHost"/>): a filter tested in isolation, or a test that only asserts a
/// mock was called, would not show the gate is attached to the route.
/// </summary>
/// <remarks>
/// Unknown equals denied (owner round 9): each route-id deny is compared against the body the HANDLER itself returns
/// for a record that is genuinely absent, so "denied" and "does not exist" are proven byte-identical apart from the
/// correlation id — not merely both 404.
/// </remarks>
[Trait("Category", "Security")]
public class CommunicationRecordAuthorizationContractTests : IClassFixture<CommunicationRecordAuthorizationHost>
{
    private const string CommunicationSet = "sprk_communications";
    private const string ThreadSet = "sprk_communicationthreads";
    private const string MatterSet = "sprk_matters";
    private const string AccountSet = "sprk_communicationaccounts";
    private const int ReviewActionProposed = 100000001;

    private readonly CommunicationRecordAuthorizationHost _host;

    public CommunicationRecordAuthorizationContractTests(CommunicationRecordAuthorizationHost host)
    {
        _host = host;
        _host.Reset();
    }

    // =============================================================================================
    // Live-verified names (task-161 note §2) — a wrong set or privilege name denies every caller silently
    // =============================================================================================

    [Fact]
    public void TheEntitySetAndPrivilegeNames_AreTheLiveVerifiedValues()
    {
        CommunicationRecordAuthorizationFilter.CommunicationEntitySet.Should().Be("sprk_communications");
        CommunicationRecordAuthorizationFilter.ThreadEntitySet.Should().Be("sprk_communicationthreads");
        CommunicationRecordAuthorizationFilter.CommunicationAccountEntitySet.Should().Be("sprk_communicationaccounts");
        CommunicationRecordAuthorizationFilter.TemplateEntitySet.Should().Be("templates");
        CommunicationRecordAuthorizationFilter.EmailReviewLogEntity.Should().Be("sprk_emailreviewlog");
        CommunicationRecordAuthorizationFilter.CreateEventPrivilege.Should().Be("prvCreatesprk_Event");
        CommunicationRecordAuthorizationFilter.AssignEventPrivilege.Should().Be("prvAssignsprk_Event");
        CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege.Should().Be("prvCreatesprk_Document");
    }

    // =============================================================================================
    // GET /{id}/status — DELETED (owner round 10 item 1: no caller in the repo, in no published API description)
    // =============================================================================================

    [Fact]
    public async Task Status_TheDeletedRoute_AnswersNobody_AndReadsNothing()
    {
        // Even a caller who can see the communication gets no route: the app-only status read (S-79) is gone, not gated.
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        AppOnlyCommunication(id);

        var response = await _host.SendAsync(Request(HttpMethod.Get, $"/api/communications/{id}/status"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("graphMessageId");
        AssertNoAppOnlyDataverseCall("no handler reads the communication any more");
    }

    // =============================================================================================
    // POST /{id}/archive — visibility, AppendTo on the communication, Create on sprk_document
    // =============================================================================================

    [Fact]
    public async Task Archive_InvisibleOrInternalOnlyCommunication_GetsTheHandlersOwnNotFound_AndNothingIsArchived()
    {
        var hidden = Guid.NewGuid();
        AppOnlyCommunication(hidden);
        var internalOnly = Guid.NewGuid();
        VisibleCommunication(internalOnly, internalOnly: true);
        _host.Identity.Setup(i => i.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        GrantArchiveRights(hidden);
        GrantArchiveRights(internalOnly);

        var deniedHidden = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{hidden}/archive"));
        var deniedInternal = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{internalOnly}/archive"));

        AssertNoAppOnlyDataverseCall("ArchiveExistingAsync must not run (no SPE upload, no sprk_document create)");
        _host.Downloads.Should().BeEmpty();

        _host.Reset();
        VisibleCommunication(hidden);
        GrantArchiveRights(hidden);
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_communication", hidden, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("does not exist"));
        var absent = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{hidden}/archive"));

        deniedHidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(deniedHidden)).Should().Be(await NormalizedBodyAsync(absent));
        deniedInternal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(deniedInternal)).Should().Be(
            (await NormalizedBodyAsync(absent)).Replace(hidden.ToString(), internalOnly.ToString()));
    }

    [Theory]
    [InlineData("no AppendTo")]
    [InlineData("no Create privilege")]
    public async Task Archive_VisibleButWithoutTheRightToFileAgainstIt_Is403AndNothingIsWritten(string missing)
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        if (missing != "no AppendTo")
            _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.AppendTo);
        if (missing != "no Create privilege")
            _host.Probe.Hold(CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/archive"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, CommunicationRecordAuthorizationFilter.ArchiveDenyReasonCode);
        AssertNoAppOnlyDataverseCall("nothing may be archived");
    }

    [Fact]
    public async Task Archive_AuthorizedCaller_ArchivesAsToday()
    {
        var id = Guid.NewGuid();
        var existingArchive = Guid.NewGuid();
        VisibleCommunication(id);
        GrantArchiveRights(id);
        AppOnlyCommunication(id);
        _host.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { new("sprk_document", existingArchive) }));

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/archive"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("archiveDocumentId").GetGuid().Should().Be(existingArchive);
        json.GetProperty("alreadyArchived").GetBoolean().Should().BeTrue();
    }

    // =============================================================================================
    // POST /{id}/suggest-associations — visibility, then candidates trimmed BEFORE the ladder decides
    // =============================================================================================

    [Fact]
    public async Task SuggestAssociations_InvisibleCommunication_GetsTheHandlersOwnNotFound_AndTheEngineNeverRuns()
    {
        var id = Guid.NewGuid();
        AppOnlyCommunication(id);

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/suggest-associations"));
        _host.Rung.Evaluations.Should().Be(0, "the Association Engine must not evaluate a communication the caller cannot see");

        _host.Reset();
        VisibleCommunication(id);
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_communication", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("does not exist"));
        var absent = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/suggest-associations"));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Be(await NormalizedBodyAsync(absent));
    }

    [Fact]
    public async Task SuggestAssociations_ACandidateTheCallerCannotRead_IsAbsentEverywhere_AndTheStatusIsDecidedWithoutIt()
    {
        var id = Guid.NewGuid();
        var readable = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        VisibleCommunication(id);
        AppOnlyCommunication(id);
        ScriptTwoConflictingMatters(readable, hidden);

        // Control: both readable → the engine sees a high-confidence conflict.
        UserCanRead(readable, "Acme v Widgets");
        UserCanRead(hidden, "Secure Matter");
        var both = await (await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/suggest-associations")))
            .Content.ReadFromJsonAsync<JsonElement>();
        both.GetProperty("status").GetString().Should().Be("Ambiguous");

        // The caller cannot read one of them.
        _host.UserClient.Reset();
        UserCanRead(readable, "Acme v Widgets");
        UserCannotRead(hidden);
        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/suggest-associations"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(hidden.ToString(), "no trace of a hidden candidate — candidate, signal or provenance");
        text.Should().NotContain(hidden.ToString("N"));
        var json = JsonDocument.Parse(text).RootElement;
        var candidates = json.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Should().ContainSingle().Which.GetProperty("targetId").GetString().Should().Be(readable.ToString());
        candidates[0].GetProperty("conflict").GetBoolean().Should().BeFalse("the conflict existed only with the hidden record");
        json.GetProperty("status").GetString().Should().Be("Resolved",
            "decided over the readable set alone, one core match ≥ the threshold auto-files — an Ambiguous status would reveal a hidden second candidate");
        json.GetProperty("autoFileEligible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SuggestAssociations_WithoutABearerToken_FailsTheRequest_NotAnEmptyList()
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        AppOnlyCommunication(id);
        ScriptTwoConflictingMatters(Guid.NewGuid(), Guid.NewGuid());
        _host.UserClient
            .Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired, "no user context"));

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/suggest-associations", withToken: false));

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
    }

    // =============================================================================================
    // POST /{id}/confirm-affinity — the never-fails contract: every deny is the 200 no-op
    // =============================================================================================

    [Theory]
    [InlineData("invisible")]
    [InlineData("non-existent")]
    [InlineData("regarding names another record")]
    [InlineData("no Write")]
    public async Task ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten(string reason)
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        switch (reason)
        {
            case "invisible":
                AppOnlyCommunication(id, from: "client@outside.com");
                _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);
                break;
            case "non-existent":
                break;
            case "regarding names another record":
                VisibleCommunication(id, regardingMatter: Guid.NewGuid());
                AppOnlyCommunication(id, from: "client@outside.com");
                _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);
                break;
            case "no Write":
                VisibleCommunication(id, regardingMatter: target);
                AppOnlyCommunication(id, from: "client@outside.com");
                _host.Probe.Grant(CommunicationSet, id, AccessRights.Read);
                break;
        }

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/confirm-affinity",
            new { targetEntityType = "sprk_matter", targetRecordId = target.ToString() }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"recordedSignals\":0}");
        AssertNoAppOnlyDataverseCall("AffinityStore must never be reached on a deny");
    }

    [Fact]
    public async Task ConfirmAffinity_AuthorizedCaller_RecordsSignalsAsToday()
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        VisibleCommunication(id, regardingMatter: target);
        AppOnlyCommunication(id, from: "client@outside.com");
        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/confirm-affinity",
            new { targetEntityType = "sprk_matter", targetRecordId = target.ToString() }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("recordedSignals").GetInt32().Should().BeGreaterThan(0);
    }

    // =============================================================================================
    // POST /threads — AppendTo on the regarding record; the record's own name, read as the caller
    // =============================================================================================

    [Fact]
    public async Task CreateRecordThread_WithoutAppendTo_Is403_AndANonExistentRecordGetsTheSameAnswer()
    {
        var existing = Guid.NewGuid();
        _host.Probe.Grant(MatterSet, existing, AccessRights.Read); // readable, but not attachable

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/threads",
            new { regardingEntityType = "sprk_matter", regardingRecordId = existing }));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/threads",
            new { regardingEntityType = "sprk_matter", regardingRecordId = Guid.NewGuid() }));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(denied, CommunicationRecordAuthorizationFilter.ThreadCreateDenyReasonCode);
        (await NormalizedBodyAsync(unknown)).Should().Be(await NormalizedBodyAsync(denied));
        AssertNoCreate("sprk_communicationthread");
    }

    [Fact]
    public async Task CreateRecordThread_TypeWithNoEntitySet_Is400BeforeAnyDataverseCall()
    {
        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/threads",
            new { regardingEntityType = "sprk_communication", regardingRecordId = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
        AssertNoDataverseCallAtAll("an unsupported type is a 400 decided from the body alone");
    }

    [Fact]
    public async Task CreateRecordThread_AuthorizedCaller_NamesTheThreadWithTheRecordsOwnName_NotTheBodys()
    {
        var matter = Guid.NewGuid();
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read | AccessRights.AppendTo);
        _host.Query.Visible(MatterSet, matter, """{ "sprk_mattername": "Acme v Widgets (as the caller reads it)" }""");
        DataverseEntity? created = null;
        _host.Entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => created = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Mixed case: authorized as sprk_matter.
        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/threads",
            new { regardingEntityType = "sprk_Matter", regardingRecordId = matter, regardingRecordName = "SPOOFED NAME" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Probe.Calls.Should().Contain($"rights {MatterSet}({matter})");
        created.Should().NotBeNull();
        ((string)created!["sprk_regardingrecordname"]).Should().Be("Acme v Widgets (as the caller reads it)");
        ((string)created["sprk_name"]).Should().Be("Acme v Widgets (as the caller reads it)");
        created.Attributes.Values.OfType<string>().Should().NotContain("SPOOFED NAME");
        created.GetAttributeValue<EntityReference>("sprk_regardingmatter").LogicalName.Should().Be("sprk_matter");
    }

    // =============================================================================================
    // POST /{communicationId}/create-task — visibility, AppendTo, Create (+ Assign) on sprk_event
    // =============================================================================================

    [Fact]
    public async Task CreateAdHocTask_InvisibleCommunication_IsTheNotFoundANonExistentIdGets()
    {
        var hidden = Guid.NewGuid();
        AppOnlyCommunication(hidden);
        var matter = Guid.NewGuid();
        GrantTaskRights(matter);

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{hidden}/create-task", AdHocBody(matter)));
        var unknownId = Guid.NewGuid();
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{unknownId}/create-task", AdHocBody(matter)));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Contain("COMMUNICATION_NOT_FOUND");
        (await NormalizedBodyAsync(unknown)).Should().Be((await NormalizedBodyAsync(denied)).Replace(hidden.ToString(), unknownId.ToString()));
        AssertNoCreate("sprk_event");
    }

    [Theory]
    [InlineData("no AppendTo")]
    [InlineData("non-existent regarding")]
    [InlineData("type with no entity set")]
    [InlineData("no Create privilege")]
    [InlineData("assigned to someone else without Assign")]
    public async Task CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(string missing)
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        var matter = Guid.NewGuid();
        GrantTaskRights(matter);
        object body = AdHocBody(matter);
        switch (missing)
        {
            case "no AppendTo": _host.Probe.Grant(MatterSet, matter, AccessRights.Read); break;
            case "non-existent regarding": body = AdHocBody(Guid.NewGuid()); break;
            case "type with no entity set": body = AdHocBody(matter, regardingEntity: "sprk_recordtype_ref"); break;
            case "no Create privilege": _host.Probe.Reset(); _host.Probe.Grant(MatterSet, matter, AccessRights.AppendTo); break;
            case "assigned to someone else without Assign": body = AdHocBody(matter, assignedTo: Guid.NewGuid()); break;
        }

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/create-task", body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, CommunicationRecordAuthorizationFilter.CreateTaskDenyReasonCode);
        AssertNoCreate("sprk_event");
        AssertNoCreate("sprk_emailreviewlog");
    }

    [Fact]
    public async Task CreateAdHocTask_DeniedAndNonExistentRegarding_AreByteIdentical()
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        var matter = Guid.NewGuid();
        GrantTaskRights(matter);
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read); // denied: no AppendTo

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/create-task", AdHocBody(matter)));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/create-task", AdHocBody(Guid.NewGuid())));

        (await NormalizedBodyAsync(unknown)).Should().Be(await NormalizedBodyAsync(denied));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAdHocTask_AuthorizedCaller_CreatesAsToday_AndNamingOneselfNeedsNoAssign(bool assignToSelf)
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        var matter = Guid.NewGuid();
        GrantTaskRights(matter); // Create held; Assign deliberately NOT held

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/create-task",
            AdHocBody(matter, assignedTo: assignToSelf ? CallerSystemUserId : null)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.Probe.Calls.Should().NotContain($"privilege {CommunicationRecordAuthorizationFilter.AssignEventPrivilege}");
        _host.Entities.Verify(e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == "sprk_event"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============================================================================================
    // Proposals — dismiss / apply / undo / create-task/apply: proposal visibility via its communication
    // =============================================================================================

    [Theory]
    [InlineData("dismiss")]
    [InlineData("apply")]
    [InlineData("undo")]
    [InlineData("create-task/apply")]
    public async Task Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(string action)
    {
        var reviewLogId = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var matter = Guid.NewGuid();
        ProposalRow(reviewLogId, communication, matter, createTask: action == "create-task/apply");
        GrantTaskRights(matter);

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/{action}", new { }));
        AssertNoCreate("sprk_emailreviewlog");
        AssertNoCreate("sprk_event");
        AssertNoImpersonatedWrite();

        // The service's own answer for an unknown reviewLogId.
        _host.Reset();
        VisibleCommunication(communication);
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_emailreviewlog", reviewLogId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("does not exist"));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/{action}", new { }));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Contain("PROPOSAL_NOT_FOUND");
        (await NormalizedBodyAsync(denied)).Should().Be(await NormalizedBodyAsync(unknown));
    }

    [Fact]
    public async Task ProposalDismiss_AuthorizedCaller_DismissesAsToday()
    {
        var reviewLogId = Guid.NewGuid();
        var communication = Guid.NewGuid();
        ProposalRow(reviewLogId, communication, Guid.NewGuid(), createTask: false);
        VisibleCommunication(communication);
        OpenProposal(reviewLogId);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/dismiss"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.Entities.Verify(e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("apply", "after")]
    [InlineData("undo", "before")]
    public async Task ProposalApplyAndUndo_AuthorizedCaller_WriteTheTargetAsTheCallerAsToday(string action, string expectedValue)
    {
        // A visible proposal passes the gate and the service runs to completion exactly as it does today: the
        // target field is written UNDER THE CALLER'S impersonation (apply writes newValue, undo restores oldValue)
        // and one audit row is appended.
        var reviewLogId = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var matter = Guid.NewGuid();
        ProposalRow(reviewLogId, communication, matter, createTask: false,
            suggestion: """{"oldValue":"before","newValue":"after","citation":{"quotedText":"the matter"}}""");
        VisibleCommunication(communication);
        AppOnlyCommunication(communication, subject: "Re: the matter");
        OpenProposal(reviewLogId);
        AllowListedTextField("sprk_matter", "sprk_description");

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/{action}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(reviewLogId.ToString());
        _host.FieldMapping.Verify(f => f.UpdateRecordFieldsAsync(
                "sprk_matter",
                matter,
                It.Is<Dictionary<string, object?>>(d => d.Count == 1 && Equals(d["sprk_description"], expectedValue)),
                It.IsAny<CancellationToken>(),
                CallerSystemUserId),
            Times.Once);
        _host.Entities.Verify(e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("no AppendTo on the target")]
    [InlineData("no Create privilege")]
    [InlineData("assigned to someone else without Assign")]
    public async Task ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated(string missing)
    {
        var reviewLogId = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var matter = Guid.NewGuid();
        ProposalRow(reviewLogId, communication, matter, createTask: true);
        VisibleCommunication(communication);
        GrantTaskRights(matter);
        object body = new { };
        switch (missing)
        {
            case "no AppendTo on the target": _host.Probe.Grant(MatterSet, matter, AccessRights.Read); break;
            case "no Create privilege": _host.Probe.Reset(); _host.Probe.Grant(MatterSet, matter, AccessRights.AppendTo); break;
            case "assigned to someone else without Assign": body = new { assignedTo = Guid.NewGuid() }; break;
        }

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/create-task/apply", body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, CommunicationRecordAuthorizationFilter.CreateTaskApplyDenyReasonCode);
        AssertNoCreate("sprk_event");
        AssertNoCreate("sprk_emailreviewlog");
    }

    [Fact]
    public async Task ProposalCreateTaskApply_AuthorizedCaller_CreatesAsToday()
    {
        var reviewLogId = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var matter = Guid.NewGuid();
        ProposalRow(reviewLogId, communication, matter, createTask: true);
        VisibleCommunication(communication);
        AppOnlyCommunication(communication, subject: "Please file the reply brief by Friday");
        GrantTaskRights(matter);
        OpenProposal(reviewLogId);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/proposals/{reviewLogId}/create-task/apply", new { }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.Entities.Verify(e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == "sprk_event"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============================================================================================
    // POST /{communicationId}/tasks/{taskId}/undo — communication visibility
    // =============================================================================================

    [Fact]
    public async Task TaskUndo_InvisibleCommunication_IsTheNotFoundANonExistentIdGets_BeforeAnyWrite()
    {
        var hidden = Guid.NewGuid();
        AppOnlyCommunication(hidden);
        var unknownId = Guid.NewGuid();

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{hidden}/tasks/{Guid.NewGuid()}/undo"));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{unknownId}/tasks/{Guid.NewGuid()}/undo"));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Contain("COMMUNICATION_NOT_FOUND");
        (await NormalizedBodyAsync(unknown)).Should().Be((await NormalizedBodyAsync(denied)).Replace(hidden.ToString(), unknownId.ToString()));
        AssertNoCreate("sprk_emailreviewlog");
        AssertNoImpersonatedWrite();
    }

    [Fact]
    public async Task TaskUndo_AuthorizedCaller_CancelsTheTaskAsTheCallerAsToday()
    {
        // A visible communication passes the gate and the service runs to completion exactly as it does today: the
        // task is soft-cancelled UNDER THE CALLER'S impersonation and one compensating audit row names the communication.
        var id = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        VisibleCommunication(id);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/{id}/tasks/{taskId}/undo"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(taskId.ToString());
        _host.FieldMapping.Verify(f => f.UpdateRecordFieldsAsync(
                "sprk_event",
                taskId,
                It.Is<Dictionary<string, object?>>(d => d.ContainsKey("sprk_eventstatus")),
                It.IsAny<CancellationToken>(),
                CallerSystemUserId),
            Times.Once);
        _host.Entities.Verify(e => e.CreateAsync(
                It.Is<DataverseEntity>(x => x.LogicalName == "sprk_emailreviewlog"
                                            && x.GetAttributeValue<EntityReference>("sprk_communication").Id == id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =============================================================================================
    // POST /accounts/{id}/verify — Write on the account, before any Graph call or write
    // =============================================================================================

    [Fact]
    public async Task VerifyAccount_WithoutWrite_GetsTheHandlersOwnNotFound_AndNoGraphOrWrite()
    {
        var id = Guid.NewGuid();
        _host.Probe.Grant(AccountSet, id, AccessRights.Read);
        Account(id);

        var denied = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/accounts/{id}/verify"));
        _host.Graph.Verify(g => g.ForApp(), Times.Never);
        AssertNoAppOnlyDataverseCall("the account must not be read or updated");

        _host.Reset();
        _host.Probe.Grant(AccountSet, id, AccessRights.Read | AccessRights.Write);
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_communicationaccount", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("does not exist"));
        var absent = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/accounts/{id}/verify"));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Be(await NormalizedBodyAsync(absent));
    }

    [Fact]
    public async Task VerifyAccount_WithWrite_VerifiesAsToday()
    {
        var id = Guid.NewGuid();
        _host.Probe.Grant(AccountSet, id, AccessRights.Read | AccessRights.Write);
        Account(id);

        var response = await _host.SendAsync(Request(HttpMethod.Post, $"/api/communications/accounts/{id}/verify"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // =============================================================================================
    // Thread / message writes (amendment: Write, not just Read, on the shared record)
    // =============================================================================================

    [Theory]
    [InlineData("rename")]
    [InlineData("pin")]
    [InlineData("deactivate")]
    public async Task ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(string route)
    {
        var threadId = Guid.NewGuid();
        _host.Query.Visible(ThreadSet, threadId);           // the caller can SEE it
        _host.Probe.Grant(ThreadSet, threadId, AccessRights.Read); // but not write it

        var response = await _host.SendAsync(ThreadWriteRequest(route, threadId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _host.Entities.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("pin")]
    [InlineData("deactivate")]
    public async Task ThreadWrite_ByAWriter_WritesAsToday(string route)
    {
        var threadId = Guid.NewGuid();
        _host.Query.Visible(ThreadSet, threadId);
        _host.Probe.Grant(ThreadSet, threadId, AccessRights.Read | AccessRights.Write);

        var response = await _host.SendAsync(ThreadWriteRequest(route, threadId));

        ((int)response.StatusCode).Should().BeInRange(200, 204, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MessageDeactivate_ByAReadOnlyCaller_Is403_AndAWriterDeactivates()
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read);

        var denied = await _host.SendAsync(Request(HttpMethod.Delete, $"/api/communications/{id}"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("MESSAGE_DELETE_FORBIDDEN");

        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);
        var allowed = await _host.SendAsync(Request(HttpMethod.Delete, $"/api/communications/{id}"));
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // =============================================================================================
    // PATCH /{id}/filing — task 147 r1c (owner round 36 item 2): the re-file, in 161's family
    // =============================================================================================

    private static readonly Dictionary<string, object?> FilingBody = new()
    {
        ["sprk_RegardingMatter@odata.bind"] = null,
        ["sprk_regardingrecordid"] = null,
    };

    [Fact]
    public async Task Filing_ABodyNamingAnythingButTheFiling_Is400_BeforeAnyRightsQuestion()
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);

        var response = await _host.SendAsync(Request(HttpMethod.Patch, $"/api/communications/{id}/filing",
            new Dictionary<string, object?> { ["sprk_RegardingMatter@odata.bind"] = null, ["sprk_associationstatus"] = null }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("child_record.not_filing");
        _host.Probe.Calls.Should().BeEmpty("the shape is answered before any Dataverse question");
        AssertNothingDownstream();
    }

    [Fact]
    public async Task Filing_ByAReadOnlyCaller_Is403_InTheFamilysOwnShape_AndNothingDownstreamRuns()
    {
        var id = Guid.NewGuid();
        VisibleCommunication(id);
        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.AppendTo);

        var denied = await _host.SendAsync(Request(HttpMethod.Patch, $"/api/communications/{id}/filing", FilingBody));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain(CommunicationRecordAuthorizationFilter.RefileDenyCode);
        AssertNothingDownstream();
    }

    // =============================================================================================
    // POST /send and /send-bulk — every body id, on every type and mode, once, before the first send
    // =============================================================================================

    [Theory]
    [InlineData("SharedMailbox")]
    [InlineData("User")]
    public async Task Send_AnAttachmentTheCallerCannotRead_Is403_AndUnknownOrUnparseableIdsGetTheSameAnswer(string sendMode)
    {
        var denied = Guid.NewGuid();
        await _host.SeedAttachmentAsync(denied, "b!drive-161", "item-denied"); // it exists — the caller just cannot read it
        _host.Access.GrantDocument(denied, AccessRights.None);

        var deniedResponse = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(sendMode: sendMode, attachments: new[] { denied.ToString() })));
        var unknownResponse = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(sendMode: sendMode, attachments: new[] { Guid.NewGuid().ToString() })));
        var unparseableResponse = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(sendMode: sendMode, attachments: new[] { "not-a-guid" })));

        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(deniedResponse, CommunicationRecordAuthorizationFilter.SendDenyReasonCode);
        var deniedBody = await NormalizedBodyAsync(deniedResponse);
        (await NormalizedBodyAsync(unknownResponse)).Should().Be(deniedBody);
        (await NormalizedBodyAsync(unparseableResponse)).Should().Be(deniedBody);
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_AThreadTheCallerCannotSee_Is403ForAMessage_AndAnUnknownThreadGetsTheSameAnswer()
    {
        var hiddenThread = Guid.NewGuid(); // exists app-only; not in the caller's impersonated view

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(type: "Message", threadId: hiddenThread)));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(type: "Message", threadId: Guid.NewGuid())));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(denied, CommunicationRecordAuthorizationFilter.SendDenyReasonCode);
        (await NormalizedBodyAsync(unknown)).Should().Be(await NormalizedBodyAsync(denied));
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_AnInheritFromCommunicationTheCallerCannotSee_Is403_AndAnUnknownOneGetsTheSameAnswer()
    {
        var hidden = Guid.NewGuid();
        AppOnlyCommunication(hidden);

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(inheritFrom: hidden)));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(inheritFrom: Guid.NewGuid())));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await NormalizedBodyAsync(unknown)).Should().Be(await NormalizedBodyAsync(denied));
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_AnAssociationWithoutAppendTo_Is403_AndUnknownOrUnaddressableTargetsGetTheSameAnswer()
    {
        var matter = Guid.NewGuid();
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read); // readable, not attachable

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(association: ("sprk_matter", matter))));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(association: ("sprk_matter", Guid.NewGuid()))));
        var unaddressable = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(association: ("sprk_recordtype_ref", matter))));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deniedBody = await NormalizedBodyAsync(denied);
        (await NormalizedBodyAsync(unknown)).Should().Be(deniedBody);
        (await NormalizedBodyAsync(unaddressable)).Should().Be(deniedBody);
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_AVisibleInheritFromCommunicationWhoseRegardingTheCallerCannotAttachTo_Is403()
    {
        var source = Guid.NewGuid();
        var securedMatter = Guid.NewGuid();
        VisibleCommunication(source, regardingMatter: securedMatter); // the caller can read the email…
        _host.Probe.Grant(MatterSet, securedMatter, AccessRights.Read); // …but may not file against its matter

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(inheritFrom: source)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, CommunicationRecordAuthorizationFilter.SendDenyReasonCode);
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_151Attachments_GetsTodays400_BeforeAnyRightsQuery()
    {
        var ids = Enumerable.Range(0, 151).Select(_ => Guid.NewGuid().ToString()).ToArray();

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send", SendBody(attachments: ids)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ATTACHMENT_LIMIT_EXCEEDED");
        _host.Access.DocumentCalls.Should().BeEmpty();
        _host.Probe.Calls.Should().BeEmpty();
        AssertNothingSent();
    }

    [Theory]
    [InlineData("SharedMailbox")]
    [InlineData("User")]
    public async Task Send_AnAuthorizedEmail_IsSentAsToday_WithItsAttachment(string sendMode)
    {
        var document = Guid.NewGuid();
        var matter = Guid.NewGuid();
        await _host.SeedAttachmentAsync(document, "b!drive-161", "item-ok");
        _host.Access.GrantDocument(document, AccessRights.Read);
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read | AccessRights.AppendTo);

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(sendMode: sendMode, attachments: new[] { document.ToString() }, association: ("sprk_matter", matter))));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.EmailSender.Sent.Should().ContainSingle().Which.Attachments.Should().ContainSingle();
        _host.Downloads.Should().ContainSingle();
    }

    [Fact]
    public async Task Send_AnAuthorizedMessage_IntoAVisibleThread_InheritingFromAVisibleCommunication_IsSentAsToday()
    {
        var thread = Guid.NewGuid();
        var source = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _host.Query.Visible(ThreadSet, thread);
        VisibleCommunication(source, regardingMatter: matter);
        AppOnlyCommunication(source);
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read | AccessRights.AppendTo);

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send",
            SendBody(type: "Message", threadId: thread, inheritFrom: source)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.MessageSender.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task SendBulk_AnUnreadableAttachment_Is403ForTheWholeRequest_AndAnUnknownOneGetsTheSameAnswer()
    {
        var denied = Guid.NewGuid();
        await _host.SeedAttachmentAsync(denied, "b!drive-161", "item-bulk");

        var deniedResponse = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send-bulk",
            BulkBody(attachments: new[] { denied.ToString() })));
        var unknownResponse = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send-bulk",
            BulkBody(attachments: new[] { Guid.NewGuid().ToString() })));

        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a deny is one 403, never a 207 with per-recipient failures");
        await ShouldCarryReasonCode(deniedResponse, CommunicationRecordAuthorizationFilter.SendBulkDenyReasonCode);
        (await NormalizedBodyAsync(unknownResponse)).Should().Be(await NormalizedBodyAsync(deniedResponse));
        AssertNothingSent();
    }

    [Fact]
    public async Task SendBulk_AnAssociationWithoutAppendTo_Is403_AndNothingIsSent()
    {
        var matter = Guid.NewGuid();
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read);

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send-bulk",
            BulkBody(association: ("sprk_matter", matter))));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        AssertNothingSent();
    }

    [Fact]
    public async Task SendBulk_AnAuthorizedCaller_SendsToEveryRecipientAsToday()
    {
        var document = Guid.NewGuid();
        await _host.SeedAttachmentAsync(document, "b!drive-161", "item-bulk-ok");
        _host.Access.GrantDocument(document, AccessRights.Read);

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/send-bulk",
            BulkBody(attachments: new[] { document.ToString() })));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _host.EmailSender.Sent.Should().HaveCount(2);
    }

    // =============================================================================================
    // POST /template/render — Read on the regarding record, a readable template, the merge read AS THE CALLER
    // =============================================================================================

    [Fact]
    public async Task TemplateRender_ARegardingRecordTheCallerCannotRead_Is403WithNoTraceOfIt_AndAnUnknownOneGetsTheSameAnswer()
    {
        var template = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _host.Query.Visible("templates", template);
        _host.Query.Visible(MatterSet, matter, """{ "sprk_mattername": "CANARY-161-SECRET" }""");
        _host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_matter", matter) { ["sprk_mattername"] = "CANARY-161-SECRET" },
            }));

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render",
            new { templateId = template, regardingEntityType = "sprk_matter", regardingRecordId = matter }));
        var unknown = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render",
            new { templateId = template, regardingEntityType = "sprk_matter", regardingRecordId = Guid.NewGuid() }));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(denied, CommunicationRecordAuthorizationFilter.TemplateRenderDenyReasonCode);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("CANARY-161-SECRET");
        (await NormalizedBodyAsync(unknown)).Should().Be(await NormalizedBodyAsync(denied));
        _host.Templates.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TemplateRender_ATypeOutsideTheCatalogue_Is400BeforeAnyDataverseCall()
    {
        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render",
            new { templateId = Guid.NewGuid(), regardingEntityType = "systemuser", regardingRecordId = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
        AssertNoDataverseCallAtAll("an unsupported type is a 400 decided from the body alone");
        _host.Templates.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TemplateRender_ATemplateTheCallerCannotRead_GetsExactlyWhatANonExistentTemplateGets()
    {
        var template = Guid.NewGuid();

        var denied = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render", new { templateId = template }));
        _host.Templates.VerifyNoOtherCalls();

        // The handler's own answer for a template that does not exist (readable to the caller, gone for the app).
        _host.Query.Visible("templates", template);
        _host.Templates
            .Setup(t => t.FetchAndRenderAsync(template, It.IsAny<Dictionary<string, object?>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Fail($"Email template not found: {template}"));
        var absent = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render", new { templateId = template }));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(denied)).Should().Be(await NormalizedBodyAsync(absent));
    }

    [Fact]
    public async Task TemplateRender_AuthorizedCaller_ReadsTheRegardingAsTheCaller_NeverThroughTheAppOnlyService()
    {
        var template = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _host.Query.Visible("templates", template);
        _host.Query.Visible(MatterSet, matter, """{ "sprk_mattername": "Acme v Widgets" }""");
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read);
        Dictionary<string, object?>? variables = null;
        _host.Templates
            .Setup(t => t.FetchAndRenderAsync(template, It.IsAny<Dictionary<string, object?>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Dictionary<string, object?>, string, string, CancellationToken>((_, vars, _, _, _) => variables = vars)
            .ReturnsAsync(EmailTemplateResult.Ok("Re: Acme v Widgets", "<p>body</p>", isHtml: true));

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render",
            new { templateId = template, regardingEntityType = "sprk_matter", regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        variables!["sprk_mattername"].Should().Be("Acme v Widgets");
        _host.Query.Calls.Should().Contain(c => c.Set == MatterSet && c.Caller == CallerSystemUserId);
        AssertNoAppOnlyDataverseCall("the regarding record is read as the caller, never through IGenericEntityService");
    }

    [Theory]
    [InlineData("the merge read faults")]
    [InlineData("no bearer token")]
    [InlineData("unresolved caller")]
    public async Task TemplateRender_AFaultOrAMissingCallerContext_IsANon2xx_AndNothingIsRendered(string fault)
    {
        var template = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _host.Query.Visible("templates", template);
        _host.Query.Visible(MatterSet, matter, """{ "sprk_mattername": "Acme v Widgets" }""");
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read);
        switch (fault)
        {
            case "the merge read faults": _host.Query.ThrowOnSet = MatterSet; break;
            case "unresolved caller":
                _host.Callers.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
                break;
        }

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render",
            new { templateId = template, regardingEntityType = "sprk_matter", regardingRecordId = matter },
            withToken: fault != "no bearer token"));

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        _host.Templates.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TemplateRender_WithNoRegarding_RendersAsToday()
    {
        var template = Guid.NewGuid();
        _host.Query.Visible("templates", template);
        _host.Templates
            .Setup(t => t.FetchAndRenderAsync(template, It.IsAny<Dictionary<string, object?>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Ok("Subject", "<p>Body</p>", isHtml: true));

        var response = await _host.SendAsync(Request(HttpMethod.Post, "/api/communications/template/render", new { templateId = template }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Probe.Calls.Should().BeEmpty("there is no regarding record to authorize");
    }

    // =============================================================================================
    // Filter reach and order, through the REAL mappers — and fail closed on every seam
    // =============================================================================================

    public static readonly TheoryData<string> AllRecordGatedRoutes = new()
    {
        "send", "send-bulk", "template/render", "archive", "suggest-associations", "confirm-affinity",
        "threads", "create-task", "proposals/apply", "proposals/dismiss", "proposals/create-task/apply",
        "proposals/undo", "tasks/undo", "accounts/verify", "threads/rename", "threads/pin", "threads/deactivate",
        "message/deactivate", "filing",
    };

    [Theory]
    [MemberData(nameof(AllRecordGatedRoutes))]
    public async Task EveryRoute_ACallerWithNoRights_IsDenied_AndNothingDownstreamRuns(string route)
    {
        // Nothing visible, nothing granted, no privilege held.
        var response = await _host.SendAsync(RouteRequest(route, Guid.NewGuid()));

        AssertDenied(route, response, await response.Content.ReadAsStringAsync());
        AssertNothingDownstream();
    }

    [Theory]
    [MemberData(nameof(AllRecordGatedRoutes))]
    public async Task EveryRoute_ACallerWithNoOidClaim_IsStoppedByTheIdentityPreconditionFirst(string route)
    {
        if (route is "template/render")
        {
            return; // never carried the identity filter (app-level route); the record gate alone denies it (above)
        }

        var response = await _host.SendAsync(RouteRequest(route, Guid.NewGuid(), withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "COMMUNICATION_NOT_AUTHORIZED");
        _host.Probe.Calls.Should().BeEmpty("the identity precondition runs before the record gate");
        _host.Query.Calls.Should().BeEmpty();
    }

    /// <summary>Every record-gated route × every id-independent fault (ADR-003: each one denies, for every route).</summary>
    public static IEnumerable<object[]> FailClosedCases() =>
        AllRecordGatedRoutes
            .Select(row => (string)row[0])
            .SelectMany(route => new[] { "no bearer token", "unresolved caller", "seam throws" }
                .Select(fault => new object[] { route, fault }));

    [Theory]
    [MemberData(nameof(FailClosedCases))]
    public async Task EveryRoute_FailsClosed_TheSameWayWhetherOrNotTheRecordExists(string route, string fault)
    {
        var existing = Guid.NewGuid();
        AuthorizeFully(route, existing);
        ApplyFault(fault);

        var withRecord = await _host.SendAsync(RouteRequest(route, existing, withToken: fault != "no bearer token"));
        var withRecordBody = await withRecord.Content.ReadAsStringAsync();
        AssertDenied(route, withRecord, withRecordBody, failClosed: true);
        AssertNothingDownstream();

        var unknown = Guid.NewGuid();
        var withoutRecord = await _host.SendAsync(RouteRequest(route, unknown, withToken: fault != "no bearer token"));
        (await NormalizedBodyAsync(withoutRecord)).Should().Be(
            (await NormalizedBodyAsync(withRecord)).Replace(existing.ToString(), unknown.ToString()));
    }

    // =============================================================================================
    // Request builders and route tables
    // =============================================================================================

    private static object SendBody(
        string type = "Email",
        string sendMode = "SharedMailbox",
        string[]? attachments = null,
        Guid? threadId = null,
        Guid? inheritFrom = null,
        (string Type, Guid Id)? association = null) => new
        {
            to = new[] { "recipient@outside.com" },
            subject = "Re: the matter",
            body = "<p>Please see attached.</p>",
            communicationType = type,
            sendMode,
            attachmentDocumentIds = attachments,
            threadId,
            inheritRegardingFromCommunicationId = inheritFrom,
            associations = association is { } a ? new[] { new { entityType = a.Type, entityId = a.Id } } : null,
        };

    private static object BulkBody(string[]? attachments = null, (string Type, Guid Id)? association = null) => new
    {
        subject = "Quarterly update",
        body = "<p>Update.</p>",
        recipients = new[] { new { to = "one@outside.com" }, new { to = "two@outside.com" } },
        attachmentDocumentIds = attachments,
        associations = association is { } a ? new[] { new { entityType = a.Type, entityId = a.Id } } : null,
    };

    private static HttpRequestMessage RouteRequest(string route, Guid id, bool withToken = true, bool withOid = true)
    {
        HttpRequestMessage Post(string url, object? body = null) => Request(HttpMethod.Post, url, body, withToken, withOid);

        return route switch
        {
            "send" => Post("/api/communications/send", SendBody(attachments: new[] { id.ToString() })),
            "send-bulk" => Post("/api/communications/send-bulk", BulkBody(attachments: new[] { id.ToString() })),
            "template/render" => Post("/api/communications/template/render", new { templateId = id, regardingEntityType = "sprk_matter", regardingRecordId = id }),
            "archive" => Post($"/api/communications/{id}/archive"),
            "suggest-associations" => Post($"/api/communications/{id}/suggest-associations"),
            "confirm-affinity" => Post($"/api/communications/{id}/confirm-affinity", new { targetEntityType = "sprk_matter", targetRecordId = id.ToString() }),
            "threads" => Post("/api/communications/threads", new { regardingEntityType = "sprk_matter", regardingRecordId = id }),
            "create-task" => Post($"/api/communications/{id}/create-task", AdHocBody(id)),
            "proposals/apply" => Post($"/api/communications/proposals/{id}/apply", new { }),
            "proposals/dismiss" => Post($"/api/communications/proposals/{id}/dismiss"),
            "proposals/create-task/apply" => Post($"/api/communications/proposals/{id}/create-task/apply", new { }),
            "proposals/undo" => Post($"/api/communications/proposals/{id}/undo"),
            "tasks/undo" => Post($"/api/communications/{id}/tasks/{Guid.NewGuid()}/undo"),
            "accounts/verify" => Post($"/api/communications/accounts/{id}/verify"),
            "threads/rename" => Post($"/api/communications/threads/{id}/rename", new { name = "Renamed" }),
            "threads/pin" => Request(HttpMethod.Patch, $"/api/communications/threads/{id}/pin", new { pinned = true }, withToken, withOid),
            "threads/deactivate" => Request(HttpMethod.Delete, $"/api/communications/threads/{id}", null, withToken, withOid),
            "message/deactivate" => Request(HttpMethod.Delete, $"/api/communications/{id}", null, withToken, withOid),
            "filing" => Request(HttpMethod.Patch, $"/api/communications/{id}/filing", FilingBody, withToken, withOid),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
        };
    }

    /// <summary>Every right the route asks for, on the record the request names — so only the injected fault can deny.</summary>
    private void AuthorizeFully(string route, Guid id)
    {
        switch (route)
        {
            case "suggest-associations":
            case "archive":
                VisibleCommunication(id);
                AppOnlyCommunication(id);
                GrantArchiveRights(id);
                break;
            case "confirm-affinity":
                VisibleCommunication(id, regardingMatter: id);
                AppOnlyCommunication(id);
                _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);
                break;
            case "proposals/dismiss":
                var communication = Guid.NewGuid();
                ProposalRow(id, communication, Guid.NewGuid(), createTask: false);
                VisibleCommunication(communication);
                OpenProposal(id);
                break;
            case "create-task":
                VisibleCommunication(id);
                GrantTaskRights(id);
                break;
            case "threads":
                _host.Probe.Grant(MatterSet, id, AccessRights.Read | AccessRights.AppendTo);
                _host.Query.Visible(MatterSet, id, """{ "sprk_mattername": "Acme" }""");
                break;
            case "accounts/verify":
                _host.Probe.Grant(AccountSet, id, AccessRights.Read | AccessRights.Write);
                Account(id);
                break;
            case "send":
                _host.Access.GrantDocument(id, AccessRights.Read);
                break;
            case "template/render":
                _host.Query.Visible("templates", id);
                _host.Query.Visible(MatterSet, id, """{ "sprk_mattername": "Acme" }""");
                _host.Probe.Grant(MatterSet, id, AccessRights.Read);
                break;
            case "send-bulk":
                _host.Access.GrantDocument(id, AccessRights.Read);
                break;
            case "proposals/apply":
            case "proposals/undo":
            case "proposals/create-task/apply":
                var proposalCommunication = Guid.NewGuid();
                var target = Guid.NewGuid();
                ProposalRow(id, proposalCommunication, target, createTask: route == "proposals/create-task/apply");
                VisibleCommunication(proposalCommunication);
                GrantTaskRights(target);
                OpenProposal(id);
                break;
            case "tasks/undo":
                VisibleCommunication(id);
                break;
            case "threads/rename":
            case "threads/pin":
            case "threads/deactivate":
                _host.Query.Visible(ThreadSet, id);
                _host.Probe.Grant(ThreadSet, id, AccessRights.Read | AccessRights.Write);
                break;
            case "message/deactivate":
            case "filing":
                VisibleCommunication(id);
                _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.Write);
                break;
        }
    }

    private void ApplyFault(string fault)
    {
        switch (fault)
        {
            case "unresolved caller":
                _host.Callers.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
                break;
            case "seam throws":
                // Every caller-scoped seam the routes use faults: the impersonated reads (communication, thread,
                // template), the RetrievePrincipalAccess / privilege probe, and the document decision's data source.
                _host.Probe.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess faulted");
                _host.Query.ThrowOnEverySet = true;
                _host.Access.ThrowOnEveryCall = new HttpRequestException("access data faulted");
                break;
        }
    }

    private static void AssertDenied(string route, HttpResponseMessage response, string body, bool failClosed = false)
    {
        switch (route)
        {
            case "confirm-affinity":
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                body.Should().Be("{\"recordedSignals\":0}");
                break;
            case "archive" or "suggest-associations" or "create-task" or "tasks/undo":
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
                body.Should().Contain("COMMUNICATION_NOT_FOUND");
                break;
            case "proposals/apply" or "proposals/dismiss" or "proposals/create-task/apply" or "proposals/undo":
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
                body.Should().Contain("PROPOSAL_NOT_FOUND");
                break;
            case "accounts/verify":
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
                body.Should().Contain("ACCOUNT_NOT_FOUND");
                break;
            case "template/render" when failClosed:
                ((int)response.StatusCode).Should().BeOneOf(new[] { 403, 404 }, body);
                break;
            default:
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
                break;
        }
    }

    private void AssertNothingSent()
    {
        _host.EmailSender.Sent.Should().BeEmpty("SendAsync must not run");
        _host.MessageSender.Sent.Should().BeEmpty("SendAsync must not run");
        _host.Downloads.Should().BeEmpty("no SPE download");
        _host.Documents.Verify(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _host.Entities.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AssertNothingDownstream()
    {
        AssertNothingSent();
        _host.Entities.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        _host.Rung.Evaluations.Should().Be(0, "the Association Engine must not run");
        _host.Templates.VerifyNoOtherCalls();
        _host.Graph.Verify(g => g.ForApp(), Times.Never);
        AssertNoImpersonatedWrite();
    }

    // =============================================================================================
    // Helpers
    // =============================================================================================

    private void VisibleCommunication(Guid id, bool internalOnly = false, Guid? regardingMatter = null)
    {
        var columns = new Dictionary<string, object?>
        {
            ["sprk_communicationid"] = id.ToString(),
            ["sprk_isinternalonly"] = internalOnly,
            ["sprk_privilegeclassification"] = 100000000,
        };
        if (regardingMatter is { } matter)
        {
            columns["_sprk_regardingmatter_value"] = matter.ToString();
        }

        _host.Query.Visible(CommunicationSet, id, JsonSerializer.Serialize(columns));
    }

    /// <summary>The app-only (BFF identity) view of the communication — what the handlers read after the gate.</summary>
    private void AppOnlyCommunication(Guid id, string from = "client@outside.com", string subject = "Re: the matter")
    {
        var record = new DataverseEntity("sprk_communication", id)
        {
            ["sprk_subject"] = subject,
            ["sprk_from"] = from,
            ["sprk_to"] = "caller@contoso.com",
            ["sprk_body"] = subject,
            ["sprk_bodyformat"] = new OptionSetValue(100000000),
            ["sprk_direction"] = new OptionSetValue(100000000),
        };
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_communication", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
    }

    private void GrantArchiveRights(Guid id)
    {
        _host.Probe.Grant(CommunicationSet, id, AccessRights.Read | AccessRights.AppendTo);
        _host.Probe.Hold(CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege);
    }

    private void GrantTaskRights(Guid matter)
    {
        _host.Probe.Grant(MatterSet, matter, AccessRights.Read | AccessRights.AppendTo);
        _host.Probe.Hold(CommunicationRecordAuthorizationFilter.CreateEventPrivilege);
    }

    private static object AdHocBody(Guid regarding, string regardingEntity = "sprk_matter", Guid? assignedTo = null) => new
    {
        subject = "File the reply brief",
        regardingEntity,
        regardingRecordId = regarding,
        assignedTo,
    };

    private void ProposalRow(
        Guid reviewLogId, Guid communication, Guid matter, bool createTask, int action = ReviewActionProposed, string? suggestion = null)
    {
        var row = new DataverseEntity("sprk_emailreviewlog", reviewLogId)
        {
            ["sprk_communication"] = new EntityReference("sprk_communication", communication),
            ["sprk_targetentity"] = "sprk_matter",
            ["sprk_targetrecordid"] = matter.ToString(),
            ["sprk_targetfield"] = createTask ? "__create_task__:reply-brief" : "sprk_description",
            ["sprk_action"] = new OptionSetValue(action),
            ["sprk_aisuggestion"] = suggestion ?? (createTask
                ? """{"subject":"File the reply brief","citation":{"quotedText":"reply brief"}}"""
                : """{"proposedValue":"x"}"""),
        };
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_emailreviewlog", reviewLogId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    /// <summary>The still-open walk finds this proposal as the open Proposed row for its key.</summary>
    private void OpenProposal(Guid reviewLogId)
    {
        _host.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_emailreviewlog", reviewLogId) { ["sprk_action"] = new OptionSetValue(ReviewActionProposed) },
            }));
    }

    /// <summary>The apply-time allow-list: <paramref name="field"/> on <paramref name="entity"/> is an enabled Text entry.</summary>
    private void AllowListedTextField(string entity, string field)
    {
        var recordTypeRef = Guid.NewGuid();
        _host.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { new("sprk_recordtype_ref", recordTypeRef) }));
        _host.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_emailupdatefield"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_emailupdatefield", Guid.NewGuid()) { ["sprk_fieldtype"] = new OptionSetValue(100000000) /* Text */ },
            }));
    }

    private void Account(Guid id)
    {
        _host.Entities.Setup(e => e.RetrieveAsync("sprk_communicationaccount", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communicationaccount", id)
            {
                ["sprk_emailaddress"] = "shared@contoso.com",
                ["sprk_name"] = "Shared mailbox",
                ["sprk_sendenabled"] = false,
                ["sprk_receiveenabled"] = false,
            });
    }

    private void ScriptTwoConflictingMatters(Guid readable, Guid hidden)
    {
        foreach (var target in new[] { readable, hidden })
        {
            _host.Rung.Matches.Add(new RungMatch
            {
                Rung = RungKind.ExplicitReference,
                RegardingFieldName = "sprk_regardingmatter",
                Target = new EntityReference("sprk_matter", target),
                Confidence = 0.95,
                Provenance = $"explicit reference to matter {target}",
            });
        }

        // A structural signal whose provenance names the hidden candidate: it must not survive the trim.
        _host.Rung.Matches.Add(new RungMatch
        {
            Rung = RungKind.ExplicitReference,
            Category = "deadline",
            Confidence = 0.6,
            Provenance = $"deadline clause linked to {hidden}",
        });
    }

    private void UserCanRead(Guid matter, string name) =>
        _host.UserClient
            .Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith($"{MatterSet}({matter})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonDocument.Parse(JsonSerializer.Serialize(new { sprk_mattername = name })).RootElement));

    private void UserCannotRead(Guid matter) =>
        _host.UserClient
            .Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith($"{MatterSet}({matter})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied"));

    private static HttpRequestMessage ThreadWriteRequest(string route, Guid threadId) => route switch
    {
        "rename" => Request(HttpMethod.Post, $"/api/communications/threads/{threadId}/rename", new { name = "Renamed" }),
        "pin" => Request(HttpMethod.Patch, $"/api/communications/threads/{threadId}/pin", new { pinned = true }),
        _ => Request(HttpMethod.Delete, $"/api/communications/threads/{threadId}"),
    };

    private void AssertNoAppOnlyDataverseCall(string because) =>
        _host.Entities.Invocations.Should().BeEmpty(because);

    private void AssertNoImpersonatedWrite() =>
        _host.FieldMapping.Invocations.Should().BeEmpty("no record is written as the caller on a deny");

    /// <summary>
    /// Not one Dataverse round trip of any kind: the caller-resolution lookup (an app-only systemuser query), the
    /// identity resolver, rights and privilege probes, impersonated and delegated reads, the document decision's data
    /// source, app-only reads and writes, and the impersonated write.
    /// </summary>
    private void AssertNoDataverseCallAtAll(string because)
    {
        _host.Callers.Invocations.Should().BeEmpty(because + " (not even the caller-resolution lookup)");
        _host.CommunicationData.Invocations.Should().BeEmpty(because);
        _host.Identity.Invocations.Should().BeEmpty(because);
        _host.Probe.Calls.Should().BeEmpty(because);
        _host.Query.Calls.Should().BeEmpty(because);
        _host.UserClient.Invocations.Should().BeEmpty(because);
        _host.Access.DocumentCalls.Should().BeEmpty(because);
        AssertNoAppOnlyDataverseCall(because);
        AssertNoImpersonatedWrite();
    }

    private void AssertNoCreate(string logicalName) =>
        _host.Entities.Verify(
            e => e.CreateAsync(It.Is<DataverseEntity>(x => x.LogicalName == logicalName), It.IsAny<CancellationToken>()),
            Times.Never, $"no {logicalName} may be created on a deny");

    internal static async Task ShouldCarryReasonCode(HttpResponseMessage response, string reasonCode)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("reasonCode").GetString().Should().Be(reasonCode);
    }
}
