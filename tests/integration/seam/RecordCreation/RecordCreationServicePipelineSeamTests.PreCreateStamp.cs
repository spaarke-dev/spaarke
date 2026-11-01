using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
    // 4. A document gets no pre-create stamp (#2169)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ADocumentUnderARestrictedMatter_GetsNoPreCreateStampOrAccessPermission()
    {
        _stamps.Set("sprk_matter", OrdinaryMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_document", new { sprk_documentname = "Brief.docx", sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(InheritedAccessPermission.Column, "a document's Access Permission is post-create only (#2169)");
        fields.Should().NotContainKey(MatterStampBind);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).State.Should().Be(RecordCreationStepState.Skipped);
    }

    /// <summary>A bind as the create body holds it: the caller's own as a JSON string, a server-added one as a string.</summary>
    private static string? Bind(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value)
            ? value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string
            : null;
}
