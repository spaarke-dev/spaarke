// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194b fix round R (ontology answer (c)) — the re-file pass asks a Decision Record the question its
//     writer asked: the subjects of the Signals its sprk_steps.resolvedSignalIds lists, read by id in any state (a Signal closed
//     during the review is never linked). The never-out rule (RecordOwnershipContext.DecisionRecordHeldInIsolation) holds a
//     record only when that question is incomplete, in every pass, the re-file pass included. Ontology's cases (i)-(iv).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL reconciler, resolver, synchronizer and CoreAncestorResolver over
// task 148's world (SecureChildShareWorld).

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

public class DecisionRecordRefileQuestionTests
{
    private const string Matter = "sprk_matter";
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid CatalogMatter = Guid.Parse("4b000000-0000-4000-8000-0000001945e1");
    private static readonly Guid Sharee = Guid.Parse("5e000000-0000-4000-8000-0000001945e2");

    private readonly Guid _m = Guid.NewGuid();
    private readonly Guid _x = Guid.NewGuid();
    private readonly Guid _event = Guid.NewGuid();
    private readonly Guid _record = Guid.NewGuid();
    private readonly Guid _primarySignal = Guid.NewGuid();
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();

    public DecisionRecordRefileQuestionTests()
    {
        _world.OrdinaryRoot(Matter, _m).SecureRoot(Matter, _x)
            .Add("sprk_recordtype_ref", CatalogMatter, ("sprk_recordlogicalname", Matter))
            .OrdinaryChild("sprk_event", _event, ("sprk_regardingmatter", Matter, _m));
        // The primary Signal: about the event, linked to the record.
        Signal(_primarySignal, "sprk_event", _event, ("sprk_decisionrecord", new EntityReference("sprk_decisionrecord", _record)));
    }

