using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060), the contract task 130e-2's editor builds on: <c>PATCH /api/finance/rulesets/{ruleSetId}/proposals/{policyId}</c>
/// takes <c>firmDescription</c> (absent or null leaves it, blank clears it); a refused firm description is a 422 whose <c>errors[]</c> entry has
/// <c>field</c> "firmDescription" and a stable <c>reason</c>, at edit and at publish; any other invalid edit stays 400. (A change on a published
/// version lands on the new draft the edit creates: fix round 1 F1, proved in RuleSetServiceTests.)
/// </summary>
public sealed partial class RuleSetEndpointsTests
{
    [Theory(DisplayName = "130e-1 contract: the edit body's firmDescription reaches the service as sent; an absent member is null (left as it is)")]
    [InlineData("{\"firmDescription\":\"Travel is billed at half rate.\"}", "Travel is billed at half rate.")]
    [InlineData("{\"firmDescription\":\"\"}", "")]
    [InlineData("{\"headline\":\"x\"}", null)]
    public async Task Edit_PassesTheFirmDescription(string body, string? expected)
    {
        await using var host = await Host.StartAsync();
        ProposalEdit? seen = null;
        host.Service.Setup(s => s.EditAsync(SetId, PolicyId, It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, ProposalEdit edit, CancellationToken _) => seen = edit)
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Refused(409, RuleSetService.CodeRetired, "retired"));

        await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}",
            content: new StringContent(body, System.Text.Encoding.UTF8, "application/json")));

        seen.Should().NotBeNull();
        seen!.FirmDescription.Should().Be(expected);
    }

    [Fact(DisplayName = "130e-1 contract: a refused firm description is 422 with field firmDescription and its reason; another invalid edit stays 400")]
    public async Task Edit_ARefusedFirmDescription_Is422()
    {
        await using var host = await Host.StartAsync();
        host.Service.SetupSequence(s => s.EditAsync(SetId, PolicyId, It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Invalid(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(PolicyId, "Rule BILL-A", "firmDescription", "The firm description names the rule set or the rule.", FirmDescriptionRules.NamesTheRule),
            }))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Invalid(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(PolicyId, "Rule BILL-A", "ruleType", "wrong domain", "rule_type_wrong_domain"),
            }));

        var refused = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new { firmDescription = "x" })));
        var other = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new { ruleType = "Existence" })));

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await Json(refused);
        body["errorCode"]!.GetValue<string>().Should().Be("ruleset.invalid");
        body["errors"]![0]!["field"]!.GetValue<string>().Should().Be("firmDescription");
        body["errors"]![0]!["reason"]!.GetValue<string>().Should().Be("firm_description_names_rule");
        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "130e-1 contract (Q4): a publish refused for a firm description is 422 ruleset.publish_invalid with every per-rule error")]
    public async Task Publish_ARefusedFirmDescription_Is422()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.PublishAsync(SetId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetPublished>.Invalid(RuleSetService.CodePublishInvalid, new[]
            {
                new RuleSetError(PolicyId, "Rule BILL-A", "firmDescription", "The firm description names the rule set or the rule.", FirmDescriptionRules.NamesTheRule),
                new RuleSetError(Guid.NewGuid(), "Rule BILL-B", "consequence", "required", null),
            }));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/publish", content: JsonContent.Create(new { effectiveDate = "2026-11-01" })));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ruleset.publish_invalid");
        body["errors"]!.AsArray().Should().HaveCount(2);
    }
}
