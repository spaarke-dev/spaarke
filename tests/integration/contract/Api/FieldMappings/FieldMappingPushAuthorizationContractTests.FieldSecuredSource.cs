using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 065 (owner 2026-11-26, superseding DI-051 on the push): the pushed children are the
/// rows filed under the one source record, so a rule that reads a field-secured source column pushes like any other. The
/// source is still read as the caller, filtered to that one record, so a column the caller cannot read copies nothing
/// (<c>Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_IsNeverCopiedIntoAChild</c> in the main file). The push no
/// longer asks the source catalog for <c>IsSecured</c> and no longer counts or names such a rule.
/// </summary>
public partial class FieldMappingPushAuthorizationContractTests
{
    private static FieldMappingRuleEntity[] ClientReferenceAndMatterIdRules() =>
    [
        PushHost.Rule("sprk_clientreference", 0, "sprk_clientreference", 0),
        PushHost.Rule("sprk_matterid", 0, "sprk_sourcematterid", 0),
    ];

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnIsFieldSecured_PushesTheValueTheCallerCanRead_AndIsNotCountedAsSkipped()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules());
        host.SourceColumnSecurity["sprk_clientreference"] = true; // the caller can read it; the child column is not secured

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
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
            "the push no longer reads the source columns' field security");
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
        host.SourceColumnSecurity["sprk_clientreference"] = null; // the catalog does not say

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
}