    private SecureChildReconciler Reconciler() => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, webApi: null!);

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    /// <summary>A Signal about <paramref name="subject"/> (its subject lookup repeats its pair id).</summary>
    private void Signal(Guid id, string subjectTable, Guid subject, params (string Column, object Value)[] more) =>
        _world.Add("sprk_signal", id, more
            .Prepend(("sprk_regardingrecordid", (object)subject.ToString("D")))
            .Prepend(("sprk_regarding" + subjectTable["sprk_".Length..], Ref(subjectTable, subject)))
            .Prepend(("owningteam", Ref("team", GeneralTeam)))
            .ToArray());

    /// <summary>A Signal resolved and closed during the review: never linked to the record, named only by its sprk_steps.</summary>
    private Guid ClosedDuringTheReview(string subjectTable, Guid subject)
    {
        var id = Guid.NewGuid();
        Signal(id, subjectTable, subject, ("sprk_signalstatus", new OptionSetValue(SignalWriter.SignalStatusResolved)));
        return id;
    }

    /// <summary>The Decision Record about the event (its core the ordinary matter M), owned by <paramref name="owner"/>, its
    /// sprk_steps <paramref name="steps"/> (none when null).</summary>
    private void DecisionRecord(Guid owner, string? steps)
    {
        _world.Add("sprk_decisionrecord", _record,
            ("owningteam", Ref("team", owner)),
            ("sprk_regardingevent", Ref("sprk_event", _event)),
            ("sprk_regardingmatter", Ref(Matter, _m)),
            ("sprk_matter", Ref(Matter, _m)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogMatter)),
            ("sprk_corerecordid", _m.ToString("D")),
            ("sprk_regardingrecordid", _event.ToString("D")));
        if (steps is not null)
            _world.Set("sprk_decisionrecord", _record, "sprk_steps", steps);
    }

    private static string Steps(params Guid[] resolved) =>
        "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"resolvedSignalIds\":["
        + string.Join(",", resolved.Select(s => "\"" + s.ToString("D") + "\"")) + "],\"steps\":[]}";

    /// <summary>The event re-filed M → X (into isolation) and back X → M, each followed by the re-file pass; the second pass's
    /// report. The record carries X's mirror between the two, as task 149 leaves it.</summary>
    private async Task<SecureChildReconcileReport?> MToXToMAsync()
    {
        _world.Set("sprk_event", _event, "sprk_regardingmatter", Ref(Matter, _x));
        _world.MoveOwner("sprk_event", _event, DataversePrincipalRef.Team(SecureTeam));
        await Reconciler().AfterRefileAsync("sprk_event", _event, () => Task.FromResult(false), CancellationToken.None);
        _world.OwnerOf("sprk_decisionrecord", _record).Should().Be(DataversePrincipalRef.Team(SecureTeam), "M → X takes it into isolation");
        _shares.Seed("sprk_decisionrecord", _record, DataversePrincipalRef.User(Sharee), CollaborateMask);

        _world.Set("sprk_event", _event, "sprk_regardingmatter", Ref(Matter, _m));
        _world.MoveOwner("sprk_event", _event, DataversePrincipalRef.Team(GeneralTeam));
        return await Reconciler().AfterRefileAsync("sprk_event", _event, () => Task.FromResult(true), CancellationToken.None);
    }

    [Fact(DisplayName = "194b R (ontology c, i) must fire: a Signal closed during the review (never linked) has a SECURE subject — after M → X → M the Decision Record stays isolated, read from resolvedSignalIds")]
    public async Task ASignalClosedDuringTheReview_WithASecureSubject_KeepsTheRecordIsolated()
    {
        var secureEvent = Guid.NewGuid();
        _world.SecureChild("sprk_event", secureEvent, ("sprk_regardingmatter", Matter, _x));
        var closed = ClosedDuringTheReview("sprk_event", secureEvent);
        DecisionRecord(SecureTeam, Steps(_primarySignal, closed)); // its writer gave it the Secure team (secure-if-any)

        var report = await MToXToMAsync();

        _world.OwnerOf("sprk_decisionrecord", _record).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "its commit-time question still names a secure subject: it belongs in isolation, not merely held there");
        report!.HeldInIsolation.Should().BeEmpty("its question is whole");
        _world.OwnerWrites.Should().NotContain(w => w.Id == _record);
    }

    [Fact(DisplayName = "194b R (ontology c, ii) must not fire: every commit-time parent ordinary — after M → X → M the Decision Record is owned by M's business-unit team again")]
    public async Task EveryCommitTimeParentOrdinary_TheRecordFollowsBackToM()
    {
        var ordinaryEvent = Guid.NewGuid();
        _world.OrdinaryChild("sprk_event", ordinaryEvent, ("sprk_regardingmatter", Matter, _m));
        var closed = ClosedDuringTheReview("sprk_event", ordinaryEvent);
        DecisionRecord(GeneralTeam, Steps(_primarySignal, closed));

        var report = await MToXToMAsync();

        _world.OwnerOf("sprk_decisionrecord", _record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _shares.MaskOf("sprk_decisionrecord", _record, DataversePrincipalRef.User(Sharee)).Should().BeNull("X's mirror goes with it");
        report!.HeldInIsolation.Should().BeEmpty();
    }

    [Fact(DisplayName = "194b R (ontology c, iii) must fire: a Decision Record whose sprk_steps lists no resolvedSignalIds is HELD in isolation and reported by the re-file pass, and loses X's mirror")]
    public async Task NoResolvedSignalIds_TheRefilePassHoldsAndReportsIt()
    {
        DecisionRecord(GeneralTeam, "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"steps\":[]}");

        var report = await MToXToMAsync();

        _world.OwnerOf("sprk_decisionrecord", _record).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        report!.HeldInIsolation.Should().ContainSingle().Which.Should().Match<SecureChildRowChange>(
            h => h.Id == _record && h.Detail!.Contains("resolvedSignalIds"));
        report.Status.Should().Be(SecureChildReconcileStatus.Completed, "holding does not make the pass incomplete");
        _shares.MaskOf("sprk_decisionrecord", _record, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "no sharee keeps access through a held record");
    }

    [Fact(DisplayName = "194b R (ontology c, iv) must fire: a Signal the Decision Record lists cannot be read — the re-file pass HOLDS it in isolation and reports it")]
    public async Task AListedSignalCannotBeRead_TheRefilePassHoldsAndReportsIt()
    {
        var ordinaryEvent = Guid.NewGuid();
        _world.OrdinaryChild("sprk_event", ordinaryEvent, ("sprk_regardingmatter", Matter, _m));
        var closed = ClosedDuringTheReview("sprk_event", ordinaryEvent);
        DecisionRecord(GeneralTeam, Steps(_primarySignal, closed));
        _world.FailingIdListReadsOf("sprk_signal", closed);

        var report = await MToXToMAsync();

        _world.OwnerOf("sprk_decisionrecord", _record).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        report!.HeldInIsolation.Should().ContainSingle().Which.Should().Match<SecureChildRowChange>(
            h => h.Id == _record && h.Detail!.Contains("could not be read"));
    }
}
