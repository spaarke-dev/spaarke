using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 017, Phase 2d-1 (owner decision 2a, FR-06 step 10): the I-1 stamp is derived into
/// the create payload BEFORE the create, a derivation that fails refuses it with nothing written, I-16 is set when it resolves
/// and never refuses, a document gets neither before the create (#2169), and the post-create re-derivation still runs. Over
/// the pipeline seam's REAL service, REAL <see cref="CoreAncestorRestamper"/> (task 016's planner) and REAL
/// <see cref="CoreAncestorResolver"/> (I-1 and I-16) over the stamp world.
/// </summary>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    /// <summary>An event filed under the ordinary matter: the intermediate a new event is filed under.</summary>
    private static readonly Guid ParentEvent = Guid.Parse("a0130000-0000-4000-8000-0000000000e1");

    private const string MatterStampBind = "sprk_RegardingMatter@odata.bind";

    /// <summary>The parent event as the owner resolver, the stamp world and the share world know it.</summary>
    private void WithParentEventUnderTheOrdinaryMatter()
    {
        _world.WithRecord("sprk_event", ParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_event", ParentEvent, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);
        _shareWorld.OrdinaryChild("sprk_event", ParentEvent, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    private RecordCreationRequest EventUnderTheParentEvent() =>
        WebApiRequest("sprk_event", new { sprk_eventname = "Follow-up", sprk_RegardingEvent_bind = $"/sprk_events({ParentEvent:D})" });

    // =====================================================================================
    // 1. The I-1 stamp is in the create payload, and the post-create re-derivation still runs
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnEventFiledUnderAnEvent_CarriesItsMattersStampInTheCreatePayload_AndIsReDerivedAfterTheCreate()
    {
        WithParentEventUnderTheOrdinaryMatter();

        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var (_, id, fields) = _creates.Should().ContainSingle().Subject;
        Bind(fields, MatterStampBind).Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "the parent event's matter is derived into the payload before the create (owner decision 2a)");
        Bind(fields, "sprk_RegardingEvent@odata.bind").Should().Be($"/sprk_events({ParentEvent:D})", "the caller's own filing stands");
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).State
            .Should().Be(RecordCreationStepState.Ran);

        // The post-create re-derivation (step 12) reads the created row, after the create, as before.
        _restampReads.Should().Contain(("sprk_event", id), "AfterWriteAsync still re-derives the stamp after the create");
        Journal().Should().ContainInOrder("create", "restamp", "mirror");
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentUnderAMatter_ARootCarriesNoStamp_ItsMatterIsItsOwnFiling_AndNothingIsDerived()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_workassignment", new { sprk_name = "Review", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        Bind(fields, MatterStampBind).Should().Be($"/sprk_matters({OrdinaryMatter:D})", "the caller's filing, unchanged");
        fields.Keys.Where(k => k.StartsWith("sprk_Regarding", StringComparison.Ordinal)).Should().Equal(MatterStampBind);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance)
            .Should().Match<RecordCreationStepRecord>(s => s.State == RecordCreationStepState.Skipped,
                "a work assignment is a core record: it carries no I-1 stamp columns and no inherited Access Permission");
    }

    // =====================================================================================
    // 2. An I-1 derivation that fails refuses the create: nothing written
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheParentsRootCannotBeDetermined_Is400StampUnderivable_AndNothingIsCreated()
    {
        // The parent the caller may file under (the owner resolver and the caller's AppendTo both see it) is gone by the time
        // its root is read: a data condition a retry does not change (CoreAncestorRefusalKind.NotFound, IsPermanent).
        _world.WithRecord("sprk_event", ParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.StampUnderivableCode);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Extensions["errorCode"].Should().Be("record_creation.stamp_underivable");
        problem.Extensions["reasonCode"].Should().Be("record_creation.stamp_underivable");
        outcome.Steps.Last().Should().Be(new RecordCreationStepRecord(
            RecordCreationStep.StampAndInheritance, RecordCreationStepState.Refused, RecordCreationRefusal.StampUnderivableCode));
        _createAttempts.Should().BeEmpty("nothing is written when the stamp cannot be derived");
        Journal().Should().NotContain("create");
    }

    [Fact]
    public async Task CreateAsync_WhenTheParentsRootCannotBeReadRightNow_Is503StampUnavailable_AndNothingIsCreated()
    {
        WithParentEventUnderTheOrdinaryMatter();
        _stamps.FailRead("sprk_event", ParentEvent); // a timeout: a fault a retry may clear

        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.StampUnavailableCode);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Extensions["errorCode"].Should().Be("record_creation.stamp_unavailable");
        problem.Detail.Should().NotContain("timed out", "a fault's text stays in the log (ADR-019)");
        _createAttempts.Should().BeEmpty();
    }

    /// <summary>The table's relationships read for the stamp bind: after the owner (step 7), so not the payload's own mapping read.</summary>
    private bool IsTheStampBindsMetadataRead(string path) =>
        path.Contains("ManyToOneRelationships", StringComparison.Ordinal) && _journal.Contains("owner");

    [Fact]
    public async Task CreateAsync_WhenTheTablesMetadataCannotBindTheDerivedStamp_IsTheNonRetryable500Undecided_NotTryAgain()
    {
        // Task 017 review F-5: the stamp WAS derived, but the table's relationships do not carry the column (the mapper's
        // validation error): a misconfiguration a retry does not clear. Not "stamp_unavailable, try again" (503).
        WithParentEventUnderTheOrdinaryMatter();
        _user.RespondTo = path => IsTheStampBindsMetadataRead(path)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { ManyToOneRelationships = Array.Empty<object>() }))
            : null;

        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Undecided);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NoDecisionCode, "the existing non-retryable code, no new one");
        outcome.Refusal.Code.Should().NotBe(RecordCreationRefusal.StampUnavailableCode);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status500InternalServerError);
        outcome.Steps.Last().Should().Be(new RecordCreationStepRecord(
            RecordCreationStep.StampAndInheritance, RecordCreationStepState.Refused, RecordCreationRefusal.NoDecisionCode));
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("permanent", StringComparison.Ordinal));
        _createAttempts.Should().BeEmpty("nothing is written");
    }

    [Fact]
    public async Task EventRoute_WhenTheTablesMetadataCannotBindTheDerivedStamp_KeepsTheRoutes500RegardingStampFailed()
    {
        // Task 031 fix round 1 (F1-low): the same real refusal as above, rendered by POST /api/v1/events' adapter. Before task
        // 031 that route answered every stamp it could not make with 500 events.regarding_stamp_failed (NFR-07): the service's
        // generic Undecided must not lose that code. Told apart by the step that refused it, never by the code alone.
        WithParentEventUnderTheOrdinaryMatter();
        _user.RespondTo = path => IsTheStampBindsMetadataRead(path)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { ManyToOneRelationships = Array.Empty<object>() }))
            : null;
        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);
        var http = new DefaultHttpContext { TraceIdentifier = "trace-stamp" };

        var result = Sprk.Bff.Api.Api.Events.EventEndpoints.RefusalResult(outcome.Refusal!, outcome.Steps, http);

        var problem = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode")
            .WhoseValue.Should().Be(Sprk.Bff.Api.Api.Events.EventEndpoints.RegardingStampFailedErrorCode);
        problem.ProblemDetails.Detail.Should().Be(
            "The event's regarding record could not be resolved for access inheritance, so nothing was written.");
        _createAttempts.Should().BeEmpty("nothing is written");
    }

    [Theory]
    [InlineData(nameof(RecordCreationStep.CallerChecks))]
    [InlineData(nameof(RecordCreationStep.Owner))]
    public void EventRoute_TheGenericNoDecisionRefusalAtAnyOtherStep_StaysTheRoutesGeneric500WithoutAnErrorCode(string stepName)
    {
        var step = Enum.Parse<RecordCreationStep>(stepName);
        // Must not fire: the same code from any other step is not a stamp failure, so it keeps the route's generic 500.
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, "The event was not created.");

        var result = Sprk.Bff.Api.Api.Events.EventEndpoints.RefusalResult(
            refusal, [new RecordCreationStepRecord(step, RecordCreationStepState.Refused, RecordCreationRefusal.NoDecisionCode)],
            new DefaultHttpContext());

        var problem = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Detail.Should().Be("An error occurred while creating the event");
        problem.ProblemDetails.Extensions.Should().NotContainKey("errorCode");
    }

    [Fact]
    public async Task CreateAsync_WhenDataverseDoesNotAnswerTheStampBindsMetadataRead_StaysThe503StampUnavailable()
    {
        // The other branch, unchanged: the metadata read itself failed at Dataverse, which a retry may clear.
        WithParentEventUnderTheOrdinaryMatter();
        _user.RespondTo = path => IsTheStampBindsMetadataRead(path)
            ? DataverseUserResponse.Fail(503, "Unavailable", "0x80072321 The service is busy.")
            : null;

        var outcome = await Service().CreateAsync(EventUnderTheParentEvent(), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.StampUnavailableCode);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _createAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_AnUnderivableStampIsRefusedTheSameWay_AndNothingIsCreated()
    {
        _world.WithRecord("sprk_event", ParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var request = await MappedRequest("sprk_event", new { sprk_eventname = "Follow-up", sprk_regardingevent = Lookup("sprk_event", ParentEvent) });

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.StampUnderivableCode, "FR-07: step 10 applies to a system principal");
        _createAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_AnEventFiledUnderAnEvent_IsStampedByThePostCreateReDerivation()
    {
        WithParentEventUnderTheOrdinaryMatter();
        var request = await MappedRequest("sprk_event", new { sprk_eventname = "Follow-up", sprk_regardingevent = Lookup("sprk_event", ParentEvent) });

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var (_, id, fields) = _creates.Should().ContainSingle().Subject;
        fields.Should().NotContainKey(MatterStampBind,
            "a system principal has no caller to read the table's navigation metadata as; its derived stamp is bound after");
        _stamps.Lookup("sprk_event", id, "sprk_regardingmatter").Should().Be(OrdinaryMatter,
            "the post-create re-derivation (step 12) writes it");
    }

    /// <summary>
    /// Task 042b (FR-41, #2483 / DI-009 closed) INVERTS task 042a's A3 (design.md section 15.6), formerly
    /// <c>CreateAsync_AsTheSystem_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window</c>, the
    /// system-principal twin of the route's window. Step 6½ derives the stamp for every principal before the owner, so the
    /// event is owned by <see cref="Directory.SecureNamedTeam"/> and the Secure matter's sharee mirror is written at create;
    /// a system principal still carries no stamp in the create body and binds it after the create (DI-007).
    /// </summary>
    [Fact]
    public async Task CreateAsync_AsTheSystem_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate()
    {
        _world.WithRecord("sprk_event", ParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter) });
        _stamps.Row("sprk_event", ParentEvent, [("sprk_regardingmatter", "sprk_matter", SecureMatter)]);
        _shareWorld.OrdinaryChild("sprk_event", ParentEvent, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        _shareTable.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(SecureMatterSharee), CollaborateMask);
        var request = await MappedRequest("sprk_event", new { sprk_eventname = "Follow-up", sprk_regardingevent = Lookup("sprk_event", ParentEvent) });

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var (_, id, fields) = _creates.Should().ContainSingle().Subject;
        Owner(fields).Should().Be(Directory.SecureNamedTeam,
            "#2483 closed: step 7 asks over the event and the root step 6½ derived, for a system principal too");
        fields.Should().NotContainKey(MatterStampBind, "a system principal's derived stamp is bound after the create, not in the body");
        _stamps.Lookup("sprk_event", id, "sprk_regardingmatter").Should().Be(SecureMatter,
            "the post-create re-derivation (step 12) binds the Secure matter");
        _shareTable.MaskOf("sprk_event", id, DataversePrincipalRef.User(SecureMatterSharee)).Should().Be(CollaborateMask,
            "the Secure matter's sharee mirror is written at create");
    }

    /// <summary>A user the Secure matter is shared with (task 042b's A3).</summary>
    private static readonly Guid SecureMatterSharee = Guid.Parse("a0130000-0000-4000-8000-0000000004b1");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    // =====================================================================================
    // 3. I-16: set before the create when it resolves; a null never refuses
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnEventUnderARestrictedMatter_CarriesItsInheritedAccessPermissionInTheCreatePayload()
    {
        _stamps.Set("sprk_matter", OrdinaryMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().ContainKey(InheritedAccessPermission.Column,
                outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).Note)
            .WhoseValue.Should().Be(InheritedAccessPermission.Restricted, "I-16 is set in the payload before the create");
    }

    [Fact]
    public async Task CreateAsync_WhenI16CannotBeResolved_TheCreateProceedsWithoutIt()
    {
        // The matter's Access Permission cannot be read: the resolver logs and answers null (display only, never fails a write).
        _stamps.FailScan("sprk_matter");

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey(InheritedAccessPermission.Column);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).State.Should().Be(RecordCreationStepState.Ran);
    }

    // =====================================================================================
    // 4. A document gets no pre-create stamp (#2169); its Access Permission is decided in the create (task 022b)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ADocumentUnderARestrictedMatter_GetsNoPreCreateStamp_ButIsCreatedRestrictedWithItsRecord()
    {
        _stamps.Set("sprk_matter", OrdinaryMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_document", new { sprk_documentname = "Brief.docx", sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey(InheritedAccessPermission.Column).WhoseValue.Should().Be(InheritedAccessPermission.Restricted,
            "task 022b: a document's Access Permission is decided in the create by the document decision");
        fields.Should().ContainKey(AccessInheritance.Column, "its record is written in the same create");
        fields.Should().NotContainKey(MatterStampBind, "a document's I-1 stamp is post-create only (#2169)");
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).State.Should().Be(RecordCreationStepState.Ran);
    }

    /// <summary>A bind as the create body holds it: the caller's own as a JSON string, a server-added one as a string.</summary>
    private static string? Bind(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value)
            ? value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string
            : null;
}
