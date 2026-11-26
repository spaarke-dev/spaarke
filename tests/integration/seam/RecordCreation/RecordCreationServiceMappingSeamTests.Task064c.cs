using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 064c (the task 064 review): which mapped values step 5 writes without the caller's
/// checks. A value is copied unchecked only when its source is the record the caller's row is filed under, by table AND id;
/// the Assigned-To class is the prefix family united with the access-conferring registries; an adapter default and a filing
/// lookup keep the caller's checks; a field-secured source column (or one whose security cannot be told) is never copied; a
/// target column whose security is not stated counts as secured.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private const string SourceSecurityPath = "EntityDefinitions(LogicalName='sprk_matter')/Attributes?$select=LogicalName,IsSecured";

    /// <summary>An event filed by the caller's own typed regarding pair under <paramref name="pairId"/> (a matter).</summary>
    private RecordCreationRequest EventByPair(Guid pairId, (string Table, Guid Id)? mappingSource = null) =>
        Request("sprk_event", new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "Hearing",
            [SecureRootInheritance.PairIdColumn] = pairId.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
        }, mapping: true, mappingSource);

    private static DataverseUserResponse Answer(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    // ── (a) a filing lookup mapped from the filing parent keeps the caller's AppendTo: refused ──

    [Fact]
    public async Task Task064c_AFilingLookupMappedFromTheFilingParent_TheCallerCannotAppendTo_IsRefused()
    {
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_primaryproject", "sprk_regardingproject", Lookup),
            Rule("sprk_matterdescription", "sprk_description", Memo));
        _scripted.NoAppendToIn.Add(("sprk_projects", OtherProject));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "a mapped filing lookup is the caller's own, whatever its source: refused, never copied unchecked");
        _creates.Should().BeEmpty();
    }

    // ── (b) a source resolved from the caller's regarding pair is the filing parent ──

    [Fact]
    public async Task Task064c_AnOrdinaryValueFromTheRegardingPairSource_TheCallerCannotAppendTo_IsWritten()
    {
        TheRecordTypes();
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(EventByPair(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_EventType_Ref@odata.bind")
            .Should().Be($"/sprk_eventtype_refs({MappedEventType:D})");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // ── (c) an explicit source equal to the filing parent (Office: by the pair; child route: by a lookup) ──

    [Theory]
    [InlineData(true)]  // the Office create's shape: filed by the pair, the source named explicitly
    [InlineData(false)] // the child-record route's shape: filed by a lookup, the source named explicitly
    public async Task Task064c_AnExplicitSourceThatIsTheFilingParent_ByTableAndId_IsWritten(bool filedByPair)
    {
        TheRecordTypes();
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));
        var request = filedByPair
            ? EventByPair(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter))
            : EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter));

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_EventType_Ref@odata.bind")
            .Should().Be($"/sprk_eventtype_refs({MappedEventType:D})");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // ── (d) item 1: an explicit source with the pair's id but another table is not the filing parent ──

    [Fact]
    public async Task Task064c_AnExplicitSourceWithThePairsIdButAnotherTable_KeepsTheCallersChecks()
    {
        // The caller files under the matter by the pair; their explicit source is a project that happens to carry the same id
        // (a client-chosen GUID), which they can read but not AppendTo. It is not the record the row is filed under.
        TheRecordTypes();
        _sources[("sprk_project", OrdinaryMatter)] = new Entity("sprk_project", OrdinaryMatter)
        {
            ["sprk_defaulteventtype"] = new EntityReference("sprk_eventtype_ref", MappedEventType),
        };
        Profile("sprk_project", "sprk_event", Rule("sprk_defaulteventtype", "sprk_eventtype_ref", Lookup));
        _user.Columns["sprk_event"]["sprk_eventtype_ref"] = ("Lookup", true);
        _scripted.NoAppendToIn.Add(("sprk_projects", OrdinaryMatter));
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(
            EventByPair(OrdinaryMatter, mappingSource: ("sprk_project", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey("sprk_EventType_Ref@odata.bind",
            "an explicit mapping source is the filing parent only when its table and id both match");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_eventtype_ref");
    }

    // ── (e) an ordinary adapter default the caller cannot AppendTo is dropped as a default ──

    [Fact]
    public async Task Task064c_AnOrdinaryAdapterDefaultTheCallerCannotAppendTo_IsDroppedAsADefault()
    {
        // Neither Assigned-To nor filing: only the default's own class keeps the caller's checks here.
        Profile("sprk_matter", "sprk_event", Rule("sprk_matterdescription", "sprk_description", Memo));
        _user.Columns["sprk_event"]["sprk_eventtype_ref"] = ("Lookup", true);
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                PostMappingDefaults = new Dictionary<string, object?> { ["sprk_eventtype_ref"] = new EntityReference("sprk_eventtype_ref", MappedEventType) },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_EventType_Ref@odata.bind", "an adapter's default keeps the caller's checks");
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Column == "sprk_eventtype_ref");
    }

    // ── (f) a Template rule with a field-secured placeholder is not applied ──

    [Fact]
    public async Task Task064c_ATemplateRuleWithAFieldSecuredPlaceholder_IsDropped()
    {
        var template = Rule(null, "sprk_description", Memo, CreateTimeFieldMapping.MappingTypeTemplate);
        template.Expression = "{sprk_matterdescription} in {sprk_location}";
        Profile("sprk_matter", "sprk_event", template);
        _scripted.SecuredColumns.Add("sprk_location");

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey("sprk_description");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
        warning.Column.Should().Be("sprk_description");
        warning.Detail.Should().Contain(RecordCreationService.SourceColumnFieldSecured).And.NotContain("Courtroom 4");
    }

    // ── (g) a source column whose security is not stated counts as secured ──

    [Theory]
    [InlineData(true)]  // the column is answered without IsSecured
    [InlineData(false)] // the column is not answered at all
    public async Task Task064c_ASourceColumnWhoseSecurityIsNotStated_IsTreatedAsSecured(bool answeredWithoutIsSecured)
    {
        MatterEventTypeToEventProfile();
        _user.FailOn = path => path.StartsWith(SourceSecurityPath, StringComparison.Ordinal)
            ? Answer(new
            {
                value = answeredWithoutIsSecured
                    ? new object[] { new { LogicalName = "sprk_matterdescription" }, new { LogicalName = "sprk_defaulteventtype", IsSecured = false } }
                    : [new { LogicalName = "sprk_defaulteventtype", IsSecured = false }],
            })
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description", "a column whose security cannot be told is not copied (fail closed)");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull("a column stated unsecured is copied");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_description");
    }

    // ── (h) the source-security read throws: nothing is mapped, with a warning ──

    [Fact]
    public async Task Task064c_WhenTheSourceSecurityReadThrows_NothingIsMapped_WithAWarning()
    {
        MatterEventTypeToEventProfile();
        _user.ThrowOn = path => path.StartsWith(SourceSecurityPath, StringComparison.Ordinal)
            ? new HttpRequestException("metadata read failed")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_EventType_Ref@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingUnavailableCode);
    }

    // ── item 2: a registry Assigned-To column without the prefix keeps the caller's checks ──

    [Fact]
    public async Task Task064c_AnAccessConferringRegistryColumnWithoutThePrefix_KeepsTheCallersChecks()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));
        var services = new ServiceCollection().AddSingleton(RecordTypeCatalog());
        services.Configure<MembershipOptions>(o => o.AccessConferringRoles.Entities["sprk_event"] =
            [new AccessConferringColumn { Field = "sprk_eventtype_ref", IdentityType = "Contact" }]);
        _scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_EventType_Ref@odata.bind",
            "a column the bound access-conferring registry names is an Assigned-To column, prefix or not");
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_eventtype_ref");
    }

    // ── task 064d: a registry that cannot be read keeps the caller's checks on every mapped value ──

    [Fact]
    public async Task Task064d_WhenTheAccessConferringRegistryCannotBeRead_AValueFromTheFilingParent_KeepsTheCallersChecks()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));
        var services = new ServiceCollection().AddSingleton(RecordTypeCatalog());
        services.AddSingleton<IOptions<MembershipOptions>>(new UnreadableMembershipOptions());
        _scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue("a registry that cannot be read never fails the create: {0}", outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_EventType_Ref@odata.bind",
            "with the registry unread every mapped column counts as Assigned-To, so the caller's AppendTo check runs and its refusal holds");
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_eventtype_ref");
    }

    // ── task 064d: a regarding pair whose record type is not told is not the filing record ──

    [Theory]
    [InlineData(false)] // the record-type read answers nothing
    [InlineData(true)]  // the record-type read faults
    public async Task Task064d_AnExplicitSourceCarryingThePairsId_WhoseRecordTypeIsNotTold_IsNotTheFilingRecord(bool faults)
    {
        var resolution = new MappingSourceResolution(new MappingSource("sprk_matter", OrdinaryMatter), MappingSourceOrigin.Explicit, null);
        var callerRow = new Entity("sprk_event")
        {
            [SecureRootInheritance.PairIdColumn] = OrdinaryMatter.ToString("D"),
            [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
        };

        var filing = await RecordCreationService.SourceIsCallersFilingRecordAsync(
            "sprk_event", resolution, callerRow,
            (_, _) => faults ? throw new HttpRequestException("record type read failed") : Task.FromResult<string?>(null),
            CancellationToken.None);

        filing.Should().BeFalse("a pair whose table cannot be told is no match: the value keeps the caller's checks");
    }

    [Theory]
    [InlineData("sprk_matter", true)]   // the pair's type names the source's table: the filing record
    [InlineData("sprk_project", false)] // the pair's type names another table: not the filing record
    public async Task Task064d_AnExplicitSourceCarryingThePairsId_IsTheFilingRecord_OnlyWhenTheRecordTypesTableIsTheSources(
        string pairTable, bool filing)
    {
        var resolution = new MappingSourceResolution(new MappingSource("sprk_matter", OrdinaryMatter), MappingSourceOrigin.Explicit, null);
        var callerRow = new Entity("sprk_event")
        {
            [SecureRootInheritance.PairIdColumn] = OrdinaryMatter.ToString("D"),
            [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
        };

        var result = await RecordCreationService.SourceIsCallersFilingRecordAsync(
            "sprk_event", resolution, callerRow, (_, _) => Task.FromResult<string?>(pairTable), CancellationToken.None);

        result.Should().Be(filing);
    }

    // ── task 064d: a rule's source column is compared by its trimmed, lower-case logical name ──

    [Theory]
    [InlineData(false)] // the column is not field-secured: the value is mapped
    [InlineData(true)]  // the column is field-secured: nothing is mapped
    public async Task Task064d_ARuleWhoseSourceFieldIsSpelledInMixedCaseWithTrailingSpace_IsCheckedForFieldSecurity(bool secured)
    {
        Profile("sprk_matter", "sprk_event", Rule("Sprk_MatterDescription ", "sprk_description", Memo));
        if (secured)
            _scripted.SecuredColumns.Add("sprk_matterdescription");

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        if (secured)
        {
            fields.Should().NotContainKey("sprk_description", "a field-secured source column is never copied, whatever its spelling");
            outcome.MappingWarnings.Should().ContainSingle()
                .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_description");
        }
        else
        {
            StringOf(fields, "sprk_description").Should().Be("From the matter");
            outcome.MappingWarnings.Should().BeEmpty();
        }
    }

    /// <summary>The membership options as a host whose configuration cannot be read: the value is not available.</summary>
    private sealed class UnreadableMembershipOptions : IOptions<MembershipOptions>
    {
        public MembershipOptions Value => throw new InvalidOperationException("the membership options cannot be read");
    }

    // ── item 3: the step trace counts the values copied without the caller's checks ──

    [Fact]
    public async Task Task064c_TheCallerChecksStep_CountsTheValuesCopiedWithoutTheCallersChecks()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.CallerChecks).Note
            .Should().EndWith("; 2 mapped value(s) copied without the caller's checks");
    }

    // ── item 4: a caller-supplied target column whose security is not stated counts as secured ──

    [Fact]
    public async Task Task064c_ACallersTargetColumnWhoseSecurityIsNotStated_IsCheckedAsFieldSecured_AndRefuses()
    {
        _user.FailOn = path => path.StartsWith("EntityDefinitions(LogicalName='sprk_event')/Attributes?$select=LogicalName,IsSecured", StringComparison.Ordinal)
            ? Answer(new { value = new object[] { new { LogicalName = "sprk_location" } } })
            : null;

        var outcome = await Service().CreateAsync(
            Request("sprk_event", new Dictionary<string, object?>
            {
                ["sprk_eventname"] = "Hearing",
                ["sprk_location"] = "Room 1",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull("a column whose security is not stated is checked as field-secured (fail closed)");
        _creates.Should().BeEmpty();
    }
}
