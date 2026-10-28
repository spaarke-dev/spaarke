using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Harness;

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

    // =====================================================================================
    // Task 037 (FR-36): the table's copied default — a work assignment's Assigned To from its own Assigned To Internal
    // =====================================================================================

    private const string WaAssignedToBind = "sprk_AssignedTo@odata.bind";

    /// <summary>The work assignment's columns as its metadata declares them, and a matter → work assignment profile that maps
    /// the matter's Assigned To Internal onto the work assignment's (the wizard's mapping, now the server's).</summary>
    private void WorkAssignmentColumnsAndProfile()
    {
        _user.Columns["sprk_workassignment"] = new()
        {
            ["sprk_name"] = ("String", true),
            ["sprk_assignedto"] = ("Lookup", true),
            ["sprk_assignedtointernal"] = ("Lookup", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
        };
        Profile("sprk_matter", "sprk_workassignment", Rule("sprk_assignedtointernal", "sprk_assignedtointernal", Lookup));
    }

    /// <summary>A work assignment filed under the ordinary matter by the caller's bind, asking for field mapping.</summary>
    private RecordCreationRequest MappedWorkAssignment(params (string Key, object? Value)[] extra)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        };
        foreach (var (key, value) in extra)
            body[key] = value;
        return Request("sprk_workassignment", body);
    }

    [Fact]
    public async Task CopiedDefault_AssignedToUnset_TakesTheAssignedToInternalTheMappingFilled_AsIs()
    {
        WorkAssignmentColumnsAndProfile();

        var outcome = await Service().CreateAsync(MappedWorkAssignment(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({Assignee:D})", "mapped from the matter");
        StringOf(fields, WaAssignedToBind).Should().Be($"/contacts({Assignee:D})",
            "FR-36: after mapping, the row's own Assigned To Internal, copied as-is (both are contact lookups)");
        outcome.MappingWarnings.Should().BeEmpty();
        outcome.Steps.Single(s => s.Step == RecordCreationStep.MapPayload).Note.Should().EndWith("; 1 default(s) filled");
    }

    [Fact]
    public async Task CopiedDefault_ASuppliedAssignedTo_IsKept()
    {
        WorkAssignmentColumnsAndProfile();

        var outcome = await Service().CreateAsync(
            MappedWorkAssignment((WaAssignedToBind, $"/contacts({NamedContact:D})")), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, WaAssignedToBind).Should().Be($"/contacts({NamedContact:D})",
            "a supplied value is never replaced");
    }

    /// <summary>UNSET (absent) takes the default; an explicit null — a null bind or a plain null — is the caller's and stays.</summary>
    [Theory]
    [InlineData(WaAssignedToBind)]
    [InlineData("sprk_assignedto")]
    public async Task CopiedDefault_AnExplicitNullAssignedTo_StaysNull(string key)
    {
        WorkAssignmentColumnsAndProfile();

        var outcome = await Service().CreateAsync(MappedWorkAssignment((key, null)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey(WaAssignedToBind);
        fields[WaAssignedToBind].Should().Match<object?>(v => v == null || ((JsonElement)v).ValueKind == JsonValueKind.Null,
            "an explicit null is a value the caller chose: the default does not fill it");
        outcome.Steps.Single(s => s.Step == RecordCreationStep.MapPayload).Note.Should().NotContain("default");
    }

    /// <summary>
    /// Fix round 1 (path parity with the owned create): a copy of a SERVER-set source names the same server-chosen record,
    /// so it is server-set too — it costs no AppendTo its source did not. Without that, a caller with no AppendTo on the
    /// contact the server named would see the copied Assigned To dropped at step 5.
    /// </summary>
    [Fact]
    public async Task CopiedDefault_TheCopyOfAServerSetAssignedToInternal_CostsNoAppendTo_AndIsWritten()
    {
        WorkAssignmentColumnsAndProfile();
        _scripted.NoAppendToIn.Add(("contacts", CallersContact));
        var request = Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_AssignedToInternal@odata.bind"] = $"/contacts({CallersContact:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_assignedtointernal" },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({CallersContact:D})");
        StringOf(fields, WaAssignedToBind).Should().Be($"/contacts({CallersContact:D})",
            "the copy of a server-set source is server-set: it is written, not dropped for an AppendTo its source never cost");
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.DefaultValueDroppedCode);
    }

    /// <summary>
    /// Fix round 2 (must not fire): an ADAPTER's own post-mapping default for Assigned To is the adapter's, not a copy — it is
    /// never made server-set, even when it names the very contact a server-set Assigned To Internal names. It is checked like
    /// any default: a caller with no AppendTo on that contact has it dropped (fails closed on an access-conferring column).
    /// </summary>
    [Fact]
    public async Task CopiedDefault_AnAdaptersOwnAssignedToDefault_IsNeverMadeServerSet_ItIsCheckedAndDropped()
    {
        WorkAssignmentColumnsAndProfile();
        _scripted.NoAppendToIn.Add(("contacts", CallersContact));
        var request = Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_AssignedToInternal@odata.bind"] = $"/contacts({CallersContact:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_assignedtointernal" },
            PostMappingDefaults = MakersContactDefault(),
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({CallersContact:D})");
        fields.Should().NotContainKey(WaAssignedToBind,
            "the adapter's default is checked as the caller's (no AppendTo on the contact), never treated as a server-set copy");
        outcome.MappingWarnings.Should().Contain(w =>
            w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Column == "sprk_assignedto");
    }

    [Fact]
    public async Task CopiedDefault_AssignedToInternalUnsetToo_LeavesAssignedToBlank_AndLogsAStructuredWarning()
    {
        WorkAssignmentColumnsAndProfile();
        _profiles.Clear(); // nothing maps the Assigned To Internal either
        var log = new SeamLog();
        _logger = log;

        var outcome = await Service().CreateAsync(MappedWorkAssignment(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Keys
            .Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase));
        var warning = log.Entries.Should().ContainSingle(e => e.Message.StartsWith("copied_default_source_unset")).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Values["Entity"].Should().Be("sprk_workassignment");
        warning.Values["Column"].Should().Be("sprk_assignedto");
        warning.Values["Source"].Should().Be("sprk_assignedtointernal");
    }

    /// <summary>
    /// UAC-r2 condition 2 on this path: the defaulted value is in the create (step 11), so the "Assigned *" materialisation
    /// (step 12, the REAL materializer over uac-r2's harness) reads it on the created row and decides as for a supplied one:
    /// an ordinary record grants the contact, a Secure one only suggests.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CopiedDefault_TheAssignedToMaterialisation_TreatsTheDefaultedValueExactlyAsASuppliedOne(bool supplied, bool secure)
    {
        WorkAssignmentColumnsAndProfile();
        var harness = new Harness { Participations = new(new RootRecordFlags(IsSecure: secure, IsRestricted: false)) };
        harness.Contact(Assignee);
        _onCreate = (table, id, fields) =>
        {
            // The created row as Dataverse holds it: what the materializer's root read returns.
            if (table != "sprk_workassignment")
                return;
            harness.Store.Root(ExternalGrantRootType.WorkAssignment, id);
            foreach (var key in fields.Keys.Where(k =>
                         k.StartsWith("sprk_Assigned", StringComparison.Ordinal) && k.EndsWith("@odata.bind", StringComparison.Ordinal)))
            {
                if (StringOf(fields, key) is { } path && path.StartsWith("/contacts(", StringComparison.Ordinal))
                {
                    harness.Store.Assign(ExternalGrantRootType.WorkAssignment, id, key[..^"@odata.bind".Length].ToLowerInvariant(),
                        Guid.Parse(path["/contacts(".Length..^1]));
                }
            }
        };
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);
        _scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var outcome = await Service().CreateAsync(
            supplied ? MappedWorkAssignment((WaAssignedToBind, $"/contacts({Assignee:D})")) : MappedWorkAssignment(),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var waId = _creates.Should().ContainSingle().Subject.Id;
        var row = harness.Store.RowsOf(waId, Assignee).Should().ContainSingle(r => r.SourceField == "sprk_assignedto",
            "the materializer evaluated the row's Assigned To — the defaulted value is on the row it reads").Subject;
        if (secure)
        {
            row.State.Should().Be(AssignedAccessState.PendingConfirmation, "Secure: a suggestion only");
            harness.Grants.ActiveRowsOf(waId, Assignee).Should().BeEmpty();
        }
        else
        {
            row.State.Should().Be(AssignedAccessState.Granted);
            harness.Grants.ActiveRowsOf(waId, Assignee).Should().ContainSingle();
        }
    }

    /// <summary>The service's log, with each entry's structured values.</summary>
    private sealed class SeamLog : ILogger<RecordCreationService>
    {
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception),
                (state as IReadOnlyList<KeyValuePair<string, object?>>)?.ToDictionary(kv => kv.Key, kv => kv.Value)
                ?? new Dictionary<string, object?>()));
    }
}
