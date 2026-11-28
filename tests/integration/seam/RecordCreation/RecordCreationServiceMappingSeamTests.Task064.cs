using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 064 (owner 2026-10-27 4b/4c, reconfirmed 2026-11-26): field mapping is a data
/// consistency rule, so an ORDINARY value mapped from the record the caller's row is filed under is written whatever the
/// caller's AppendTo or the target column's field security. The boundaries that stay: a mapped filing lookup is the caller's
/// own (refused, pinned in the main file); a BFF-owned invariant column keeps the caller's checks; a value from a field-secured
/// source column is not copied from a source that is not the filing parent (task 065: from the filing parent it is, when the
/// caller can read it); a source that is not the filing parent keeps the caller's checks; FR-04's source Read check stays;
/// the caller's own values are checked as before.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid MappedEventType = Guid.Parse("a0140000-0000-4000-8000-0000000000e2");

    /// <summary>The matter's event type, copied into an ordinary (non-filing, non-invariant) lookup of the event.</summary>
    private void MatterEventTypeToEventProfile(params FieldMappingRuleEntity[] more)
    {
        _sources[("sprk_matter", OrdinaryMatter)]["sprk_defaulteventtype"] = new EntityReference("sprk_eventtype_ref", MappedEventType);
        Profile("sprk_matter", "sprk_event",
        [
            Rule("sprk_defaulteventtype", "sprk_eventtype_ref", Lookup),
            Rule("sprk_matterdescription", "sprk_description", Memo),
            .. more,
        ]);
        _user.Columns["sprk_event"]["sprk_eventtype_ref"] = ("Lookup", true);
    }

    // ── 4b/4c: an ordinary mapped value from the filing parent is written without the caller's checks ──

    [Fact]
    public async Task CreateAsync_AnOrdinaryMappedLookupFromTheFilingParent_TheCallerCannotAppendTo_IsWritten_WithNoDropWarning()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().Be($"/sprk_eventtype_refs({MappedEventType:D})",
            "mapping is a data consistency rule: an ordinary mapped lookup is copied whatever the caller's AppendTo (owner 4b/4c)");
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappedValueDroppedCode);
    }

    [Fact]
    public async Task CreateAsync_AnOrdinaryMappedValueIntoAFieldSecuredTarget_FromAnUnsecuredSourceColumn_IsWritten()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_description"); // the target is secured; its source column sprk_matterdescription is not

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter",
            "an ordinary mapped value is copied whatever the target column's field security (owner 4b/4c)");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    /// <summary>Task 065: an event filed under the secure matter by the caller's own bind, whose explicit mapping source is
    /// another (ordinary) matter — a source that is not the filing parent.</summary>
    private RecordCreationRequest EventFromASourceThatIsNotTheFilingParent() =>
        EventUnder(SecureMatter, mappingSource: ("sprk_matter", OrdinaryMatter));

    // ── Task 065 (owner 2026-11-26): a value from a field-secured SOURCE column copies from the filing parent ──

    [Fact]
    public async Task CreateAsync_AValueFromAFieldSecuredSourceColumnOfTheFilingParent_TheCallerCanRead_IsWritten_WithNoWarning()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_matterdescription"); // the caller can read it; the target sprk_description is not secured

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter",
            "a mapped value from the record the row is filed under copies whatever its source column's field security (task 065)");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull("the other mapped values still apply");
        outcome.MappingWarnings.Should().BeEmpty("no SourceColumnFieldSecured warning from the filing parent");
    }

    [Fact]
    public async Task CreateAsync_AValueFromAFieldSecuredSourceColumnOfTheFilingParent_IntoAFieldSecuredTarget_IsWritten()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_matterdescription");
        _scripted.SecuredColumns.Add("sprk_description");

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    /// <summary>
    /// Task 067 (owner 2026-11-27), must fire: from the filing parent a field-secured mapped column the caller cannot read is
    /// copied, no caller column read is made, nothing is refused, and one Information line names the rule's source and target
    /// columns, never the value (C7). MUTATION: masking the filing parent leaves sprk_description unwritten.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AFieldSecuredSourceColumnOfTheFilingParent_TheCallerCannotRead_IsCopied_AndLoggedWithoutItsValue()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_matterdescription");
        _callerRead.Secured.Add("sprk_matterdescription");
        var log = new Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.CapturingLog<RecordCreationService>();
        _logger = log;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "the filing parent's value is copied whatever the caller can read");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull();
        outcome.MappingWarnings.Should().BeEmpty();
        _callerRead.Paths.Should().BeEmpty("no caller column read is made for the filing parent (no access-model column is mapped)");
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information
                                          && e.Message.Contains("create: rule")
                                          && e.Message.Contains("sprk_matter.sprk_matterdescription into sprk_description"));
        log.Messages.Should().NotContain(m => m.Contains("From the matter"), "a log line never carries the value");
    }

    /// <summary>Task 067 C2, must not fire: from a source that is not the filing parent, a column the caller cannot read (not
    /// field-secured in metadata) is still masked (#2188), with one caller column read.</summary>
    [Fact]
    public async Task CreateAsync_AColumnTheCallerCannotRead_OfASourceThatIsNotTheFilingParent_IsStillMasked()
    {
        MatterEventTypeToEventProfile();
        _callerRead.Secured.Add("sprk_matterdescription");

        var outcome = await Service().CreateAsync(EventFromASourceThatIsNotTheFilingParent(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description");
        fields.Values.Select(v => v?.ToString()).Should().NotContain(v => v != null && v.Contains("From the matter"));
        _callerRead.Paths.Should().Equal($"sprk_matters({OrdinaryMatter:D})");
    }

    /// <summary>
    /// Task 067 C3, must fire: from the filing parent a rule that reads an access-model column keeps the caller's view, so a value
    /// the caller cannot read there is not copied; the other mapped columns are, and a warning names the rule's target column.
    /// </summary>
    [Theory]
    [InlineData("sprk_externalobjectid")]
    [InlineData("sprk_identitycollisionnote")]
    public async Task CreateAsync_FromTheFilingParent_ARuleReadingAnAccessModelColumn_KeepsTheCallersView_AndIsNamed(string accessColumn)
    {
        _sources[("sprk_matter", OrdinaryMatter)][accessColumn] = "not-to-be-copied";
        MatterEventTypeToEventProfile(Rule(accessColumn, "sprk_location", Text));
        _callerRead.Secured.Add(accessColumn);

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_location");
        fields.Values.Select(v => v?.ToString()).Should().NotContain(v => v != null && v.Contains("not-to-be-copied"));
        StringOf(fields, "sprk_description").Should().Be("From the matter", "the business columns of the filing parent are copied");
        _callerRead.Paths.Should().Equal($"sprk_matters({OrdinaryMatter:D})");
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingRuleSkippedCode, "sprk_location",
            Sprk.Bff.Api.Services.Dataverse.FieldMapping.AccessModelSourceColumns.ReadAsTheCallerWarning("sprk_location")));
    }

    /// <summary>Task 067 C4: the filing parent is read app-only once, for its id, and for exactly the mapped columns (plus the
    /// gate columns the source rule always read, which are never copied).</summary>
    [Fact]
    public async Task CreateAsync_FromTheFilingParent_TheAppOnlyReadAsksForTheOneRecordsMappedColumns()
    {
        MatterEventTypeToEventProfile();

        await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        var read = _sourceReads.Should().ContainSingle().Subject;
        (read.Table, read.Id).Should().Be(("sprk_matter", OrdinaryMatter));
        // The gate reads: owner, secure flag (SourceGateColumns) and the matter's Access Permission (task 022b).
        read.Columns.Except(MappingSourceCopyGate.SourceGateColumns("sprk_matter"), StringComparer.OrdinalIgnoreCase)
            .Except([Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.Column], StringComparer.OrdinalIgnoreCase)
            .Should().BeEquivalentTo("sprk_defaulteventtype", "sprk_matterdescription");
    }

    [Fact]
    public async Task CreateAsync_FromTheFilingParent_WhenWhetherTheSourceColumnsAreSecuredCannotBeRead_TheValuesAreStillMapped()
    {
        MatterEventTypeToEventProfile();
        _user.FailOn = path => path.StartsWith("EntityDefinitions(LogicalName='sprk_matter')/Attributes?$select=LogicalName,IsSecured", StringComparison.Ordinal)
            ? Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserResponse.Fail(503, "ServiceUnavailable", "busy")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter",
            "from the filing parent the source columns' field security does not decide the copy");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull();
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // ── Boundary: from a source that is NOT the filing parent, a value from a field-secured SOURCE column is not copied ──

    [Fact]
    public async Task CreateAsync_AValueFromAFieldSecuredSourceColumnOfASourceThatIsNotTheFilingParent_IsDroppedWithAWarning_AndTheRowIsCreated()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_matterdescription"); // the caller can read it; the target sprk_description is not secured

        var outcome = await Service().CreateAsync(EventFromASourceThatIsNotTheFilingParent(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description", "from another source a field-secured value is not copied");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull("the other mapped values still apply");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
        warning.Column.Should().Be("sprk_description");
        warning.Detail.Should().Contain(RecordCreationService.SourceColumnFieldSecured).And.NotContain("From the matter");
    }

    [Fact]
    public async Task CreateAsync_AValueFromAFieldSecuredSourceColumnOfASourceThatIsNotTheFilingParent_IntoAFieldSecuredTarget_IsNotCopied()
    {
        MatterEventTypeToEventProfile();
        _scripted.SecuredColumns.Add("sprk_matterdescription");
        _scripted.SecuredColumns.Add("sprk_description");

        var outcome = await Service().CreateAsync(EventFromASourceThatIsNotTheFilingParent(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
        warning.Column.Should().Be("sprk_description");
        warning.Detail.Should().Contain(RecordCreationService.SourceColumnFieldSecured);
    }

    [Fact]
    public async Task CreateAsync_FromASourceThatIsNotTheFilingParent_WhenWhetherTheSourceColumnsAreSecuredCannotBeRead_NothingIsMapped_AndTheRowIsCreated()
    {
        MatterEventTypeToEventProfile();
        _user.FailOn = path => path.StartsWith("EntityDefinitions(LogicalName='sprk_matter')/Attributes?$select=LogicalName,IsSecured", StringComparison.Ordinal)
            ? Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserResponse.Fail(503, "ServiceUnavailable", "busy")
            : null;

        var outcome = await Service().CreateAsync(EventFromASourceThatIsNotTheFilingParent(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_EventType_Ref@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Code.Should().Be(RecordCreationWarning.MappingUnavailableCode);
    }

    // ── Boundary: a source that is not the filing parent keeps the caller's checks ──

    [Fact]
    public async Task CreateAsync_AnOrdinaryMappedLookupFromASourceThatIsNotTheFilingParent_TheCallerCannotAppendTo_IsStillDropped()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        // Filed under the SECURE matter by the caller's own bind; the source named explicitly is another (ordinary) matter.
        var outcome = await Service().CreateAsync(
            EventUnder(SecureMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_EventType_Ref@odata.bind",
            "only a value from the record the row is filed under is copied without the caller's checks");
        outcome.MappingWarnings.Should().Contain(w =>
            w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_eventtype_ref");
    }

    // ── Boundary (task 065 must-fire): the row as it will be written is asked whole once more, and a failure there refuses ──

    [Fact]
    public async Task CreateAsync_WhenTheRowAsWrittenFailsTheFinalWholeRowCheck_TheCreateIsRefused_AndNothingIsWritten()
    {
        // AppendTo on the mapped event type answers: denied to the merged-row check, allowed to its own check (so it is kept),
        // denied to the final check of the row as it will be written. The final check is the one that refuses.
        MatterEventTypeToEventProfile();
        var asks = 0;
        _user.FailOn = path =>
        {
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                && path.Contains(MappedEventType.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                asks++;
                if (asks == 2)
                    _scripted.NoAppendToIn.Remove(("sprk_eventtype_refs", MappedEventType));
                else
                    _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));
            }

            return null;
        };

        var outcome = await Service().CreateAsync(EventFromASourceThatIsNotTheFilingParent(), AsCaller(), CancellationToken.None);

        asks.Should().Be(3, "the merged row, the value on its own, then the row as it will be written");
        outcome.Refusal.Should().NotBeNull("a failure of the final whole-row check refuses the create");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        _creates.Should().BeEmpty();
    }

    // ── Boundary: a BFF-owned invariant column keeps the caller's checks, from the filing parent too ──

    [Fact]
    public async Task CreateAsync_AMappedAssignedToFromTheFilingParent_TheCallerCannotAppendTo_IsStillDropped_TheOtherValuesAreWritten()
    {
        MatterEventTypeToEventProfile(Rule("sprk_assignedtointernal", "sprk_assignedto", Lookup));
        _scripted.NoAppendToIn.Add(("contacts", Assignee));
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_AssignedTo@odata.bind", "an Assigned-To column confers access: never copied past a failed check");
        StringOf(fields, "sprk_EventType_Ref@odata.bind").Should().NotBeNull("the ordinary lookup beside it is copied");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_assignedto");
    }

    // ── FR-04 and the caller's own values: unchanged ──

    [Fact]
    public async Task CreateAsync_ASourceTheCallerCannotRead_MapsNothing_EvenAnOrdinaryValue()
    {
        MatterEventTypeToEventProfile();
        _readable.Remove(OrdinaryMatter);

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_EventType_Ref@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceUnavailableCode);
        // Task 067 C1 (must fire): the caller's Read on the filing parent is asked first; without it nothing is read app-only.
        _sourceReads.Should().BeEmpty();
        _callerRead.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ACallersOwnOrdinaryLookupTheyCannotAppendTo_StillRefuses_WhileTheSameMappedLookupWouldBeWritten()
    {
        MatterEventTypeToEventProfile();
        _scripted.NoAppendToIn.Add(("sprk_eventtype_refs", MappedEventType));

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: ("sprk_EventType_Ref@odata.bind", $"/sprk_eventtype_refs({MappedEventType:D})")),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied, "the caller's own values are checked as before");
        _creates.Should().BeEmpty();
    }
}
