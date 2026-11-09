using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 022 fix round 2 — the mapping-source rule, through the REAL service: values mapped
/// from a secure source are written only into a row owned by that source's secure owner. Step 3 narrows it early (a secure
/// source maps only into a create secure from creation, or one the caller files under that source); the owner check after
/// step 7, before the create, is the invariant and runs on every path. "Secure" is the 032b copy gate's predicate.
/// </summary>
/// <remarks>
/// Create surfaces covered here: the shared service as the child-records route reaches it (<c>RouteCreate</c>, the Project
/// wizard's create) and as the typed routes and Office quick-create reach it (an event filed by the caller's bind with a
/// named or inferred source). Office quick-create and the event/analysis routes call the same <c>CreateAsync</c>; their own
/// suites keep passing with their sources now stating owner and flag.
/// </remarks>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    /// <summary>A seeded answer of the owner step (the forced-mismatch case); <c>null</c> is the real resolver's.</summary>
    private Guid? _forcedOwnerTeam;

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MappingSourceRule_AChildFiledUnderASecureMatter_StillMaps_AndIsOwnedByTheSecureTeam()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(SecureMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "(b): the caller files the row under the secure source");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceSecureCode);
    }

    [Fact]
    public async Task MappingSourceRule_AChildFiledUnderASecureMatter_NamingItAsTheSource_StillMaps()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(SecureMatter, mappingSource: ("sprk_matter", SecureMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
    }

    [Fact]
    public async Task MappingSourceRule_ANonSecureSource_IsUnchanged()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task MappingSourceRule_TheProjectWizardsCreate_FromAnOrdinaryMatter_Maps()
    {
        MatterToProjectProfile();

        var result = await RouteCreate("sprk_project", new() { ["sprk_projectname"] = "Phase 1" }, "sprk_matter", OrdinaryMatter.ToString("D"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MappingSourceRule_ASecureSourceTheRowIsNotFiledUnder_IntoACreateNotSecureFromCreation_MapsNothing()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", SecureMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue("the create still proceeds; only the mapping is skipped");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location").And.NotContainKey("sprk_AssignedTo@odata.bind");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing", "the caller's own values are created");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappingSourceSecureCode);
        warning.Detail.Should().Be(RecordCreationService.MappingSourceSecureNotMapped).And.NotContain(SecureMatter.ToString("D"));
    }

    [Fact]
    public async Task MappingSourceRule_TheProjectWizardsCreate_FromASecureMatter_WithSecureOff_MapsNothing()
    {
        MatterToProjectProfile();

        var result = await RouteCreate("sprk_project", new() { ["sprk_projectname"] = "Phase 1" }, "sprk_matter", SecureMatter.ToString("D"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        WarningsOf(result).Should().Contain(RecordCreationService.MappingSourceSecureNotMapped);
    }

    [Fact]
    public async Task MappingSourceRule_ASourceWhoseOwnerDoesNotComeBack_MapsNothing()
    {
        MatterToEventProfile();
        _sourcesWithNoOwner.Add(OrdinaryMatter);

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceSecureCode);
    }

    // ── The invariant: the owner check before the create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MappingSourceRule_WhenTheOwnerIsNotTheSecureSourcesOwner_EveryMappedValueIsRemoved_AndTheCallersValuesAreWritten()
    {
        MatterToEventProfile();
        _forcedOwnerTeam = Directory.ChildTeam;

        var outcome = await Service().CreateAsync(EventUnder(SecureMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam, "the check never changes the owner");
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location").And.NotContainKey("sprk_AssignedTo@odata.bind");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})", "nor the filing");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappingSourceSecureCode);
        warning.Detail.Should().Be(RecordCreationService.MappingSourceSecureStripped);
    }

    [Fact]
    public async Task MappingSourceRule_WhenTheSecureOwnerAnswerFaults_EveryMappedValueIsRemoved_AndTheCallersValuesAreWritten()
    {
        MatterToEventProfile();
        _shareWorld.FailingQueriesOf("businessunit").FailingQueriesOf("team");

        var outcome = await Service().CreateAsync(EventUnder(SecureMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceSecureCode);
    }

    [Fact]
    public async Task MappingSourceRule_ASecureSourceThatMapsAFilingLookup_ButIsNotTheFilingParent_MapsNothing_AndFilesNothing()
    {
        // A secure project source whose profile maps the event's filing lookup: the row is not filed under the project, so
        // step 3 maps nothing, the lookup included; the row is created filed by the caller's values only.
        ProjectToEventProfile();
        _sources[("sprk_project", SourceProject)][Sprk.Bff.Api.Services.Access.AccessFollowsParent.SecureFlagColumn] = true;

        var outcome = await Service().CreateAsync(
            Event(new { sprk_eventname = "Hearing" }, mappingSource: ("sprk_project", SourceProject)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_RegardingMatter@odata.bind").And.NotContainKey("sprk_description");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceSecureCode);
    }

    /// <summary>The owner step answers <paramref name="team"/> for every create (the mismatch the invariant must catch).</summary>
    private sealed class ForcedOwnerResolver(IRecordOwnershipResolver inner, Guid team) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) =>
            Task.FromResult<Guid?>(team);

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct) =>
            Task.FromResult(RecordOwnerResolution.Owned(team));

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(request, applyChange, ct);
    }
}
