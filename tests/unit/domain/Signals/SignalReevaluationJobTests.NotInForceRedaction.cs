using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// #3091 (DI-172, ontology task 217): the nightly stored-evidence redaction of the policies that are not in force (task 145) reads Signals
/// only for the Off or Retired policies that still have a Signal whose stored evidence carries detail. Before: one <c>sprk_signal</c> read
/// per Off or Retired Ontology policy, every status, every night, though a Signal whose detail is gone never changes again (the redaction is
/// monotonic). Now one paged read of the Signals whose stored evidence still holds a <c>text</c> or <c>source</c> property, joined to a
/// not-in-force Ontology policy (plus the same read of such Signals with no policy), names the policies, and the unchanged per-policy sweep
/// runs for those only. Which evidence is redacted, and how, is unchanged (the controls below).
/// These run the REAL job, closure service, suppression service and writer over the job harness; the fake answers the LIKE filter and the
/// join as Dataverse does.
/// </summary>
public partial class SignalReevaluationJobTests
{
    /// <summary>The stored evidence of a line whose detail was already cleared: what the redaction writes for
    /// <see cref="StoredEvidenceQuotingTheWitness"/> (its <c>text</c> and <c>source</c> removed, every other property as stored).</summary>
    private static string EvidenceAlreadyRedacted() =>
        $"[{{\"kind\":\"communication\",\"ref\":\"sprk_communication:{Lower(CommunicationId)}\",\"tier\":\"Fact\","
        + "\"asOf\":\"2026-10-05T09:15:00Z\",\"clause\":\"all[0]\"}]";

