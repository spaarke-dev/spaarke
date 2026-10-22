using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
    // Fix round (F1-2): a flag or a choice the caller sent as null is supplied — the default does not overwrite it
    // =====================================================================================

    [Theory]
    [InlineData("sprk_priority", "Picklist")]
    [InlineData("sprk_isreminder", "Boolean")]
    public async Task PostMappingDefault_ACallersExplicitNull_OnAChoiceOrFlag_IsKept(string column, string type)
    {
        _user.Columns["sprk_event"][column] = (type, true);
        object value = type == "Boolean" ? true : new OptionSetValue(3);

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: (column, null!)) with
            {
                Mapping = null,
                PostMappingDefaults = new Dictionary<string, object?> { [column] = value },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey(column);
        fields[column].Should().Match<object?>(v => v == null || (v is JsonElement && ((JsonElement)v).ValueKind == JsonValueKind.Null),
            "FR-02: a flag or choice whose key is present is supplied, an explicit null included");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // =====================================================================================
    // Fix round (F4-3): a default the column cannot take is dropped, never a fault
    // =====================================================================================

    [Theory]
    [InlineData("sprk_nosuchcolumn", "a column the table does not have")]
    [InlineData("sprk_eventcode", "a column that takes no value on create")]
    [InlineData("sprk_location", "a value of the wrong kind (a number into text)")]
    public async Task PostMappingDefault_TheColumnCannotTake_IsDroppedWithADefaultWarning_AndTheRowIsCreated(
        string column, string because)
    {
        object value = column == "sprk_location" ? 5 : "x";

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = new Dictionary<string, object?> { [column] = value } },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue($"{because}: {outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey(column);
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.DefaultValueDroppedCode);
        warning.Column.Should().Be(column);
    }

    // =====================================================================================
    // Fix round (K-4): a default that does not map costs only itself
    // =====================================================================================

    [Fact]
    public async Task PostMappingDefault_ThatDoesNotMap_IsDropped_AndTheMappedValuesStand()
    {
        MatterToEventProfile();

        // A lookup onto a table the column does not reference: it fits "Lookup", but the mapper cannot bind it.
        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_regardingproject"] = new EntityReference("contact", CallersContact),
                },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "the step-3 mapped values stand");
        StringOf(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        fields.Should().NotContainKey("sprk_RegardingProject@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Column == "sprk_regardingproject");
    }

    [Fact]
    public async Task PostMappingDefault_WhenTheDefaultsMergeFaults_OnlyTheDefaultsAreDropped_AndTheMappedRowIsCreated()
    {
        MatterToEventProfile();

        // Fault the mapper's metadata reads only once the defaults' own column read has run (step 3½), so the caller's
        // payload (step 1) and the mapping (step 3) map normally and only the defaults' merge faults.
        var defaultsRead = false;
        _user.ThrowOn = path =>
        {
            if (path.Contains("AttributeType", StringComparison.Ordinal) && path.Contains("sprk_regardingproject", StringComparison.Ordinal))
                defaultsRead = true;
            return defaultsRead
                   && (path.Contains("ManyToOneRelationships", StringComparison.Ordinal)
                       || path.Contains("EntitySetName", StringComparison.Ordinal))
                ? new TimeoutException("metadata read timed out at 10.0.0.9")
                : null;
        };

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_regardingproject"] = new EntityReference("sprk_project", OtherProject),
                },
            },
            AsCaller(), CancellationToken.None);

        defaultsRead.Should().BeTrue("the fault was reached at step 3½");
        outcome.Succeeded.Should().BeTrue($"a default only ever costs itself: {outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "the step-3 item and its mapped values stand");
        StringOf(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        fields.Should().NotContainKey("sprk_RegardingProject@odata.bind");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.DefaultValueDroppedCode);
        warning.Column.Should().Be("sprk_regardingproject");
        warning.Detail.Should().NotContain("10.0.0.9", "the fault's text stays in the log");
    }

    private static readonly DataverseUserResponse Busy =
        DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.RateLimited, "busy");

    private static bool IsMapperMetadataRead(string path) =>
        path.Contains("ManyToOneRelationships", StringComparison.Ordinal) || path.Contains("EntitySetName", StringComparison.Ordinal);

    private static bool IsDefaultsColumnRead(string path, string column) =>
        path.Contains("AttributeType", StringComparison.Ordinal) && path.Contains(column, StringComparison.Ordinal);

    [Fact]
    public async Task PostMappingDefault_WhenTheDefaultsMergeIsRefused503_TheMappedValuesStand_AndOnlyTheDefaultIsDropped()
    {
        // Re-check F3: the realistic failure — a non-throwing 429/503 on the mapper's relationship and entity-set reads
        // after the defaults' own column read (step 3½). The step-3 mapped values were mapped before it and must stand.
        MatterToEventProfile();
        var defaultsRead = false;
        _user.FailOn = path =>
        {
            if (IsDefaultsColumnRead(path, "sprk_regardingproject"))
                defaultsRead = true;
            return defaultsRead && IsMapperMetadataRead(path) ? Busy : null;
        };

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_regardingproject"] = new EntityReference("sprk_project", OtherProject),
                },
            },
            AsCaller(), CancellationToken.None);

        defaultsRead.Should().BeTrue("the failure was reached at step 3½");
        outcome.Succeeded.Should().BeTrue($"{outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "a mapped value never pays for a default");
        StringOf(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        fields.Should().NotContainKey("sprk_RegardingProject@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Column == "sprk_regardingproject");
    }

    [Fact]
    public async Task PostMappingDefault_WhenTheMergeIsRefusedThenFaults_EachDefaultIsNamedOnce()
    {
        // Re-check K-2: the whole merge answers 503, the first default alone answers 503 (named), the second faults — each
        // default is still named exactly once.
        var defaultsRead = false;
        var mapperReads = 0;
        _user.ThrowOn = path =>
        {
            if (IsDefaultsColumnRead(path, "sprk_regardingproject"))
                defaultsRead = true;
            if (!defaultsRead || !path.Contains("ManyToOneRelationships", StringComparison.Ordinal))
                return null;
            mapperReads++;
            return mapperReads == 3 ? new TimeoutException("metadata read timed out at 10.0.0.9") : null;
        };
        _user.FailOn = path => defaultsRead && mapperReads < 3 && IsMapperMetadataRead(path) ? Busy : null;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                Mapping = null,
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_assignedto"] = new EntityReference("contact", CallersContact),
                    ["sprk_regardingproject"] = new EntityReference("sprk_project", OtherProject),
                },
            },
            AsCaller(), CancellationToken.None);

        mapperReads.Should().Be(3, "the whole merge, the first default alone, then the fault");
        outcome.Succeeded.Should().BeTrue($"{outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        outcome.MappingWarnings.Select(w => (w.Code, w.Column)).Should().BeEquivalentTo(
            [
                (RecordCreationWarning.DefaultValueDroppedCode, "sprk_assignedto"),
                (RecordCreationWarning.DefaultValueDroppedCode, "sprk_regardingproject"),
            ],
            "one warning per default, never two for the same column");
    }

    [Theory]
    [InlineData(true, "the read throws")]
    [InlineData(false, "the read answers 503")]
    public async Task PostMappingDefault_WhenTheDefaultsColumnMetadataIsUnavailable_EveryDefaultIsDropped_AndTheRowIsCreated(
        bool throws, string because)
    {
        // Re-check K-3: no field mapping, so the only column-metadata read is the defaults' own.
        if (throws)
        {
            _user.ThrowOn = path => path.Contains("AttributeType", StringComparison.Ordinal)
                ? new TimeoutException("metadata read timed out at 10.0.0.9")
                : null;
        }
        else
        {
            _user.FailOn = path => path.Contains("AttributeType", StringComparison.Ordinal) ? Busy : null;
        }

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                Mapping = null,
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_location"] = "Courtroom 1",
                    ["sprk_assignedto"] = new EntityReference("contact", CallersContact),
                },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue($"{because}: {outcome.Refusal?.Code} {outcome.Refusal?.Detail}");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_location").And.NotContainKey("sprk_AssignedTo@odata.bind");
        outcome.MappingWarnings.Should().HaveCount(2).And.OnlyContain(w =>
            w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Detail.EndsWith("it could not be checked."));
        outcome.MappingWarnings.Select(w => w.Column).Should().BeEquivalentTo("sprk_assignedto", "sprk_location");
    }

    // =====================================================================================
    // The adapter's defects are argument errors
    // =====================================================================================

    [Fact]
    public async Task PostMappingDefault_TwoKeysNamingOneColumn_IsAnArgumentError()
    {
        var act = () => Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?> { ["sprk_location"] = "A", ["SPRK_Location"] = "B" },
            },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("Two post-mapping defaults");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMappingDefault_OnATableFieldMappingDoesNotApplyTo_IsAnArgumentError()
    {
        var item = new Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper.MappedItem(
            "{\"sprk_sentence\":\"A sentence\"}", ["sprk_sentence"]);
        var request = new RecordCreationRequest("sprk_signal", new RecordCreationPayload.Mapped(item))
        {
            PostMappingDefaults = new Dictionary<string, object?> { ["sprk_description"] = "x" },
        };

        var act = () => Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("OntologyContentIsNotMapped");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMappingDefault_ANullValue_IsAnArgumentError()
    {
        var act = () => Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = new Dictionary<string, object?> { ["sprk_location"] = null } },
            AsCaller(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

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
