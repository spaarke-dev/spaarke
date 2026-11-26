using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// The retry compensation of the drafting job (ebilling-r1 task 075, review finding F1): <c>RemoveUntouchedDraftsAsync</c>, and the way
/// <c>AddDraftsAsync</c> treats the rules that survive it.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): a run that died midway is retried and the model is asked again (its output is not deterministic), so the
/// earlier run's drafts must go first or two sets remain. The guarantee that matters more is the negative one: a draft that a person
/// worked on, or that is not this job's, is never deleted. Each criterion of "untouched" has its own case here, isolated from the others
/// where the fake store allows it. Reuses the 076 harness (a fake store that evaluates the real queries and now models <c>createdby</c>,
/// <c>createdon</c>, <c>modifiedon</c> and delete faults).
/// </remarks>
public sealed class RuleSetServiceCompensationTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid SomeoneElse = Guid.NewGuid();

    private const string Q1 = "Time must be billed in tenth-hour increments.";
    private const string Q2 = "No timekeeper may bill more than ten hours a day.";

    private static IReadOnlyList<NotConvertedClause> None => Array.Empty<NotConvertedClause>();

    private static DraftedRule Increment(string quote = Q1) =>
        Drafted("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", "Flag", quote, "Section 4.2"); // task 073: TimeIncrement may not reject (FR-26)

    private static DraftedRule Cap(int hours, string quote = Q2) =>
        Drafted("DailyHoursCap", $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""", "Flag", quote, "Section 5");

    private static DraftedRule Drafted(string type, string body, string consequence, string quote, string? location)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(type));
        return new DraftedRule(type, body, template, headline, consequence, quote, location);
    }

    /// <summary>A run that stored its rules and died before it wrote the completion marker.</summary>
    /// <remarks>Task 116: on a Draft version of a Billing Procedure (or a further version of <paramref name="procedure"/>); the version's memo
    /// and Yes/No marker are removed again, as a run that died before them left them.</remarks>
    private static async Task<Harness.Drafting> EarlierRun(Harness h, params DraftedRule[] rules) => await EarlierRun(h, null, rules);

    private static async Task<Harness.Drafting> EarlierRun(Harness h, Guid? procedure, params DraftedRule[] rules)
    {
        var d = procedure is { } p ? h.DraftVersion(procedure: p, number: 2) : h.DraftVersion();
        var outcome = await h.Service().AddDraftsAsync(d.Version, rules, None, CallerOid, CancellationToken.None);
        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row(ProcedureEntity.VersionEntity, d.Version).Attributes.Remove(RuleSetService.VersionDraftedColumn);
        h.Row(ProcedureEntity.VersionEntity, d.Version).Attributes.Remove(RuleSetService.VersionNotConvertedListColumn);
        h.Writes.Clear();
        return d;
    }

    private static (Guid Policy, Guid Version) Find(Harness h, string quote)
    {
        var version = h.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == quote);
        return (version.GetAttributeValue<EntityReference>("sprk_policy").Id, version.Id);
    }

    private static Exception NotFound() => new InvalidOperationException(
        "Failed to delete sprk_policyversion record",
        new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")));

    // ── must fire ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compensate_RemovesEveryUntouchedDraftOfTheEarlierRun_EachVersionBeforeItsPolicy()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value.Should().Be(new DraftsCompensated(Removed: 2, Kept: 0));
        h.Rows["sprk_policy"].Should().BeEmpty();
        h.Rows["sprk_policyversion"].Should().BeEmpty();
        h.DeleteAttempts.Select(x => x.Entity).Should().Equal("sprk_policyversion", "sprk_policy", "sprk_policyversion", "sprk_policy");
    }

    [Fact]
    public async Task Compensate_ThenDraftAgain_WithDifferentModelOutput_LeavesExactlyOneSet()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        var firstRunIds = h.Rows["sprk_policy"].Select(p => p.Id).ToList();
        var service = h.Service();

        (await service.RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var redraft = await service.AddDraftsAsync(d.Version, new[] { Increment(), Cap(12) }, None, CallerOid, CancellationToken.None);

        redraft.Value!.Stored.Should().Be(2);
        h.Rows["sprk_policy"].Should().HaveCount(2, "one set, not the first run's rules beside the second's");
        h.Rows["sprk_policy"].Select(p => p.Id).Should().NotIntersectWith(firstRunIds);
        h.Rows["sprk_policyversion"].Select(v => v.GetAttributeValue<string>("sprk_rulebody")).Should().Contain(b => b!.Contains("\"maxHoursPerDay\":12"))
            .And.NotContain(b => b!.Contains("\"maxHoursPerDay\":10"));
        h.Row(ProcedureEntity.VersionEntity, d.Version).GetAttributeValue<bool>(RuleSetService.VersionDraftedColumn).Should().BeTrue("the marker is written last");
    }

    [Fact]
    public async Task Compensate_APolicyWithNoVersion_LeftByARunThatDied_IsRemoved()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment());
        h.Rows["sprk_policyversion"].Clear(); // the version create failed and the policy could not be removed either

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Value.Should().Be(new DraftsCompensated(1, 0));
        h.Rows["sprk_policy"].Should().BeEmpty();
    }

    [Fact]
    public async Task Compensate_ARowThatIsAlreadyGone_CountsAsDeleted()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        var gone = Find(h, Q1).Version;
        h.OnDelete = (entity, rowId) => rowId == gone ? NotFound() : null;

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Removed.Should().Be(2);
        h.Rows["sprk_policy"].Should().BeEmpty("the policy of the vanished version is still deleted");
    }

    [Fact]
    public async Task Compensate_ADeleteFault_IsAFailure_StopsThere_AndTheNextPassFinishesTheJob()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        var (firstPolicy, _) = Find(h, Q1);
        h.OnDelete = (entity, rowId) => rowId == firstPolicy ? new InvalidOperationException("simulated Dataverse 429") : null;
        var service = h.Service();

        var failed = await service.RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.StatusCode.Should().Be(500);
        failed.ErrorCode.Should().Be(RuleSetService.CodeWriteFailed);
        h.WritesOf("sprk_policy", "sprk_policyversion").Should().NotContain(w => w.Kind == Write.Create, "nothing is drafted by a failed compensation");
        h.Rows["sprk_policy"].Should().NotBeEmpty();

        // The next delivery compensates again and finishes, including the policy whose version already went.
        h.OnDelete = null;
        var again = await service.RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);
        again.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Rows["sprk_policy"].Should().BeEmpty();
        h.Rows["sprk_policyversion"].Should().BeEmpty();
    }

    [Fact]
    public async Task Compensate_WhenTheIdentityCannotBeResolved_FailsClosed_AndDeletesNothing()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment());
        h.FailIdentity = true;

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(500);
        h.DeleteAttempts.Should().BeEmpty();
        h.Rows["sprk_policy"].Should().ContainSingle();
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> Spoilers => new()
    {
        "admin edited the version (modifiedon moved)",
        "admin edited the policy (modifiedon moved)",
        "accepted (status and modifiedon moved)",
        "status Accepted alone",
        "status Rejected alone",
        "turned On",
        "enabled unknown",
        "has a current version",
        "version was in force (inforceto set)",
        "version created by another user",
        "policy created by another user",
        "policy code is not a drafted code",
        "a second version someone else added",
        "task 116: version under another Procedure Version",
        "task 116: version under no Procedure Version",
        "task 116: version publish state not Draft",
    };

    private static void Spoil(Harness h, string how, Guid policy, Guid version)
    {
        switch (how)
        {
            case "admin edited the version (modifiedon moved)":
                h.ConcurrentWrite("sprk_policyversion", version, new Dictionary<string, object> { ["sprk_shortheadline"] = "Edited by an admin" });
                break;
            case "admin edited the policy (modifiedon moved)":
                h.ConcurrentWrite("sprk_policy", policy, new Dictionary<string, object> { ["sprk_name"] = "Renamed by an admin" });
                break;
            case "accepted (status and modifiedon moved)":
                h.ConcurrentWrite("sprk_policyversion", version, new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted) });
                break;
            case "status Accepted alone":
                h.Row("sprk_policyversion", version)["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted);
                break;
            case "status Rejected alone":
                h.Row("sprk_policyversion", version)["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Rejected);
                break;
            case "turned On":
                h.Row("sprk_policy", policy)["sprk_enabled"] = true;
                break;
            case "enabled unknown":
                h.Row("sprk_policy", policy).Attributes.Remove("sprk_enabled");
                break;
            case "has a current version":
                h.Row("sprk_policy", policy)["sprk_currentversion"] = new EntityReference("sprk_policyversion", version);
                break;
            case "version was in force (inforceto set)":
                h.Row("sprk_policyversion", version)["sprk_inforceto"] = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
                break;
            case "version created by another user":
                h.SetCreatedBy(version, SomeoneElse);
                break;
            case "policy created by another user":
                h.SetCreatedBy(policy, SomeoneElse);
                break;
            case "policy code is not a drafted code":
                h.Row("sprk_policy", policy)["sprk_policycode"] = "BILL-A";
                break;
            case "a second version someone else added":
                h.AddVersion(policy, 2, ProposalState.Proposed);
                break;
            case "task 116: version under another Procedure Version":
                h.Row("sprk_policyversion", version)[RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, Guid.NewGuid());
                break;
            case "task 116: version under no Procedure Version":
                h.Row("sprk_policyversion", version).Attributes.Remove(RuleSetService.ProcedureVersionColumn);
                break;
            case "task 116: version publish state not Draft":
                h.Row("sprk_policyversion", version)[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStateDraft + 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }
    }

    [Theory]
    [MemberData(nameof(Spoilers))]
    public async Task Compensate_NeverDeletesADraftThatFailsAnyOneCriterion_ButStillClearsTheUntouchedOnes(string how)
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        var (policy, version) = Find(h, Q1);
        var (otherPolicy, otherVersion) = Find(h, Q2);
        Spoil(h, how, policy, version);

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value.Should().Be(new DraftsCompensated(Removed: 1, Kept: 1));
        h.Rows["sprk_policy"].Select(p => p.Id).Should().Equal(policy);
        h.Rows["sprk_policyversion"].Select(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id).Should().OnlyContain(p => p == policy);
        h.Rows["sprk_policyversion"].Select(v => v.Id).Should().Contain(version);
        h.DeleteAttempts.Select(x => x.Id).Should().BeEquivalentTo(new[] { otherPolicy, otherVersion }, "only the untouched rule was ever deleted");
    }

    [Fact]
    public async Task Compensate_NeverTouchesAnotherRuleSet_NorAnOntologyPolicyInThisOne()
    {
        var h = new Harness();
        var other = await EarlierRun(h, Increment("A clause of another rule set."));
        var d = await EarlierRun(h, Cap(10));
        // Even a draft of another rule set that carries this set's code prefix (the lookup, not the code, decides) stays.
        var lookalike = h.Rows["sprk_policy"].Single(p => p.GetAttributeValue<EntityReference>("sprk_ruleset").Id == other.RuleSet);
        lookalike["sprk_policycode"] = $"BRD-{d.RuleSet:N}-050";
        var ontologyPolicy = h.Rule(d.RuleSet, code: $"BRD-{d.RuleSet:N}-009", domain: null).Policy;
        h.SetCreatedBy(ontologyPolicy, h.Identity);

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Value!.Removed.Should().Be(1);
        h.Rows["sprk_policy"].Select(p => p.Id).Should().Contain(ontologyPolicy, "an Ontology-domain policy is not a billing rule and is never read as one");
        h.Rows["sprk_policy"].Should().Contain(p => p.GetAttributeValue<EntityReference>("sprk_ruleset").Id == other.RuleSet, "another rule set's drafts stay");
        h.DeleteAttempts.Should().NotContain(x => x.Id == ontologyPolicy);
        h.DeleteAttempts.Should().NotContain(x => x.Id == lookalike.Id);
    }

    [Fact]
    public async Task Compensate_AVersionWithTheCompletionMarker_DeletesNothing()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        h.Row(ProcedureEntity.VersionEntity, d.Version)[RuleSetService.VersionDraftedColumn] = true;

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeAlreadyDrafted);
        h.DeleteAttempts.Should().BeEmpty();
        h.Rows["sprk_policy"].Should().HaveCount(2);
    }

    [Theory]
    [InlineData("unknown", 404, "procedure.version_not_found")]
    [InlineData("retired set", 409, "ruleset.retired")]
    [InlineData("not a Draft", 409, "procedure_version.not_draft")]
    [InlineData("Ontology", 400, "procedure.not_billing")]
    public async Task Compensate_ARefusedTarget_IsRefused_AndDeletesNothing(string scenario, int status, string code)
    {
        var h = new Harness();
        var version = scenario switch
        {
            "unknown" => Guid.NewGuid(),
            "retired set" => h.DraftVersion(setStatus: RuleSetService.RuleSetRetired).Version,
            "not a Draft" => h.DraftVersion(versionStatus: ProcedureEntity.VersionStatusActive).Version,
            _ => h.DraftVersion(area: RuleDomains.PolicyDomainOntology).Version,
        };

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(version, CancellationToken.None);

        outcome.StatusCode.Should().Be(status);
        outcome.ErrorCode.Should().Be(code);
        h.DeleteAttempts.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 116: compensation is per version — another version's untouched draft and a copy-forward draft (a policy with a current version) stay")]
    public async Task Compensate_KeepsAnotherVersionsDrafts_AndCopyForwardDrafts()
    {
        var h = new Harness();
        var v1 = await EarlierRun(h, Increment("A clause of version 1."));
        h.Row(ProcedureEntity.VersionEntity, v1.Version)[ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusActive);
        var v2 = await EarlierRun(h, v1.Procedure, Cap(10));
        var v1Policy = Find(h, "A clause of version 1.").Policy;
        // 081's copy-forward: a published rule whose new working version is a Draft under version 2.
        var (published, publishedV1) = h.Rule(v1.RuleSet, code: $"BRD-{v1.RuleSet:N}-077", current: true);
        h.SetCreatedBy(published, h.Identity);
        h.SetCreatedBy(publishedV1, h.Identity);
        var copy = h.AddVersion(published, 2, ProposalState.Proposed);
        h.SetCreatedBy(copy, h.Identity);
        h.Row("sprk_policyversion", copy)[RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, v2.Version);
        h.Row("sprk_policyversion", copy)[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStateDraft);

        var outcome = await h.Service().RemoveUntouchedDraftsAsync(v2.Version, CancellationToken.None);

        outcome.Value.Should().Be(new DraftsCompensated(Removed: 1, Kept: 2));
        h.Rows["sprk_policy"].Select(p => p.Id).Should().BeEquivalentTo(new[] { v1Policy, published });
        h.Rows["sprk_policyversion"].Select(v => v.Id).Should().Contain(copy);
        h.DeleteAttempts.Should().NotContain(x => x.Id == v1Policy || x.Id == published || x.Id == copy);
    }

    // ── what the rules that survive do to the next draft ────────────────────────────────────────

    [Fact]
    public async Task AddDrafts_AfterTheCompensation_DoesNotStoreASecondRuleForAClauseAKeptRuleCovers_ItListsIt()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));
        var (policy, version) = Find(h, Q1);
        h.ConcurrentWrite("sprk_policyversion", version, new Dictionary<string, object> { ["sprk_shortheadline"] = "Edited by an admin" });
        var service = h.Service();
        (await service.RemoveUntouchedDraftsAsync(d.Version, CancellationToken.None)).Value.Should().Be(new DraftsCompensated(1, 1));

        // The model words the first clause differently this time (case and spacing) and caps the second at twelve hours.
        var redraft = await service.AddDraftsAsync(
            d.Version, new[] { Cap(9, "TIME must be billed   in tenth-hour increments."), Cap(12) }, None, CallerOid, CancellationToken.None);

        redraft.Value!.Stored.Should().Be(1, "only the clause nothing covers is drafted again");
        h.Rows["sprk_policy"].Should().HaveCount(2);
        h.Rows["sprk_policy"].Select(p => p.Id).Should().Contain(policy);
        var listed = redraft.Value.NotConverted.Should().ContainSingle().Subject;
        listed.Reason.Should().Contain("covers this clause");
        listed.Text.Should().Be("TIME must be billed   in tenth-hour increments.");
    }

    [Fact]
    public async Task AddDrafts_AnIdenticalRuleThatAlreadyExists_IsStillSkippedQuietly_NotListed()
    {
        var h = new Harness();
        var d = await EarlierRun(h, Increment(), Cap(10));

        var redraft = await h.Service().AddDraftsAsync(d.Version, new[] { Increment(), Cap(10) }, None, CallerOid, CancellationToken.None);

        redraft.Value!.Stored.Should().Be(0);
        redraft.Value.AlreadyPresent.Should().Be(2);
        redraft.Value.NotConverted.Should().BeEmpty();
        h.Rows["sprk_policy"].Should().HaveCount(2);
    }
}
