using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 102a (owner option 101(b)): the authorable-column allow-list. Covers what the compiler records as read
/// (<see cref="CompiledPredicate.ReadColumns"/>), what <see cref="PolicyVersionValidator.ValidateForAuthoring"/> refuses, the template
/// fields it returns, and that the live Ontology policies still pass.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The real-schema pairing (every allow-listed column exists and is not
/// field-secured; the live current versions pass) is in <c>tests/integration/seam/Signals/SignalPredicateTests.cs</c>.
/// </remarks>
[Trait("status", "new")]
public class AuthorableColumnsTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);

    private static PolicyVersionValidator Validator() =>
        new(new RuleBodySchemaValidator(), new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt)),
            NullLogger<PolicyVersionValidator>.Instance);

    private static PredicateCompiler Compiler() => new(new RuleBodySchemaValidator(), new FakeTimeProvider(RunAt));

    // The five live Ontology policies' CURRENT versions, read read-only from spaarkedev1 on 2026-10-16 (sprk_policyversion rows
    // 0000...6111 to 6114 and e51906ea-62c2-f111-a05a-7c1e520a989f). The live seam test re-reads them from Dataverse.
    public static TheoryData<string, string, string> LivePolicies => new()
    {
        {
            "POL-COMMIT-BUDGET",
            """{"type":"Existence","subject":"sprk_matter","when":{},"all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_triagecategory":["8b62dd84-1fbc-f111-aaaf-3833c5e9614d","8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],"sprk_receiveddate":{">=":"now-30d"},"sprk_reviewoutcome":{"<>":100000003}}},{"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}}]}""",
            "A communication on this matter was classified as a fee or scope change in the last 30 days, and no budget revision was recorded in that time."
        },
        {
            "POL-TASK-OVERDUE",
            """{"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","statuscode":659490001,"sprk_duedate":{"<=":"now-1d"}},"all":[]}""",
            "This task is open and its due date has passed."
        },
        {
            "POL-TASK-DUESOON",
            """{"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","statuscode":659490001,"sprk_duedate":{">=":"now","<=":"now+3d"}},"all":[]}""",
            "This task is open and is coming due."
        },
        {
            "POL-WA-RESPONSE",
            """{"type":"Existence","subject":"sprk_workassignment","when":{"statecode":0,"sprk_responseduedate":{"<=":"now-1d"},"sprk_respondedon":{"null":true}},"all":[]}""",
            "This work assignment is active, its response date has passed and no response is recorded."
        },
        {
            "POL-TODO-OVERDUE",
            """{"type":"Existence","subject":"sprk_todo","when":{"statuscode":[1,659490001],"sprk_duedate":{"<=":"now-1d"}},"all":[]}""",
            "This To Do is open and its due date has passed."
        },
    };

    [Theory]
    [MemberData(nameof(LivePolicies))]
    public void EveryLiveOntologyPolicy_PassesValidateForAuthoring(string code, string body, string template)
    {
        var result = Validator().ValidateForAuthoring("Existence", body, template);

        result.Validation.IsValid.Should().BeTrue($"{code} is live; the allow-list must not refuse it: {string.Join("; ", result.Validation.Errors)}");
    }

    [Theory]
    [InlineData("do-lane.policies.json", 4)]
    [InlineData("know-promotion.policies.json", 1)]
    public void EveryPolicyDefinition_PassesValidateForAuthoring(string file, int count)
    {
        // config/*.policies.json is what scripts/Apply-DoLanePolicies.ps1 writes; their current versions must stay authorable.
        // know-promotion.policies.json holds POL-MATTER-NOBUDGET (task 063), whose 'when' reads sprk_matter.createdon.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "config", file))) root = root.Parent;
        root.Should().NotBeNull();
        var policies = JsonNode.Parse(File.ReadAllText(Path.Combine(root!.FullName, "config", file)))!["policies"]!.AsArray();

        policies.Should().HaveCount(count);
        foreach (var policy in policies.Select(p => p!.AsObject()))
        {
            var current = policy["versions"]!.AsArray().Select(v => v!.AsObject()).Single(v => (int)v["versionNumber"]! == (int)policy["currentVersion"]!);
            var result = Validator().ValidateForAuthoring((string)current["ruleType"]!, current["ruleBody"]!.ToJsonString(), (string)current["messageTemplate"]!);
            result.Validation.IsValid.Should().BeTrue($"{policy["code"]}: {string.Join("; ", result.Validation.Errors)}");
        }
    }

    // ── What the compiler records as read ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadColumns_RecordsWhenExistsNotExistsFiltersAndJoinPaths()
    {
        var compiled = Compiler().Compile(
            """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_receiveddate":{">=":"now-30d"}}},{"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}}]}""");

        compiled.ReadColumns.Should().BeEquivalentTo(new[]
        {
            ("sprk_matter", "statecode"),
            ("sprk_communication", "sprk_receiveddate"),
            ("sprk_communication", "sprk_regardingmatter"),
            ("sprk_budgetrevision", "sprk_revisedon"),
            ("sprk_budgetrevision", "sprk_matter"),
        });
    }

    [Fact]
    public void ReadColumns_RecordsTheThresholdField()
    {
        var compiled = Compiler().Compile(
            """{"type":"Threshold","subject":"sprk_matter","when":{"statecode":0},"threshold":{"field":"sprk_budgetutilizationpercent","operator":">=","value":85}}""",
            null, RuleType.Threshold);

        compiled.ReadColumns.Should().BeEquivalentTo(new[] { ("sprk_matter", "statecode"), ("sprk_matter", "sprk_budgetutilizationpercent") });
    }

    // ── The refusal ─────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> UnlistedBodies => new()
    {
        { "when", """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0,"sprk_secretnotes":"x"},"all":[]}""" },
        { "exists filter", """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_body":"x"}}]}""" },
        { "notExists filter", """{"type":"Existence","subject":"sprk_matter","all":[{"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_amount":{">":0}}}]}""" },
    };

    [Theory]
    [MemberData(nameof(UnlistedBodies))]
    public void AnUnlistedColumn_AnywhereInTheBody_IsRefused_ColumnNotAuthorable(string where, string body)
    {
        var validator = Validator();

        var result = validator.ValidateForAuthoring("Existence", body, null);

        result.Validation.IsValid.Should().BeFalse($"an unlisted column in the {where} must be refused");
        result.Validation.Reason.Should().Be(OntologyWriterFailureReason.ColumnNotAuthorable);
        result.Validation.Errors.Single().Should().Contain("may not use");

        // The evaluator's gate and ValidateForSave are unchanged: the same body still compiles there.
        validator.ValidateForSave("Existence", body, null).IsValid.Should().BeTrue("the allow-list is an authoring rule only");
        validator.TryPrepareForEvaluation(new PolicyVersionSnapshot(Guid.NewGuid(), "POL-X", "Existence", body, null), null, out _)
            .Should().BeTrue("a version written outside the BFF still evaluates as before");
    }

    [Fact]
    public void TheAllowListIsCheckedBeforeTheTemplate_AndTheEligibleFieldsLeaveOutTheUnlistedColumn()
    {
        var result = Validator().ValidateForAuthoring("Existence",
            """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0,"sprk_secretnotes":"x"},"all":[]}""",
            "Notes {{sprk_secretnotes}}.");

        result.Validation.Reason.Should().Be(OntologyWriterFailureReason.ColumnNotAuthorable);
        result.TemplateEligibleFields.Should().Equal("statecode");
    }

    [Fact]
    public void TheEligibleFields_AreTheCompiledOnes_WhenEveryReadIsAllowListed_EvenWhenTheTemplateIsRefused()
    {
        const string body =
            """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_triagecategory":["8b62dd84-1fbc-f111-aaaf-3833c5e9614d"],"sprk_receiveddate":{">=":"now-30d"}}}]}""";

        var ok = Validator().ValidateForAuthoring("Existence", body, "State {{statecode}}, category {{sprk_triagecategory}}.");
        var refused = Validator().ValidateForAuthoring("Existence", body, "Received {{sprk_receiveddate}}.");

        ok.Validation.IsValid.Should().BeTrue(string.Join("; ", ok.Validation.Errors));
        ok.TemplateEligibleFields.Should().Equal("sprk_triagecategory", "statecode");
        refused.Validation.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        refused.TemplateEligibleFields.Should().Equal("sprk_triagecategory", "statecode");
    }

    [Fact]
    public void ABodyThatDoesNotCompile_HasNoEligibleFields()
    {
        var result = Validator().ValidateForAuthoring("Existence", """{"type":"Existence","subject":"sprk_secret","when":{"statecode":0},"all":[]}""", null);

        result.Validation.Reason.Should().Be(OntologyWriterFailureReason.CompileRefused);
        result.TemplateEligibleFields.Should().BeEmpty();
    }

    [Fact]
    public void APredicateWithNoRecordedReads_IsRefused_AndHasNoEligibleFields_FailClosed()
    {
        var compiled = Compiler().Compile("""{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""");
        PolicyVersionValidator.CheckAuthorableColumns(compiled).Should().BeNull("every read is allow-listed");
        PolicyVersionValidator.AuthorableTemplateFields(compiled).Should().Equal("statecode");

        var unrecorded = compiled with { ReadColumns = null };

        var refusal = PolicyVersionValidator.CheckAuthorableColumns(unrecorded);
        refusal.Should().NotBeNull("reads that were not recorded cannot be shown to be on the list");
        refusal!.IsValid.Should().BeFalse();
        refusal.Reason.Should().Be(OntologyWriterFailureReason.ColumnNotAuthorable);
        PolicyVersionValidator.AuthorableTemplateFields(unrecorded).Should().BeEmpty();
    }

    // ── The seed ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSeed_HoldsTheCompilersTypedComparablesAndJoinPaths_OnEvaluatorReadableTablesOnly()
    {
        var allow = PolicyVersionValidator.AuthorableColumns;

        allow.Should().Contain(PredicateCompiler.DateOnlyColumns);
        allow.Should().Contain(PredicateCompiler.ThresholdColumns);
        allow.Should().Contain(PredicateCompiler.VerifiedJoins.Select(kv => (kv.Key.Related, kv.Value)));
        allow.Select(c => c.Entity).Should().OnlyContain(e => PredicateCompiler.EvaluatorGlobalReadableEntities.Contains(e));
        allow.Should().NotContain(c => c.Column == "modifiedon", "FR-07: sprk_budget.modifiedon is refused, and no other modifiedon is authorable");
    }
}
