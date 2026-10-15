// spaarke-ontology-platform-r1 task 038 (task 145 review F1, option B; D-123; uac-r2 #1747 finding e): the witness rule at READ
// time. SignalSuppressionService.WitnessDetailShownOnReadAsync judges each stored witness reference against THE partition truth
// (uac-r2's real walk over the in-memory Dataverse of SignalSuppressionTests.Store). It mirrors task 145's write-time rule; when 145
// merges, the two share one helper and these cases stay.
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

public partial class SignalSuppressionTests
{
    private static string WitnessRef(Guid communicationId) => $"sprk_communication:{communicationId:D}";

    /// <summary>A communication filed under <paramref name="matterId"/> and, when given, also under a secure project.</summary>
    private static Guid ReadSideCommunication(Store s, Guid matterId, Guid? secureProject = null, string owner = Store.OrdinaryTeam)
    {
        var id = Guid.NewGuid();
        s.Subject(id, family: secureProject, owner: owner);
        s.Others.Single(r => r.Id == id)["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        return id;
    }

    [Fact]
    public async Task T038_AWitnessReFiledUnderASecureFamily_IsNotShownOnAnOrdinaryMattersSignal()
    {
        var s = new Store();
        var signal = s.Open(MatterId, MatterId, secure: false, subjectEntity: "sprk_matter");
        var refiled = ReadSideCommunication(s, MatterId, secureProject: Store.ProjectA, owner: Store.SecureOwner);
        var ordinary = ReadSideCommunication(s, MatterId);

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(refiled), WitnessRef(ordinary)])], default);

        shown![signal].Should().BeEquivalentTo([WitnessRef(ordinary)], "the re-filed witness is in project A's family, not the matter's");
    }

    [Fact]
    public async Task T038_AWitnessOfTheSignalsOwnSecureFamily_IsShown()
    {
        var s = new Store();
        var secureMatter = Guid.NewGuid();
        s.SecureMatter(secureMatter);
        var signal = s.Open(secureMatter, secureMatter, secure: true, subjectEntity: "sprk_matter");
        var own = ReadSideCommunication(s, secureMatter, owner: Store.SecureOwner);

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(own)])], default);

        shown![signal].Should().BeEquivalentTo([WitnessRef(own)]);
    }

    [Fact]
    public async Task T038_AnUndeterminedWitness_OrARefThatNamesNoLineageChild_IsNotShown()
    {
        var s = new Store();
        var signal = s.Open(MatterId, MatterId, secure: false, subjectEntity: "sprk_matter");
        var orphan = ReadSideCommunication(s, MatterId, owner: Store.SecureOwner); // Secure-owned, no secure root: undetermined

        var shown = await s.Service.WitnessDetailShownOnReadAsync(
            [(signal, [WitnessRef(orphan), $"sprk_matter:{MatterId:D}", "not-a-ref"])], default);

        shown![signal].Should().BeEmpty();
    }

    [Fact]
    public async Task T038_NoSecureTeamInTheEnvironment_ShowsEveryReference()
    {
        var s = new Store { SecureTeamExists = false };
        var signal = s.Open(MatterId, MatterId, subjectEntity: "sprk_matter");
        var anything = Guid.NewGuid();

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(anything)])], default);

        shown![signal].Should().BeEquivalentTo([WitnessRef(anything)], "with no Secure team every family is ordinary");
    }

    [Fact]
    public async Task T038_AWitnessFiledUnderSecureXButStillOwnedByAnOrdinaryTeam_IsInXsFamily_AndIsNotShown()
    {
        // Task 145's re-own window case (pass 2 F1 of 038): the family comes from the ROOTS, never from the owner alone. An owner-based
        // override would call this witness ordinary and show its detail on the ordinary matter's Signal.
        var s = new Store();
        var signal = s.Open(MatterId, MatterId, secure: false, subjectEntity: "sprk_matter");
        var windowed = ReadSideCommunication(s, MatterId, secureProject: Store.ProjectA, owner: Store.OrdinaryTeam);

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(windowed)])], default);

        shown![signal].Should().BeEmpty("filed under secure project A, the witness is in A's family whoever owns it today");
    }

    [Fact]
    public async Task T038_ASecureOwnedSignalWithNoSecureRoot_IsUndetermined_AndShowsNothing()
    {
        // The Signal's OWNER is part of its placement (Secure-team-owned with no root found = undetermined): the app-only read must
        // select ownerid. An ordinary witness would otherwise be judged "same family" as an ordinary-looking Signal.
        var s = new Store();
        var signal = s.Open(MatterId, MatterId, secure: true, subjectEntity: "sprk_matter");
        var ordinary = ReadSideCommunication(s, MatterId);

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(ordinary)])], default);

        shown![signal].Should().BeEmpty();
    }

    [Fact]
    public async Task T038_TheReadsAreBoundedByDistinctParents_NotSignalsTimesWalks()
    {
        // F3 (038 pass 2): every lineage is placed in ONE walker run with the Secure team identified once, so adding Signals whose
        // witnesses each bring ONE new parent (a work assignment) adds about one read each, not a team lookup plus a re-walk of the
        // shared matter and secure project per witness.
        async Task<int> QueriesFor(int signals)
        {
            var s = new Store();
            var batch = new List<(Guid, IReadOnlyList<string>)>();
            for (var i = 0; i < signals; i++)
            {
                var signal = s.Open(MatterId, Guid.NewGuid(), secure: false, subjectEntity: "sprk_matter");
                var assignment = Guid.NewGuid();
                s.Others.Add(new Entity("sprk_workassignment", assignment)
                {
                    ["sprk_issecure"] = false,
                    ["owninguser"] = new EntityReference("systemuser", Guid.NewGuid()),
                });
                var witness = ReadSideCommunication(s, MatterId, secureProject: Store.ProjectA);
                s.Others.Single(r => r.Id == witness)["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", assignment);
                batch.Add((signal, [WitnessRef(witness)]));
            }

            var before = s.Queries;
            var shown = await s.Service.WitnessDetailShownOnReadAsync(batch, default);
            shown.Should().NotBeNull();
            return s.Queries - before;
        }

        var few = await QueriesFor(4);
        var many = await QueriesFor(24);
        (many - few).Should().BeLessThanOrEqualTo(20 * 3,
            $"20 more Signals bring 20 more distinct parents (and their subjects); the per-lineage walk made it far more (few={few}, many={many})");
        few.Should().BeLessThan(30); // measured 2026-10-14: few=14, many=54 (one run); the per-lineage walk gave 52 and 272
    }

    [Fact]
    public async Task T038_AFault_ReturnsNull_SoTheRouteWithholdsEverything()
    {
        var s = new Store();
        var signal = s.Open(MatterId, MatterId, secure: false, subjectEntity: "sprk_matter");
        var ordinary = ReadSideCommunication(s, MatterId);
        s.FailReads = true;

        (await s.Service.WitnessDetailShownOnReadAsync([(signal, [WitnessRef(ordinary)])], default)).Should().BeNull();
    }

    [Fact]
    public async Task T038_ASignalTheAppOnlyReadDoesNotReturn_ShowsNothing()
    {
        var s = new Store();
        var ordinary = ReadSideCommunication(s, MatterId);
        var missing = Guid.NewGuid();

        var shown = await s.Service.WitnessDetailShownOnReadAsync([(missing, [WitnessRef(ordinary)])], default);

        shown!.ContainsKey(missing).Should().BeFalse("absent means withheld");
    }
}
