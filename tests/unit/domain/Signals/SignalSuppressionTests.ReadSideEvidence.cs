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
