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

    // ── R3 / R5: the other segments and the merge ──

    private static JsonObject FindingsWith(int count) => new()
    {
        [InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode)new JsonObject
        {
            ["policyCode"] = $"BILL-{i}", ["code"] = "core_record_changed",
            ["subject"] = new JsonObject { ["entity"] = "sprk_billingevent", ["id"] = Guid.NewGuid().ToString("D") },
        }).ToArray()),
    };

    [Fact(DisplayName = "049 R3: the processing-stopped note names the reason code only and states the invoice is linked")]
    public void StoppedNote_NamesTheCode() =>
        AdminAttentionText.StoppedNote("owner_refused").Should().Be(
            "Processing stopped: owner_refused; the invoice is linked and the submission stays Processing: review it, then resume or correct");

    [Fact(DisplayName = "049 R3: an over-long reason code is cut, an empty one reads 'unknown'")]
    public void StoppedNote_CutsLongCodes()
    {
        AdminAttentionText.StoppedNote(new string('x', 500)).Length.Should().BeLessThan(200);
        AdminAttentionText.StoppedNote(null).Should().StartWith("Processing stopped: unknown;");
    }

    [Theory(DisplayName = "049 R5: the policy note per state; disabled and in-force give no note")]
    [InlineData("missing", "System billing policy BILL-SYSTEM-SUBMISSION is missing: system Signals were not created")]
    [InlineData("ambiguous", "System billing policy BILL-SYSTEM-SUBMISSION is ambiguous (more than one): system Signals were not created")]
    [InlineData("without_a_version_in_force", "System billing policy BILL-SYSTEM-SUBMISSION is without a version in force: system Signals were not created")]
    [InlineData("of_another_rule_type", "System billing policy BILL-SYSTEM-SUBMISSION is of the wrong rule type: system Signals were not created")]
    [InlineData("disabled", null)]
    [InlineData("in_force", null)]
    public void PolicyNote_PerState(string state, string? expected) =>
        AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", state).Should().Be(expected);

    [Fact(DisplayName = "049 R3/R5: Merge keeps each segment, replaces only its own kind, and always orders signals, policy, stopped")]
    public void Merge_KeepsOthers_ReplacesOwnKind_FixedOrder()
    {
        var signals = AdminAttentionText.Build([Line("BILL-RATE", "record_owner_no_default_team")])!;
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "missing")!;
        var stopped = AdminAttentionText.StoppedNote("a");

        var one = AdminAttentionText.Merge(signals, null, null, stopped);
        var two = AdminAttentionText.Merge(one, null, policy, null);
        var replaced = AdminAttentionText.Merge(two, null, null, AdminAttentionText.StoppedNote("b"));

        one.Should().Be(signals + AdminAttentionText.Separator + stopped);
        two.Should().Be(signals + AdminAttentionText.Separator + policy + AdminAttentionText.Separator + stopped);
        replaced.Should().Be(signals + AdminAttentionText.Separator + policy + AdminAttentionText.Separator + AdminAttentionText.StoppedNote("b"));
        AdminAttentionText.Merge(replaced, null, null, AdminAttentionText.StoppedNote("b")).Should().Be(replaced, "the same cause merges to the same text");
        AdminAttentionText.Merge(stopped + AdminAttentionText.Separator + policy, null).Should().Be(policy + AdminAttentionText.Separator + stopped, "the order is normalised");
    }

    [Fact(DisplayName = "049 R3/R5: Merge rebuilds the Signal segment from the stored refusals and keeps the notes; no refusals drops a stale Signal segment")]
    public void Merge_RebuildsSignalSegment_FromFindings()
    {
        var findings = new JsonObject
        {
            [InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = new JsonArray(new JsonObject
            {
                ["policyCode"] = "BILL-W", ["code"] = "dataverse_access_denied",
                ["subject"] = new JsonObject { ["entity"] = "sprk_billingevent", ["id"] = "aaaaaaaa-0000-0000-0000-000000000000" },
            }),
        };
        var stopped = AdminAttentionText.StoppedNote("x");

        AdminAttentionText.Merge(stopped, findings).Should().Be(
            "1 Signal could not be created: dataverse_access_denied (BILL-W on line aaaaaaaa)" + AdminAttentionText.Separator + stopped);
        AdminAttentionText.Merge("1 Signal could not be created: gone (BILL-X on line 00000000)" + AdminAttentionText.Separator + stopped, new JsonObject())
            .Should().Be(stopped);
    }

    [Fact(DisplayName = "049 R3/R5: text an administrator typed is kept, and nothing at all is null")]
    public void Merge_KeepsAdminText_AndNothingIsNull()
    {
        var stopped = AdminAttentionText.StoppedNote("x");
        AdminAttentionText.Merge("Looked at it, waiting on the firm", null, null, stopped).Should().Be(stopped + AdminAttentionText.Separator + "Looked at it, waiting on the firm");
        AdminAttentionText.Merge(null).Should().BeNull();
        AdminAttentionText.Merge("", new JsonObject()).Should().BeNull();
    }

    [Theory(DisplayName = "049 R3/R5: a merged text never exceeds 500 characters; the notes stay whole and the Signal segment gives way with its count")]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(500)]
    public void Merge_IsCapped_NotesStayWhole(int count)
    {
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "missing")!;
        var stopped = AdminAttentionText.StoppedNote("ebilling.submission.stored_state_mismatch");

        var text = AdminAttentionText.Merge(null, FindingsWith(count), policy, stopped)!;

        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.AdminAttentionMaxLength);
        text.Should().Contain(policy).And.Contain(stopped);
        text.Should().StartWith($"{count} Signals could not be created: ");
        text.Should().Contain("… (+").And.Contain(AdminAttentionText.Separator + policy);
    }

    [Fact(DisplayName = "049 R3/R5: the same inputs always merge to the same text")]
    public void Merge_IsDeterministic()
    {
        var findings = FindingsWith(30);
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "ambiguous");
        var stopped = AdminAttentionText.StoppedNote("c");

        AdminAttentionText.Merge(null, findings, policy, stopped).Should().Be(AdminAttentionText.Merge(null, findings, policy, stopped));
    }

    [Fact(DisplayName = "049 R7: WithoutStopped removes only the stopped segment and keeps every other segment in its stored order")]
    public void WithoutStopped_RemovesOnlyTheStoppedSegment()
    {
        var signals = AdminAttentionText.Build([new AdminAttentionText.Item("BILL-AGE", "sprk_billingevent", "aaaaaaaa-0000-0000-0000-000000000000", "core_record_changed")])!;
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "missing")!;
        var stopped = AdminAttentionText.StoppedNote("decision_owner_refused");
        var sep = AdminAttentionText.Separator;

        AdminAttentionText.WithoutStopped(string.Join(sep, signals, policy, stopped, "typed by an admin")).Should().Be((true, string.Join(sep, signals, policy, "typed by an admin")));
        AdminAttentionText.WithoutStopped(string.Join(sep, "typed first", stopped)).Should().Be((true, "typed first"));
        AdminAttentionText.WithoutStopped(stopped).Should().Be((true, (string?)null), "only a stopped segment: nothing is left");
    }

    [Theory(DisplayName = "049 R7 (must not fire): WithoutStopped reports no change, and returns the text as it is, when there is no stopped segment")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("typed by an admin")]
    [InlineData("1 Signal could not be created: x (BILL on line 1) | System billing policy BILL-SYSTEM-SUBMISSION is missing: system Signals were not created")]
    [InlineData("A note that mentions Processing stopped: in the middle")]
    public void WithoutStopped_NoStoppedSegment_IsNoChange(string? current) =>
        AdminAttentionText.WithoutStopped(current).Should().Be((false, current));

    [Fact(DisplayName = "049 R7: removing the stopped segment is the inverse of merging it in - Merge then WithoutStopped gives the text before")]
    public void WithoutStopped_UndoesMergeOfTheStoppedNote()
    {
        var policy = AdminAttentionText.PolicyNote("BILL-SYSTEM-SUBMISSION", "ambiguous");
        var before = AdminAttentionText.Merge("typed by an admin", null, policy, null)!;
        var stalled = AdminAttentionText.Merge(before, null, null, AdminAttentionText.StoppedNote("stored_state_mismatch"))!;

        AdminAttentionText.WithoutStopped(stalled).Should().Be((true, before));
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
