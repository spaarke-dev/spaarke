using FluentAssertions;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 018a (UAC-r2's review of 013, K-3) — <see cref="RecordCreationRequest.ReferenceLookupColumns"/>:
/// an optional reference the caller chose (a matter type, a practice area) that the caller may not use is DROPPED with a
/// warning naming the column and the create proceeds, where a lookup of the caller's own is refused. Opt-in per request:
/// without it, and for every lookup it does not name, a filing lookup included, the refusal is unchanged.
/// </summary>
/// <remarks>
/// The REAL <see cref="RecordCreationService"/>, the REAL caller checks over the caller's scripted Dataverse (the reference
/// tables are organization-owned, so using a row costs the table's AppendTo PRIVILEGE), and the REAL owner resolver — the
/// world of <see cref="CreateWorld"/>.
/// </remarks>
[Trait("status", "new")]
public sealed class RecordCreationServiceReferenceLookupSeamTests
{
    private static readonly Guid MatterTypeRow = Guid.Parse("a018a000-0000-4000-8000-000000000001");
    private static readonly Guid PracticeAreaRow = Guid.Parse("a018a000-0000-4000-8000-000000000002");
    private static readonly Guid AssigneeContact = Guid.Parse("a018a000-0000-4000-8000-000000000003");
    private const string MatterTypeBind = "sprk_MatterType@odata.bind";
    private const string PracticeAreaBind = "sprk_PracticeArea@odata.bind";

    private static readonly HashSet<string> References =
        new(StringComparer.OrdinalIgnoreCase) { "sprk_mattertype", "sprk_practicearea" };

    private readonly CreateWorld _world = new();

    private Task<RecordCreationRequest> MatterAsync(bool withAssignee = false) => _world.MappedAsync("sprk_matter",
        withAssignee
            ? new
            {
                sprk_mattername = "Acme v. Widget",
                sprk_mattertype = CreateWorld.Lookup("sprk_mattertype_ref", MatterTypeRow),
                sprk_practicearea = CreateWorld.Lookup("sprk_practicearea_ref", PracticeAreaRow),
                sprk_assignedtointernal = CreateWorld.Lookup("contact", AssigneeContact),
            }
            : (object)new
            {
                sprk_mattername = "Acme v. Widget",
                sprk_mattertype = CreateWorld.Lookup("sprk_mattertype_ref", MatterTypeRow),
                sprk_practicearea = CreateWorld.Lookup("sprk_practicearea_ref", PracticeAreaRow),
            });

    // =====================================================================================
    // Must fire: an unusable reference is dropped with a warning, the create proceeds
    // =====================================================================================

    [Fact]
    public async Task AReferenceTheCallerMayNotUse_IsDroppedWithAWarning_AndTheMatterIsCreatedWithoutIt()
    {
        _world.Caller.Held.Remove("prvAppendTosprk_mattertype_ref");
        var request = await MatterAsync() with { ReferenceLookupColumns = References };

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(MatterTypeBind, "the reference the caller may not use is not written");
        fields.Should().ContainKey(PracticeAreaBind, "a usable reference is kept");
        fields["sprk_mattername"]!.ToString().Should().Contain("Acme v. Widget");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.ReferenceValueDroppedCode);
        warning.Column.Should().Be("sprk_mattertype");
        warning.Detail.Should().Be("The value you chose for 'sprk_mattertype' was not written: it was not found, or you do not " +
            "have permission to use it; the matter was created without it.");
        warning.Detail.Should().NotContain(MatterTypeRow.ToString("D"), "ADR-019: nothing of the value");
        outcome.Warnings.Should().Contain(warning.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.TableRules).Note.Should().EndWith("1 reference value(s) dropped");
    }

    [Theory]
    [InlineData(true, "the AppendTo read throws")]
    [InlineData(false, "the AppendTo read answers 503")]
    public async Task AReferenceWhoseCheckCannotComplete_IsDroppedWithAWarning_AndTheMatterIsCreated(bool throws, string because)
    {
        // UAC-r2's 018a acceptance (K-a): "the check could not complete" drops the reference (K-5, the accepted deviation
        // matching live Office), never keeps it.
        if (throws)
            _world.Caller.PrivilegesThrowFor = "prvAppendTosprk_mattertype_ref";
        else
            _world.Caller.PrivilegesFault = ("prvAppendTosprk_mattertype_ref", 503);
        var request = await MatterAsync() with { ReferenceLookupColumns = References };

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue($"{because}: {outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(MatterTypeBind, "a reference that could not be checked is never written");
        fields.Should().ContainKey(PracticeAreaBind, "the other reference's check completed");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.ReferenceValueDroppedCode);
        warning.Column.Should().Be("sprk_mattertype");
        // A throw is the reference pass's own catch ("it could not be checked"). A 503 on an ORGANIZATION-OWNED table's
        // AppendTo read is collapsed by CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync to "no AppendTo"
        // (pre-existing, uniform), so it reads as the not-usable wording — dropped either way.
        warning.Detail.Should().Be(throws
            ? "The value you chose for 'sprk_mattertype' was not written: it could not be checked; the matter was created without it."
            : "The value you chose for 'sprk_mattertype' was not written: it was not found, or you do not have permission to use " +
              "it; the matter was created without it.");
        warning.Detail.Should().NotContain("10.0.0.11", "the fault's text stays in the log");
    }

    [Fact]
    public async Task AUsableReference_IsKept_AndNothingIsSaid()
    {
        var request = await MatterAsync() with { ReferenceLookupColumns = References };

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey(MatterTypeBind).And.ContainKey(PracticeAreaBind);
        outcome.MappingWarnings.Should().BeEmpty();
        outcome.Steps.Single(s => s.Step == RecordCreationStep.TableRules).Note
            .Should().Be("no server-owned column in the caller's payload", "the step's note is unchanged when nothing is dropped");
    }

    // =====================================================================================
    // Must not fire: the caller's own values keep their refusal
    // =====================================================================================

    [Fact]
    public async Task WithoutTheOption_AReferenceTheCallerMayNotUse_IsStillRefused_AndNothingIsCreated()
    {
        _world.Caller.Held.Remove("prvAppendTosprk_mattertype_ref");

        var outcome = await _world.CreateAsync(await MatterAsync());

        outcome.Succeeded.Should().BeFalse("no request option, no change: every lookup is the caller's own");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode, "the uniform AppendTo refusal");
        outcome.MappingWarnings.Should().BeEmpty();
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task WithTheOption_ALookupItDoesNotName_TheCallerMayNotAppendTo_IsStillRefused_AndNothingIsCreated()
    {
        // The Assigned To contact stays OUT of the reference path (coordinator 2026-10-21; UAC-r2 condition (b) item 1).
        _world.Caller.NoAppendToIn.Add(("contacts", AssigneeContact));
        var request = await MatterAsync(withAssignee: true) with { ReferenceLookupColumns = References };

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeFalse("an ordinary caller value the caller cannot AppendTo is refused, as before");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode, "the uniform AppendTo refusal");
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task AFilingLookupNamedAsAReference_IsAnArgumentError_NeverDropped()
    {
        _world.Caller.NoAppendTo.Add(CreateWorld.OrdinaryMatter);
        var request = await _world.MappedAsync("sprk_event", new
        {
            sprk_eventname = "Hearing",
            sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter),
        }) with
        {
            ReferenceLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_regardingmatter" },
        };

        var act = () => _world.CreateAsync(request);

        (await act.Should().ThrowAsync<ArgumentException>("a filing lookup is the record the row is filed under, never an optional reference"))
            .Which.Message.Should().Contain("not an optional reference");
        _world.Creates.Should().BeEmpty();
    }

    // =====================================================================================
    // Fix round (F4-1): only the registry's references may be named
    // =====================================================================================

    [Fact]
    public async Task AnEventsAssignedTo_NamedAsAReference_IsAnArgumentError_NeverCreatedWithTheMakersContact()
    {
        // The reviewer's probe: an unattachable contact named, marked a reference, with the maker's contact as the default.
        // Dropping it would let the default fill the column; UAC-r2 condition (b) item 1 requires the refusal instead.
        _world.Caller.NoAppendToIn.Add(("contacts", AssigneeContact));
        var request = await _world.MappedAsync("sprk_event", new
        {
            sprk_eventname = "Hearing",
            sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter),
            sprk_assignedto = CreateWorld.Lookup("contact", AssigneeContact),
        }) with
        {
            ReferenceLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_assignedto" },
            PostMappingDefaults = new Dictionary<string, object?>
            {
                ["sprk_assignedto"] = new Microsoft.Xrm.Sdk.EntityReference("contact", Guid.NewGuid()),
            },
        };

        var act = () => _world.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        _world.Creates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_assignedtointernal", "the for-person column")]
    [InlineData("sprk_project", "sprk_assignedtointernal", "the for-person column")]
    [InlineData("sprk_project", "sprk_regardingrecordtype", "the regarding pair's type")]
    [InlineData("sprk_matter", "sprk_practicearea_typo", "a column the registry does not declare")]
    [InlineData("sprk_project", "sprk_mattertype", "another table's reference")]
    public async Task ALookupTheTableDoesNotDeclareAsAReference_IsAnArgumentError(string table, string column, string because)
    {
        var request = CreateWorld.Mapped(table, new Dictionary<string, object?> { ["sprk_name"] = "x" }) with
        {
            ReferenceLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { column },
        };

        var act = () => _world.CreateAsync(request);

        (await act.Should().ThrowAsync<ArgumentException>(because)).Which.Message.Should().Contain(column);
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSystemPrincipal_WithReferences_IsAnArgumentError()
    {
        var request = CreateWorld.Mapped("sprk_matter", new Dictionary<string, object?> { ["sprk_mattername"] = "Acme" }) with
        {
            ReferenceLookupColumns = References,
        };

        var act = () => _world.CreateAsync(request, system: true);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("References");
        _world.Creates.Should().BeEmpty();
    }
}
