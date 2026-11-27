using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 DI-051 (#3258) and task 065 (owner 2026-11-26): a push applies create's field-secured
/// SOURCE rule (<see cref="FieldSecuredSourceRule"/>) unless the pushed rows are filed under the source by a listed Filing edge.
/// <list type="bullet">
/// <item>Filing edge (an ordinary push whose parent lookup is the edge's column; family mode, in
/// <c>FieldMappingPushFamilyContractTests.FieldSecuredSource.cs</c>): a rule that reads a field-secured source column pushes
/// like any other. The source is still read as the caller, filtered to that one record, so a column the caller cannot read
/// copies nothing (<c>Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_IsNeverCopiedIntoAChild</c> in the main file).</item>
/// <item>Any other lookup to the source: the rule is not pushed and the target column it would have filled is named in a warning;
/// a column whose security the catalog does not state counts as secured; an unsecured source column pushes as before.</item>
/// </list>
/// The host's default target, <c>sprk_event</c>, names the matter through <c>sprk_regardingmatter</c>, a listed Filing edge;
/// <see cref="NonFilingLookup"/> is a lookup to the matter that no Filing edge lists.
/// </summary>
public partial class FieldMappingPushAuthorizationContractTests
{
    /// <summary>A lookup from <c>sprk_event</c> to <c>sprk_matter</c> that is not the column of a listed Filing edge.</summary>
    private const string NonFilingLookup = "sprk_relatedmatter";

    /// <summary>The Filing edge column from <c>sprk_matter</c> down to <c>sprk_event</c>.</summary>
    private const string FilingLookup = "sprk_regardingmatter";

    private static FieldMappingRuleEntity[] ClientReferenceAndMatterIdRules() =>
    [
        PushHost.Rule("sprk_clientreference", 0, "sprk_clientreference", 0),
        PushHost.Rule("sprk_matterid", 0, "sprk_sourcematterid", 0),
    ];

    private static string SecuredWarning(string column) => $"'{column}' was not updated: {FieldSecuredSourceRule.NotCopiedReason}.";

    // ───────────────────────── Filing edge: the rule is lifted ─────────────────────────

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnIsFieldSecured_PushesTheValueTheCallerCanRead_AndIsNotCountedAsSkipped()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules(), lookupsToSource: [FilingLookup]);
        host.SourceColumnSecurity["sprk_clientreference"] = true; // the caller can read it; the child column is not secured

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.ChildQueries.Should().ContainSingle().Which.Query.Should().StartWith($"$filter=_{FilingLookup}_value eq ",
            "the children are the rows filed under the source by the Filing edge");
        host.Writes.Should().HaveCount(2).And.OnlyContain(w => (string?)w.Payload["sprk_clientreference"] == "REF-166",
            "the children are filed under the source, so a field-secured source value copies (task 065)");
        host.Writes.Should().OnlyContain(w => w.Payload.ContainsKey("sprk_sourcematterid"), "the other rules still push");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Should().BeEmpty();
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
        body["updatedCount"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task Push_ACopyReadsOnlyTheOneSourceRecord_AsTheCaller_AndAsksNoColumnSecurity()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules());
        host.SourceColumnSecurity["sprk_clientreference"] = true;

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.SourceReads.Should().ContainSingle("the values come from one read of the one source record").Which.Should().Be(
            ("sprk_matters", (string?)$"$select=sprk_clientreference,sprk_matterid&$filter=sprk_matterid eq {MatterId:D}&$top=1",
             CallerSystemUserId));
        host.MetadataQueries.Should().NotContain(q => q.Query != null && q.Query.Contains(FieldSecuredSourceRule.IsSecuredProperty),
            "on a Filing edge the push does not read the source columns' field security");
        host.FieldMappings.Verify(f => f.RetrieveRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "no app-only source read");
    }

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnSecurityCannotBeTold_PushesLikeAnyOther()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules());
        host.SourceColumnSecurity["sprk_clientreference"] = null; // the catalog does not say; not asked on a Filing edge

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().HaveCount(2).And.OnlyContain(w => (string?)w.Payload["sprk_clientreference"] == "REF-166");
        JsonNode.Parse(text)!["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnIsExplicitlyNotSecured_PushesAsBefore_WithNoWarning()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData();
        host.SourceColumnSecurity["sprk_clientreference"] = false;

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().HaveCount(2).And.OnlyContain(w => (string?)w.Payload["sprk_clientreference"] == "REF-166");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Should().BeEmpty();
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
    }

    // ───────────────────────── any other lookup: the rule holds ─────────────────────────

    [Fact]
    public async Task Push_ThroughALookupThatIsNotAFilingEdge_ARuleWhoseSourceColumnIsFieldSecured_IsSkippedWithAWarningNamingItsTarget()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules(), lookupsToSource: [NonFilingLookup]);
        host.SourceColumnSecurity["sprk_clientreference"] = true; // the caller can read it; the child column is not secured

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.ChildQueries.Should().ContainSingle().Which.Query.Should().StartWith($"$filter=_{NonFilingLookup}_value eq ",
            "the children are the rows that name the source through a lookup no Filing edge lists");
        host.Writes.Should().HaveCount(2);
        host.Writes.Should().OnlyContain(w => !w.Payload.ContainsKey("sprk_clientreference"),
            "rows not filed under the source keep the rule: a field-secured source value is not copied into them");
        host.Writes.Should().OnlyContain(w => w.Payload.ContainsKey("sprk_sourcematterid"), "the other rules still push");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal(SecuredWarning("sprk_clientreference"));
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(1);
        host.SourceReads.Should().ContainSingle().Which.Query.Should().NotContain("sprk_clientreference",
            "the secured column is not even read for the push");
        host.MetadataQueries.Should().Contain(q => q.Query != null && q.Query.Contains(FieldSecuredSourceRule.IsSecuredProperty),
            "the source catalog is read with IsSecured, as the caller");
    }

    [Fact]
    public async Task Push_ThroughALookupThatIsNotAFilingEdge_ARuleWhoseSourceColumnSecurityCannotBeTold_MapsNothingFromIt_AndWarns()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules(), lookupsToSource: [NonFilingLookup]);
        host.SourceColumnSecurity["sprk_clientreference"] = null; // the catalog does not say: fail closed

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().HaveCount(2).And.OnlyContain(w => !w.Payload.ContainsKey("sprk_clientreference"),
            "anything but an explicit IsSecured == false counts as secured");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal(SecuredWarning("sprk_clientreference"));
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task Push_ThroughALookupThatIsNotAFilingEdge_WhenEveryRuleReadsAFieldSecuredColumn_NothingIsReadOrWritten_AndEachTargetIsNamed()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(lookupsToSource: [NonFilingLookup]);
        host.SourceColumnSecurity["sprk_clientreference"] = true;

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var body = JsonNode.Parse(text)!;
        body["updatedCount"]!.GetValue<int>().Should().Be(0);
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal(SecuredWarning("sprk_clientreference"));
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(1);
        host.Writes.Should().BeEmpty();
        host.SourceReads.Should().BeEmpty("no rule is left to read the source for");
        host.ChildQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Push_ThroughALookupThatIsNotAFilingEdge_ARuleWhoseSourceColumnIsExplicitlyNotSecured_PushesAsBefore_WithNoWarning()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(lookupsToSource: [NonFilingLookup]);
        host.SourceColumnSecurity["sprk_clientreference"] = false;

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().HaveCount(2).And.OnlyContain(w => (string?)w.Payload["sprk_clientreference"] == "REF-166");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Should().BeEmpty();
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
    }

    // ───────────────────────── the edge decision ─────────────────────────

    [Theory]
    [InlineData("sprk_matter", "sprk_event", FilingLookup, true)]
    [InlineData("sprk_matter", "sprk_invoice", "sprk_matter", true)]
    [InlineData(" SPRK_Matter ", "sprk_event", "SPRK_RegardingMatter", true)]
    [InlineData("sprk_matter", "sprk_event", NonFilingLookup, false)]
    [InlineData("sprk_matter", "sprk_event", null, false)]
    [InlineData("sprk_matter", "sprk_event", "sprk_regardingproject", false)] // an edge column of sprk_event, but from sprk_project
    [InlineData("sprk_project", "sprk_event", FilingLookup, false)] // the column files sprk_event under a matter, not a project
    [InlineData("sprk_event", "sprk_eventlog", "sprk_event", false)] // a detail table is never listed
    public void IsListedFilingEdgeColumn_IsTrueOnlyForTheColumnOfAListedFilingEdgeFromTheSourceToTheTarget(
        string source, string target, string? lookup, bool expected) =>
        FieldMappingEndpoints.IsListedFilingEdgeColumn(source, target, lookup).Should().Be(expected);
}
