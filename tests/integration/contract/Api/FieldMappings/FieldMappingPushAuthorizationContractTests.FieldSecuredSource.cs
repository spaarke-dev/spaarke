using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 DI-051 (#3258): a push applies create's field-secured SOURCE rule
/// (<see cref="FieldSecuredSourceRule"/>). A rule that reads a field-secured source column is not pushed, whatever the caller
/// may read, and the target column it would have filled is named in a warning; a column whose security the catalog does not
/// state counts as secured; an unsecured source column pushes as before.
/// </summary>
public partial class FieldMappingPushAuthorizationContractTests
{
    private static FieldMappingRuleEntity[] ClientReferenceAndMatterIdRules() =>
    [
        PushHost.Rule("sprk_clientreference", 0, "sprk_clientreference", 0),
        PushHost.Rule("sprk_matterid", 0, "sprk_sourcematterid", 0),
    ];

    private static string SecuredWarning(string column) => $"'{column}' was not updated: {FieldSecuredSourceRule.NotCopiedReason}.";

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnIsFieldSecured_IsSkippedWithAWarningNamingItsTarget_AndTheOtherRulesPush()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules());
        host.SourceColumnSecurity["sprk_clientreference"] = true; // the caller can read it; the child column is not secured

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().HaveCount(2);
        host.Writes.Should().OnlyContain(w => !w.Payload.ContainsKey("sprk_clientreference"),
            "a value read from a field-secured column is never copied into a column that is not (declassification)");
        host.Writes.Should().OnlyContain(w => w.Payload.ContainsKey("sprk_sourcematterid"), "the other rules still push");
        var body = JsonNode.Parse(text)!;
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal(SecuredWarning("sprk_clientreference"));
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(1);
        host.SourceReads.Should().ContainSingle().Which.Query.Should().NotContain("sprk_clientreference",
            "the secured column is not even read for the push");
    }

    [Fact]
    public async Task Push_ARuleWhoseSourceColumnSecurityCannotBeTold_MapsNothingFromIt_AndWarns()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(rules: ClientReferenceAndMatterIdRules());
        host.SourceColumnSecurity["sprk_clientreference"] = null; // the catalog does not say: fail closed

        var response = await host.SendAsync(Authenticated(PushBody()));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().OnlyContain(w => !w.Payload.ContainsKey("sprk_clientreference"),
            "anything but an explicit IsSecured == false counts as secured");
        JsonNode.Parse(text)!["warnings"]!.AsArray().Select(w => w!.GetValue<string>())
            .Should().Equal(SecuredWarning("sprk_clientreference"));
    }

    [Fact]
    public async Task Push_WhenEveryRuleReadsAFieldSecuredColumn_NothingIsReadOrWritten_AndEachTargetIsNamed()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData();
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
