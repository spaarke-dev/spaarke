using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>049 D-133: the text of <c>sprk_invoicesubmission.sprk_adminattention</c> (pure function of the stored refusals).</summary>
public class AdminAttentionTextTests
{
    private static AdminAttentionText.Item Line(string policy, string code, string id = "0a1b2c3d-0000-0000-0000-000000000001") =>
        new(policy, "sprk_billingevent", id, code);

    [Fact(DisplayName = "049 D-133: no refusal gives no text (null, never an empty string)")]
    public void None_IsNull() => AdminAttentionText.Build([]).Should().BeNull();

    [Fact(DisplayName = "049 D-133: one refusal reads plainly: count, code, policy, subject and an id prefix")]
    public void One_Reads_Plainly() =>
        AdminAttentionText.Build([Line("BILL-RATE", "record_owner_no_default_team")])
            .Should().Be("1 Signal could not be created: record_owner_no_default_team (BILL-RATE on line 0a1b2c3d)");

    [Fact(DisplayName = "049 D-133: two refusals are counted, in order, and a repeated entry counts once")]
    public void Two_AreCounted_InOrder_AndDeduplicated() =>
        AdminAttentionText.Build([
                Line("BILL-RATE", "record_owner_no_default_team"),
                new AdminAttentionText.Item("BILL-AGE", "sprk_invoice", "9f8e7d6c-0000-0000-0000-000000000002", "core_record_changed"),
                Line("BILL-RATE", "record_owner_no_default_team")])
            .Should().Be("2 Signals could not be created: record_owner_no_default_team (BILL-RATE on line 0a1b2c3d), core_record_changed (BILL-AGE on invoice 9f8e7d6c)");

    [Fact(DisplayName = "049 D-133: the same refusals always give the same text")]
    public void SameRefusals_SameText()
    {
        var items = Enumerable.Range(0, 60).Select(i => Line($"BILL-{i}", "core_record_changed", Guid.NewGuid().ToString("D"))).ToList();

        AdminAttentionText.Build(items).Should().Be(AdminAttentionText.Build(items.ToList()));
    }

    [Theory(DisplayName = "049 D-133: an overflow is capped at 500 characters and ends with the count that did not fit")]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(500)]
    public void Overflow_IsCapped_WithTheCount(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => Line($"BILL-{i}", "record_owner_secure_parent_not_isolated", Guid.NewGuid().ToString("D"))).ToList();

        var text = AdminAttentionText.Build(items)!;

        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.AdminAttentionMaxLength);
        text.Should().StartWith($"{count} Signals could not be created: ");
        var kept = text.Split("record_owner_secure_parent_not_isolated").Length - 1;
        kept.Should().BeGreaterThan(0).And.BeLessThan(count);
        text.Should().EndWith($", … (+{count - kept} more)");
    }

    [Fact(DisplayName = "049 D-133: FromFindings reads signalPlanRefusals then signalWriteRefusals; neither member gives null")]
    public void FromFindings_ReadsBothMembers()
    {
        AdminAttentionText.FromFindings(new JsonObject()).Should().BeNull();
        var findings = new JsonObject
        {
            [InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = new JsonArray(new JsonObject
            {
                ["policyCode"] = "BILL-W", ["code"] = "dataverse_access_denied",
                ["subject"] = new JsonObject { ["entity"] = "sprk_billingevent", ["id"] = "aaaaaaaa-0000-0000-0000-000000000000" },
            }),
            [InvoiceSubmissionPersister.SignalPlanRefusalsMember] = new JsonArray(new JsonObject
            {
                ["policyCode"] = "BILL-P", ["code"] = "sentence_too_long",
                ["subject"] = new JsonObject { ["entity"] = "sprk_invoice", ["id"] = "bbbbbbbb-0000-0000-0000-000000000000" },
            }),
        };

        AdminAttentionText.FromFindings(findings).Should().Be(
            "2 Signals could not be created: sentence_too_long (BILL-P on invoice bbbbbbbb), dataverse_access_denied (BILL-W on line aaaaaaaa)");
    }
}
