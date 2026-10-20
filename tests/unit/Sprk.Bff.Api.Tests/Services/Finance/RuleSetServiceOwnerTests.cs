using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 076 follow-up to the #1911 master merge: who owns a billing rule's policy and versions. #2084 made
/// <c>sprk_policy</c> and <c>sprk_policyversion</c> secure-family tables, and the census waives RuleSetService's creates of them
/// (agreed with ontology-r1 2026-10-20) on three conditions these tests pin: (1) RuleSetService never writes a matter onto a policy,
/// (2) the first version and the edit-path version resolve their owner the same way (<c>ResolveOwnerAsync</c> with NO parents, then
/// <c>ApplyTo</c>), (3) a refusal writes nothing. A billing rule hangs off <c>sprk_ruleset</c> and has no matter to name as a parent.
/// </summary>
public sealed class RuleSetServiceOwnerTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly DateOnly Effective = new(2026, 11, 1);

    /// <summary>A team the resolver answers, distinct from the harness default so a version cannot pass by taking a constant.</summary>
    private static readonly Guid AnswerTeam = Guid.NewGuid();

    private static ProposalEdit Edit(string headline) => new(null, null, null, headline, null, null, null, CallerOid);

    private static IReadOnlyList<RecordOwnershipContext> OwnerContexts(Harness h) =>
        h.Ownership.Invocations
            .Where(i => i.Method.Name == nameof(IRecordOwnershipResolver.ResolveOwnerAsync))
            .Select(i => (RecordOwnershipContext)i.Arguments[0])
            .ToList();

    /// <summary>A draft rule set with a source document, as the drafting job reads it.</summary>
    private static Guid RuleSetWithSource(Harness h)
    {
        var id = h.RuleSet();
        h.Row("sprk_ruleset", id)["sprk_sourcedocument"] = new EntityReference("sprk_document", SourceId);
        return id;
    }

    private static DraftedRule Drafted() =>
        new("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", "Billed in tenth-hour increments.", "Time increment", "Flag",
            "Time must be billed in tenth-hour increments.", "Section 4.2");

    /// <summary>Every write to a policy or a version that names a matter, by <c>sprk_matter</c> or a lookup to a matter row.</summary>
    private static IReadOnlyList<string> MatterWrites(IEnumerable<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> writes) =>
        writes
            .Where(w => w.Entity is "sprk_policy" or "sprk_policyversion")
            .SelectMany(w => w.Fields.Where(f =>
                    f.Key.Contains("sprk_matter", StringComparison.OrdinalIgnoreCase)
                    || (f.Value is EntityReference r && r.LogicalName == "sprk_matter"))
                .Select(f => $"{w.Kind} {w.Entity}.{f.Key}"))
            .ToList();

    // ── The edit-path version takes the resolver's owner ────────────────────────────────────────

    [Fact]
    public async Task Edit_OfAVersionInForce_TheNewVersionIsOwnedByTheTeamTheResolverAnswered()
    {
        var h = new Harness { OwnerAnswer = RecordOwnerResolution.Owned(AnswerTeam) };
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true);

        var outcome = await h.Service().EditAsync(set, policy, Edit("Changed"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        var created = h.Rows["sprk_policyversion"].Single(v => v.Id != v1);
        created.GetAttributeValue<EntityReference>("ownerid").Should().NotBeNull("the new version is owned like the first one");
        created.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(AnswerTeam);
    }

    [Fact]
    public async Task Edit_AndTheFirstVersion_AskTheResolverTheSameQuestion_WithNoParents()
    {
        // A billing rule hangs off sprk_ruleset and has no matter: the owner is resolved with NO parents, for the first version
        // (CreateDraftRuleAsync) and for the edit-path version alike. A parent on either would make the two diverge.
        var h = new Harness { OwnerAnswer = RecordOwnerResolution.Owned(AnswerTeam) };
        var drafts = RuleSetWithSource(h);
        await h.Service().AddDraftsAsync(drafts, new[] { Drafted() }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);
        var firstVersionAsk = OwnerContexts(h).Should().ContainSingle().Subject;

        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);
        await h.Service().EditAsync(set, policy, Edit("Changed"), default);
        var editAsk = OwnerContexts(h).Should().HaveCount(2).And.Subject.Last();

        foreach (var ask in new[] { firstVersionAsk, editAsk })
        {
            ask.Parents.Should().BeEmpty();
            ask.HasParent.Should().BeFalse();
            ask.TargetEntityLogicalName.Should().BeNull();
            ask.CallerObjectId.Should().Be(CallerOid);
        }

        editAsk.Should().BeEquivalentTo(firstVersionAsk, "both creates resolve the owner the same way");
        h.Rows["sprk_policy"].Single(p => p.GetAttributeValue<string>("sprk_policycode")!.StartsWith("BRD-", StringComparison.Ordinal))
            .GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(AnswerTeam);
        h.Rows["sprk_policyversion"].Where(v => v.Contains("ownerid")).Select(v => v.GetAttributeValue<EntityReference>("ownerid").Id)
            .Should().OnlyContain(t => t == AnswerTeam);
    }

    // ── A refusal refuses the edit and writes nothing ───────────────────────────────────────────

    [Fact]
    public async Task Edit_OfAVersionInForce_WhenTheOwnerIsRefused_RefusesAndWritesNothing()
    {
        var h = new Harness { OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true);

        var outcome = await h.Service().EditAsync(set, policy, Edit("Changed"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        outcome.OwnerRefusal.Should().NotBeNull();
        h.Writes.Should().BeEmpty("a refused owner writes no version, no pointer and no rule-set row");
        h.Rows["sprk_policyversion"].Should().ContainSingle().Which.Id.Should().Be(v1);
    }

    // ── Condition (1): no matter is ever written onto a policy or a version ─────────────────────

    // Remove this test only together with the routing change that ends the Pending census waiver for RuleSetService.
    [Fact]
    public async Task NoWrittenPolicyOrVersionRowCarriesAMatter_OnCreateOrUpdate()
    {
        var h = new Harness();
        var drafts = RuleSetWithSource(h);
        await h.Service().AddDraftsAsync(drafts, new[] { Drafted() }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);

        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (inForce, _) = h.Rule(set, code: "BILL-A", state: ProposalState.Accepted, current: true);
        await h.Service().EditAsync(set, inForce, Edit("Changed"), default);          // creates a version

        var draftSet = h.RuleSet();
        var (draft, _) = h.Rule(draftSet, code: "BILL-B", state: ProposalState.Proposed);
        await h.Service().EditAsync(draftSet, draft, Edit("Changed in place"), default); // updates a version
        await h.Service().DecideAsync(draftSet, draft, accept: true, h.WorkingToken(draft), default); // updates a version
        var published = await h.Service().PublishAsync(draftSet, Effective, default); // the publish path: pointer and scope writes
        published.Kind.Should().Be(RuleSetOutcomeKind.Done, "the publish must really run, or its writes are not under this check");

        h.Writes.Should().Contain(w => w.Kind == Write.Create && w.Entity == "sprk_policy");
        h.Writes.Should().Contain(w => w.Kind == Write.Create && w.Entity == "sprk_policyversion");
        h.Writes.Should().Contain(w => w.Kind != Write.Create && w.Entity == "sprk_policyversion");
        MatterWrites(h.Writes).Should().BeEmpty("a billing rule is never filed under a matter, so it can never be filed under a Secure one");
    }

    [Fact]
    public void TheMatterWriteCheck_FiresOnAPlantedMatterAndNotOnAnOrdinaryRow()
    {
        var policy = Guid.NewGuid();
        var planted = new (Write, string, Guid, Dictionary<string, object>)[]
        {
            (Write.Create, "sprk_policy", policy, new() { ["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()) }),
            (Write.Update, "sprk_policyversion", Guid.NewGuid(), new() { ["sprk_regarding"] = new EntityReference("sprk_matter", Guid.NewGuid()) }),
        };
        var ordinary = new (Write, string, Guid, Dictionary<string, object>)[]
        {
            (Write.Create, "sprk_policy", policy, new() { ["sprk_ruleset"] = new EntityReference("sprk_ruleset", Guid.NewGuid()), ["sprk_name"] = "Rule" }),
            (Write.Create, "sprk_ruleset", Guid.NewGuid(), new() { ["sprk_matter"] = "not a policy row" }),
        };

        MatterWrites(planted).Should().HaveCount(2, "must-fire: a matter on a policy or a version is caught");
        MatterWrites(ordinary).Should().BeEmpty("must-not-fire: ordinary rule writes and other tables are ignored");
    }
}