    /// <summary>A Resolved matter Signal of <paramref name="code"/> (so the Off/Retired close leaves it alone) with the given stored
    /// evidence, placed in matter M's family. <paramref name="policyId"/> null: a Signal whose policy lookup is empty.</summary>
    private static Guid SeedEvidenceSignal(Harness h, string code, Guid? policyId, string evidence, int episode = 1)
    {
        var id = h.Org.SeedSignal(Key(code, "sprk_matter", MatterId, episode), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: MatterId, policyId: policyId,
            versionId: policyId is null ? null : h.Sysadmin.CurrentVersionOf(code));
        h.Org.Rows[id]["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        h.Org.Rows[id]["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        h.Org.Rows[id]["sprk_evidencerefs"] = evidence;
        return id;
    }

    /// <summary>The dedupe-key prefix of every per-policy Signal read (the sweep's <c>ReadPolicySignalsAsync</c>), in order.</summary>
    private static List<string> ObservePerPolicySignalReads(Harness h, Func<QueryExpression, Exception?>? fault = null)
    {
        var prefixes = new List<string>();
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal" && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_dedupekey") is { } key)
                prefixes.Add((string)key.Values[0]);
            return fault?.Invoke(q);
        };
        return prefixes;
    }

    private static IEnumerable<(Guid Id, string Evidence)> EvidenceWrites(Harness h) =>
        h.Org.ConditionalUpdates.Where(u => u.Fields.ContainsKey("sprk_evidencerefs")).Select(u => (u.Id, (string)u.Fields["sprk_evidencerefs"]));

    [Theory]
    [InlineData(500)]
    public async Task Run_ManyOffPoliciesAlreadyRedacted_FewWithDetail_SweepReadsDoNotGrowWithTheOffPolicies(int offPolicies)
    {
        // Every Off policy has a Signal whose detail is already cleared; 3 of them also have one still quoting a witness that has since
        // moved behind secure X's wall.
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000217");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var policies = AddOffPolicies(h, offPolicies);
        var alreadyRedacted = policies.Select(p => SeedEvidenceSignal(h, p.Code, p.Id, EvidenceAlreadyRedacted())).ToList();
        var withDetail = new[] { policies[3], policies[offPolicies / 2], policies[^1] };
        var toRedact = withDetail.Select(p => SeedEvidenceSignal(h, p.Code, p.Id, StoredEvidenceQuotingTheWitness(), episode: 2)).ToList();
        RefileTheWitnessUnder(h, secureProject);
        var versions = alreadyRedacted.ToDictionary(id => id, h.Org.VersionOf);
        var perPolicy = ObservePerPolicySignalReads(h);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before: one per-policy Signal read per Off policy (500). Now: one page of each stored-detail read, plus one per-policy read for
        // each policy that still has detail (3), whatever the number of Off policies.
        perPolicy.Should().HaveCount(withDetail.Length, "one per-policy read per policy that still has stored detail, not per Off policy");
        perPolicy.Should().BeEquivalentTo(withDetail.Select(p => SignalWriter.DedupeKeyPolicyPrefix(p.Code)),
            "only the policies that still have stored detail are read, each once");
        h.Sysadmin.StoredDetailReads.Should().Equal(new[] { "joined", "no policy" }, "one page of each stored-detail read");
        result.ResultJson.Should().Contain("\"evidenceRedacted\":3");
        EvidenceWrites(h).Select(w => w.Id).Should().BeEquivalentTo(toRedact, "exactly the Signals with detail outside their family");
        EvidenceWrites(h).Should().OnlyContain(w => w.Evidence == EvidenceAlreadyRedacted(), "redacted exactly as before: text and source removed");
        alreadyRedacted.Should().OnlyContain(id => h.Org.VersionOf(id) == versions[id], "a Signal with nothing left to clear is never written (D-61)");
    }

    [Fact]
    public async Task Run_AnOffPolicyWithDetail_IsRedactedAsBefore_AlreadyRedactedIsNotRewritten_AndTheRunSucceeds()
    {
        // Must-not-fire controls in one run: an Off policy's Signal with detail outside its family (redacted), the same policy's Signal
        // already redacted (untouched), a Retired policy's Signal with only a source (redacted), and an Off policy with nothing left to clear
        // (never read).
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000218");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var off = AddOffPolicies(h, 2);
        var redacts = SeedEvidenceSignal(h, off[0].Code, off[0].Id, StoredEvidenceQuotingTheWitness());
        var untouched = SeedEvidenceSignal(h, off[0].Code, off[0].Id, EvidenceAlreadyRedacted(), episode: 2);
        var neverRead = SeedEvidenceSignal(h, off[1].Code, off[1].Id, EvidenceAlreadyRedacted());
        h.Sysadmin.AddPolicy("POL-RETIRED", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyState("POL-RETIRED", enabled: true, statecode: 1);
        var sourceOnly = SeedEvidenceSignal(h, "POL-RETIRED", h.Sysadmin.PolicyIdOf("POL-RETIRED"),
            $"[{{\"kind\":\"communication\",\"ref\":\"sprk_communication:{Lower(CommunicationId)}\",\"tier\":\"Fact\",\"source\":\"partner@firm.example\"}}]");
        RefileTheWitnessUnder(h, secureProject);
        var versions = new[] { untouched, neverRead }.ToDictionary(id => id, h.Org.VersionOf);
        var perPolicy = ObservePerPolicySignalReads(h);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"evidenceRedacted\":2").And.Contain("\"policyFailures\":0");
        EvidenceWrites(h).Should().BeEquivalentTo(new[]
        {
            (redacts, EvidenceAlreadyRedacted()),
            (sourceOnly, $"[{{\"kind\":\"communication\",\"ref\":\"sprk_communication:{Lower(CommunicationId)}\",\"tier\":\"Fact\"}}]"),
        });
        h.Org.Rows[redacts].GetAttributeValue<string>("sprk_evidencerefs").Should().NotContain("Rate increase").And.NotContain("partner@firm.example");
        versions.Should().OnlyContain(v => h.Org.VersionOf(v.Key) == v.Value, "nothing to clear: never written");
        perPolicy.Should().BeEquivalentTo(new[] { SignalWriter.DedupeKeyPolicyPrefix(off[0].Code), SignalWriter.DedupeKeyPolicyPrefix("POL-RETIRED") });
    }

    [Fact]
    public async Task Run_ASignalWithNoPolicyLookup_KeyedToAnOffPolicy_IsStillRedacted_Once()
    {
        // The per-policy sweep reads by dedupe-key prefix, so it also reached a Signal of the Off policy whose lookup is empty but which the
        // deleted-policy sweep does not call an orphan (created at or after the policy, or a date unknown). The no-policy stored-detail read
        // keeps it reached.
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000219");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var off = AddOffPolicies(h, 3);
        var noLookup = SeedEvidenceSignal(h, off[1].Code, policyId: null, StoredEvidenceQuotingTheWitness());
        RefileTheWitnessUnder(h, secureProject);
        var perPolicy = ObservePerPolicySignalReads(h);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        EvidenceWrites(h).Should().ContainSingle().Which.Should().Be((noLookup, EvidenceAlreadyRedacted()));
        perPolicy.Should().Equal(new[] { SignalWriter.DedupeKeyPolicyPrefix(off[1].Code) }, "its policy's Signals are read once, by one sweep");
    }

    [Fact]
    public async Task Run_TheStoredDetailReadFaults_TheSweepReadsEveryNotInForcePolicyAsBefore_AndStillRedacts()
    {
        // The stored-detail read is a filter over a complete source: when it cannot answer, the sweep reads every Off or Retired policy's
        // Signals, as before task 217, so nothing is left unredacted. Logged at Warning with the cause.
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-00000000021a");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var off = AddOffPolicies(h, 3);
        var redacts = SeedEvidenceSignal(h, off[0].Code, off[0].Id, StoredEvidenceQuotingTheWitness());
        SeedEvidenceSignal(h, off[1].Code, off[1].Id, EvidenceAlreadyRedacted());
        RefileTheWitnessUnder(h, secureProject);
        var perPolicy = ObservePerPolicySignalReads(h, q =>
            q.EntityName == "sprk_signal" && q.LinkEntities.Any(l => l.LinkToEntityName == "sprk_policy")
            && q.Criteria.Filters.SelectMany(f => f.Conditions).Any(c => c.AttributeName == "sprk_evidencerefs")
                ? new TimeoutException("Dataverse timed out.")
                : null);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        perPolicy.Should().BeEquivalentTo(off.Select(p => SignalWriter.DedupeKeyPolicyPrefix(p.Code)), "the sweep falls back to every policy");
        EvidenceWrites(h).Should().ContainSingle().Which.Should().Be((redacts, EvidenceAlreadyRedacted()));
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is TimeoutException
            && e.Message.Contains("every Off or Retired policy"));
    }

    [Fact]
    public async Task Run_APerPolicyReadThatFails_IsStillAPolicyFailure_AndTheRunFails()
    {
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-00000000021b");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var off = AddOffPolicies(h, 1);
        SeedEvidenceSignal(h, off[0].Code, off[0].Id, StoredEvidenceQuotingTheWitness());
        RefileTheWitnessUnder(h, secureProject);
        var perPolicy = ObservePerPolicySignalReads(h, q =>
            q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_dedupekey")
                ? new TimeoutException("Dataverse timed out.")
                : null);

        var result = await h.RunAsync();

        perPolicy.Should().Contain(SignalWriter.DedupeKeyPolicyPrefix(off[0].Code), "the precondition: its read faulted");
        result.Success.Should().BeFalse("a policy whose Signals could not be read fails the run, as before");
        result.ResultJson.Should().Contain("\"policyFailures\":1").And.Contain("\"evidenceRedacted\":0");
        EvidenceWrites(h).Should().BeEmpty();
    }
}
