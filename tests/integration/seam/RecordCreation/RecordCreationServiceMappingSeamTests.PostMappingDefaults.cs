using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 018a (task 014 review K-7) — <see cref="RecordCreationRequest.PostMappingDefaults"/>:
/// the last link of an order such as Office's "the contact the request names, else a mapped value, else the maker's contact".
/// A default fills a column only when neither the caller nor a mapping rule supplied it, after step 3 and BEFORE the step-5
/// checks, so a defaulted value is checked like a mapped one: one the caller may not set is dropped, named as a default.
/// Opt-in per request: without it (or with it empty) the create is exactly what it was.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid NamedContact = Guid.Parse("a0140000-0000-4000-8000-0000000000c3");

    private static Dictionary<string, object?> MakersContactDefault() =>
        new() { ["sprk_assignedto"] = new EntityReference("contact", CallersContact) };

    // =====================================================================================
    // The order: named, else mapped, else the default
    // =====================================================================================

    [Fact]
    public async Task PostMappingDefault_TheContactTheRequestNames_Wins()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, assignedTo: NamedContact) with { PostMappingDefaults = MakersContactDefault() },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind")
            .Should().Be($"/contacts({NamedContact:D})", "the caller's own value is never overwritten");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMappingDefault_AMappedValue_Wins_OverTheDefault()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = MakersContactDefault() }, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind")
            .Should().Be($"/contacts({Assignee:D})", "the mapped value fills first; the default only fills what is still unset");
        outcome.Steps.Single(s => s.Step == RecordCreationStep.MapPayload).Note.Should().NotContain("default");
    }

    [Theory]
    [InlineData(true, "field mapping asked for, the profile maps nothing")]
    [InlineData(false, "no field mapping asked for")]
    public async Task PostMappingDefault_NeitherNamedNorMapped_TheDefaultIsWritten(bool mapping, string because)
    {
        // No profile: the matter → event mapping supplies nothing.
        var request = EventUnder(OrdinaryMatter) with { PostMappingDefaults = MakersContactDefault() };
        if (!mapping)
            request = request with { Mapping = null };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind")
            .Should().Be($"/contacts({CallersContact:D})", because);
        outcome.MappingWarnings.Should().BeEmpty();
        outcome.Steps.Single(s => s.Step == RecordCreationStep.MapPayload).Note.Should().EndWith("; 1 default(s) filled");
    }

    // =====================================================================================
    // A default is checked (step 5): dropped, named as a default — or, on a filing lookup, refused
    // =====================================================================================

    [Fact]
    public async Task PostMappingDefault_ACallerMayNotAppendTo_IsDroppedWithADefaultWarning_AndTheRowIsCreated()
    {
        _scripted.NoAppendToIn.Add(("contacts", CallersContact));

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = MakersContactDefault() }, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey("sprk_AssignedTo@odata.bind",
            "a defaulted value is checked like a mapped one, never written past a check it failed");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.DefaultValueDroppedCode);
        warning.Column.Should().Be("sprk_assignedto");
        warning.Detail.Should().StartWith("The default value for 'sprk_assignedto' was not written: ");
        warning.Detail.Should().NotContain(CallersContact.ToString("D"), "ADR-019: nothing of the value");
    }

    [Fact]
    public async Task PostMappingDefault_AFilingLookupTheCallerMayNotFileUnder_Refuses_AndNothingIsCreated()
    {
        _scripted.NoAppendToIn.Add(("sprk_projects", OtherProject));

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_regardingproject"] = new EntityReference("sprk_project", OtherProject),
                },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse("a filing lookup the caller may not file under refuses, as a mapped one does");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode);
        outcome.Steps.Last().Step.Should().Be(RecordCreationStep.CallerChecks, "refused by the step-5 checks");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // The adapter's defects are argument errors
    // =====================================================================================

    [Theory]
    [InlineData("ownerid")]
    [InlineData("sprk_regardingrecordid")]
    [InlineData("not a column")]
    public async Task PostMappingDefault_AColumnMappingNeverWrites_IsAnArgumentError(string column)
    {
        var act = () => Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = new Dictionary<string, object?> { [column] = "x" } },
            AsCaller(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMappingDefault_ADate_IsAnArgumentError()
    {
        var act = () => Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?> { ["sprk_duedate"] = new DateTime(2027, 1, 1) },
            },
            AsCaller(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PostMappingDefault_TheSystemPrincipal_IsAnArgumentError()
    {
        // A server-built item, so nothing else about the request is a system principal's argument error.
        var item = new Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper.MappedItem(
            "{\"sprk_eventname\":\"Hearing\"}", ["sprk_eventname"]);
        var request = new RecordCreationRequest("sprk_event", new RecordCreationPayload.Mapped(item));
        var withDefaults = request with { PostMappingDefaults = MakersContactDefault() };

        var act = () => Service().CreateAsync(withDefaults, RecordCreationPrincipal.ForSystem(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("post-mapping defaults");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // Not opted in: the create is exactly what it was
    // =====================================================================================

    [Fact]
    public async Task WithoutEitherOption_OrWithBothEmpty_TheCreateIsUnchanged()
    {
        MatterToEventProfile();
        _scripted.NoAppendToIn.Add(("contacts", Assignee)); // a mapped value dropped, so the warnings are not trivially empty

        var none = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);
        var empty = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                ReferenceLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PostMappingDefaults = new Dictionary<string, object?>(),
            },
            AsCaller(), CancellationToken.None);

        none.Succeeded.Should().BeTrue(none.Refusal?.Detail);
        _creates.Should().HaveCount(2);
        JsonSerializer.Serialize(_creates[1].Fields).Should().Be(JsonSerializer.Serialize(_creates[0].Fields));
        empty.MappingWarnings.Should().Equal(none.MappingWarnings);
        none.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
        empty.Steps.Should().Equal(none.Steps);

        // The step notes as they read before task 018a.
        none.Steps.Single(s => s.Step == RecordCreationStep.TableRules).Note
            .Should().Be("no server-owned column in the caller's payload");
        none.Steps.Single(s => s.Step == RecordCreationStep.MapPayload).Note
            .Should().MatchRegex(@"^\d+ column\(s\) \(\d+ mapped\), \d+ lookup\(s\)$");
    }
}
