using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// The ONE shared playbook-parameter policy (unified-access-control-r2 task 164, owner round 16 item 3): which caller
/// parameters a run accepts and in what shape. Pins the closed lists and each rule; the HTTP-entry enforcement (400 / 403,
/// nothing runs) is proven through the real hosts in <c>PlaybookRouteAuthorizationContractTests</c>.
/// </summary>
public class PlaybookParameterPolicyTests
{
    // =========================================================================================
    // The closed lists — changing one is a policy change, made deliberately and recorded in the task note.
    // =========================================================================================

    [Fact]
    public void TheServerOwnedKeys_AreExactlyTheDecidedSet()
    {
        PlaybookParameterPolicy.ServerOwnedKeys.Should().BeEquivalentTo(
            "userId", "tenantId", "userName", "run", "start", "userPreferences");
        PlaybookParameterPolicy.ServerOwnedPrefixes.Should().BeEquivalentTo("run.", "start.", "userPreferences.");
    }

    [Fact]
    public void TheRecordIdentityMap_IsExactlyMatterProjectAndInvoice_ByLogicalName()
    {
        PlaybookParameterPolicy.RecordIdentityParameters.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["matterId"] = "sprk_matter",
            ["projectId"] = "sprk_project",
            ["invoiceId"] = "sprk_invoice",
        });
    }

    [Fact]
    public void TheTypedKeys_AreTheSchedulerTuningKeys_WithTheirTypes()
    {
        PlaybookParameterPolicy.TypedParameters.Keys.Should().BeEquivalentTo(
            "timeWindowHours", "dueWithinDays", "todayUtc", "dueSoonWindowUtc");
        PlaybookParameterPolicy.TypedParameters["timeWindowHours"].Should().Be(
            new PlaybookParameterPolicy.TypedParameter(PlaybookParameterPolicy.ParameterValueType.Integer, 1, 8760));
        PlaybookParameterPolicy.TypedParameters["todayUtc"].Type.Should().Be(PlaybookParameterPolicy.ParameterValueType.IsoDateTime);
    }

    [Fact]
    public void TheTextKeys_AreTheReasonedNonRecordList()
    {
        PlaybookParameterPolicy.TextParameters.Should().BeEquivalentTo(
            "matterDescription", "matterContext", "assessments", "currentGrade", "observations",
            "cohortObservations", "liveFacts", "precedents", "focus", "practiceAreaHint", "documentTypeHint");
    }

    // =========================================================================================
    // Evaluate — rules 1-5
    // =========================================================================================

    [Theory]
    [InlineData("userId")]
    [InlineData("USERID")]
    [InlineData("tenantId")]
    [InlineData("TenantID")]
    [InlineData("userName")]
    [InlineData("run")]
    [InlineData("run.userId")]
    [InlineData("Start.channels")]
    [InlineData("userPreferences.timeWindow")]
    public void ServerOwnedKey_InAnyLetterCase_IsRefused(string key)
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string> { [key] = "x" });

        result.IsValid.Should().BeFalse();
        result.RejectedKey.Should().Be(key);
        result.Reason.Should().Be(PlaybookParameterPolicy.ServerOwnedReason);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("6f0c1a52-0000-4000-8000-000000000164' or 1=1")]
    public void RecordIdentityKey_ThatIsNotARecordId_IsRefused(string value)
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string> { ["matterId"] = value });

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(PlaybookParameterPolicy.NotARecordIdReason);
    }

    [Fact]
    public void RecordIdentityKeys_ThatAreGuids_AreReturnedToAuthorize_WithTheirEntity()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();

        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string>
        {
            ["MatterId"] = matter.ToString(),
            ["projectId"] = project.ToString("B"),
        });

        result.IsValid.Should().BeTrue();
        result.RecordParameters.Should().BeEquivalentTo(new[]
        {
            new PlaybookParameterPolicy.RecordParameter("MatterId", "sprk_matter", matter),
            new PlaybookParameterPolicy.RecordParameter("projectId", "sprk_project", project),
        });
    }

    [Theory]
    [InlineData("timeWindowHours", "24", true)]
    [InlineData("timeWindowHours", "1", true)]
    [InlineData("timeWindowHours", "8760", true)]
    [InlineData("timeWindowHours", "0", false)]
    [InlineData("timeWindowHours", "8761", false)]
    [InlineData("timeWindowHours", "-5", false)]
    [InlineData("timeWindowHours", "24\" operator=\"ne", false)]
    [InlineData("timeWindowHours", "abc", false)]
    [InlineData("todayUtc", "2026-10-04", true)]
    [InlineData("todayUtc", "2026-10-04T00:00:00Z", true)]
    [InlineData("todayUtc", "2026-10-04T12:30:15.123+02:00", true)]
    [InlineData("todayUtc", "2026-10-04'/><condition attribute='x", false)]
    [InlineData("todayUtc", "2026-10-04' or", false)]
    [InlineData("todayUtc", "yesterday", false)]
    [InlineData("todayUtc", "2026-10-04T10:00", true)]
    [InlineData("todayUtc", "2026-10-04T10:00:00.1234567Z", true)]
    [InlineData("todayUtc", "2026-10-04T10:00:00.12345678Z", false)]
    [InlineData("todayUtc", "2026-10-04 10:00", false)]
    [InlineData("todayUtc", "10/04/2026", false)]
    [InlineData("dueWithinDays", "3", true)]
    [InlineData("dueSoonWindowUtc", "2026-13-40", false)]
    public void TypedKey_IsAcceptedOnlyWhenItParsesAsItsType(string key, string value, bool accepted)
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string> { [key] = value });

        result.IsValid.Should().Be(accepted);
        if (!accepted)
        {
            result.Reason.Should().Be(PlaybookParameterPolicy.WrongTypeReason);
        }
    }

    [Theory]
    [InlineData("documentOwnerId", "6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("tone", "formal")]
    [InlineData("subject", "matter:6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("myMatters", "x")]
    public void AnUndeclaredKey_IsRefused_WhateverItsValue(string key, string value)
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string> { [key] = value });

        result.IsValid.Should().BeFalse("the policy is a declared allow-list (owner round 16 item 3)");
        result.RejectedKey.Should().Be(key);
        result.Reason.Should().Be(PlaybookParameterPolicy.UndeclaredKeyReason);
    }

    [Fact]
    public void ADeclaredTextKey_CarryingAGuid_IsRefused()
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string> { ["focus"] = Guid.NewGuid().ToString() });

        result.IsValid.Should().BeFalse("a record id is accepted only on a record-identity key");
        result.Reason.Should().Be(PlaybookParameterPolicy.UndeclaredRecordIdReason);
    }

    [Fact]
    public void ADeclaredTextKey_IsAccepted_WithNoRecordToAuthorize()
    {
        var result = PlaybookParameterPolicy.Evaluate(new Dictionary<string, string>
        {
            ["focus"] = "termination clauses",
            ["matterContext"] = "<b>O'Brien & Sons</b> v. Acme",
        });

        result.IsValid.Should().BeTrue();
        result.RecordParameters.Should().BeEmpty();
    }

    [Fact]
    public void NoParameters_IsValid()
    {
        PlaybookParameterPolicy.Evaluate(null).IsValid.Should().BeTrue();
        PlaybookParameterPolicy.Evaluate(new Dictionary<string, string>()).IsValid.Should().BeTrue();
    }

    // =========================================================================================
    // The substitution-point type check and the node-reference detector
    // =========================================================================================

    [Fact]
    public void EnsureTypedParametersValid_ThrowsOnAWrongType_NamingTheKeyButNotTheValue()
    {
        var act = () => PlaybookParameterPolicy.EnsureTypedParametersValid(
            new Dictionary<string, string> { ["timeWindowHours"] = "24' secret" });

        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("timeWindowHours") && !ex.Message.Contains("secret"));
    }

    [Fact]
    public void EnsureTypedParametersValid_AcceptsTheSchedulersOwnValues()
    {
        var act = () => PlaybookParameterPolicy.EnsureTypedParametersValid(new Dictionary<string, string>
        {
            ["userId"] = Guid.NewGuid().ToString(),
            ["userName"] = "Pat Smith",
            ["todayUtc"] = "2026-10-04T08:00:00Z",
            ["dueSoonWindowUtc"] = "2026-10-07T08:00:00Z",
            ["timeWindowHours"] = "24",
            ["dueWithinDays"] = "3",
        });

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("{\"recordId\":\"{{matterId}}\"}", true)]
    [InlineData("{\"recordId\":\"{{ matterId }}\"}", true)]
    [InlineData("{\"recordId\":\"{{start.matterId}}\"}", true)]
    [InlineData("{\"subject\":\"matter:{{default matterId 'x'}}\"}", true)]
    [InlineData("{\"recordId\":\"{{matterIdentity}}\"}", false)]
    [InlineData("{\"recordId\":\"{{othermatterId}}\"}", false)]
    [InlineData("{\"note\":\"matterId\"}", false)]
    [InlineData("{\"recordId\":\"{{{matterId}}}\"}", true)]
    [InlineData("{\"recordId\":\"{{#if matterId}}x{{/if}}\"}", true)]
    [InlineData("{\"recordId\":\"{{ MATTERID }}\"}", true)]
    [InlineData("{\"recordId\":\"{{other}} matterId {{x}}\"}", false)]
    [InlineData("{\"recordId\":\"{matterId}\"}", false)]
    [InlineData("{\"recordId\":\"{{matterId\"}", false)]
    [InlineData("{\"recordId\":\"{{foo_matterId}} {{matterId_x}}\"}", false)]
    [InlineData("{\"recordId\":\"{{matterIdx matterId}}\"}", true)]
    [InlineData(null, false)]
    public void ReferencesParameter_MatchesTheKeyInsideAnyTemplateExpression(string? configJson, bool expected)
    {
        PlaybookParameterPolicy.ReferencesParameter(configJson, "matterId").Should().Be(expected);
    }
}
