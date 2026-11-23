using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message is never filed; UAC-r2: fail closed wherever a
/// filing is read). A private message filed under a record outside the BFF is decided nothing by the secure-child reconcile: under a
/// SECURE record it is not moved into isolation and gets none of the record's mirrored shares; under an ORDINARY record that moved
/// business unit it is not moved either. An ordinary Direct message (no reference) is decided exactly as before.
/// </summary>
/// <remarks>The REAL <see cref="SecureChildReconciler"/> over <see cref="SecureChildShareWorld"/>, its resolver composed with the
/// REAL <see cref="Sprk.Bff.Api.Infrastructure.Dataverse.DirectMessageOwnerTeam"/> as the host composes it.</remarks>
public class SecureChildReconcilerPrivateMessageTests
{
    private const string Matter = "sprk_matter";
    private const string Communication = "sprk_communication";

    private static readonly Guid DirectTeam = Guid.Parse("d0560000-0000-4000-8000-0000000003d1");
    private static readonly Guid Sender = Guid.Parse("c0560000-0000-4000-8000-0000000003a1");
    private static readonly Guid Recipient = Guid.Parse("c0560000-0000-4000-8000-0000000003a2");
    private static readonly Guid Sharee = Guid.Parse("5e056000-0000-4000-8000-0000000003a3");

    /// <summary>Business unit B, where the ordinary matter was reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b056000-0000-4000-8000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("0b056000-0000-4000-8000-000000000002");

    private const int CollaborateMask = 1 | 2 | 4 | 16;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);

    private readonly FakeRecordShareTable _shares = new();
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);

    public SecureChildReconcilerPrivateMessageTests()
    {
        var directUnit = Guid.Parse("d0560000-0000-4000-8000-0000000003b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
        _world.User(Sharee, SecureChildShareWorld.GeneralBu);
    }

    // ── Under a SECURE record ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under a secure matter is not moved into isolation and gets no mirrored share")]
    public async Task APrivateMessageFiledUnderASecureMatter_IsNotReownedOrShared()
    {
        var matter = SecureMatterSharedWithSharee();
        var message = DirectMessageUnder(matter, DirectTeam, privateAbout: matter);

        var report = await Reconciler().ReconcileAsync(
            Matter, matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Id == message, "a private message holds no filing, so the record decides nothing");
        _world.OwnerOf(Communication, message).Should().Be(DataversePrincipalRef.Team(DirectTeam));
        _shares.WriteLog.Should().NotContain(w => w.RecordId == message, "the secure record's sharees are never mirrored onto it");
        report.Changes.Should().ContainSingle(c => c.Id == message).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.Detail!.Contains("private message"),
            "reported, never failed or refused: nothing is carried, the filing is cleared elsewhere");
        report.Tables.Single(t => t.Table == Communication).Should().Match<SecureChildTableCounts>(t => t.Failed == 0 && t.Refused == 0);
    }

    [Fact(DisplayName = "fm 056 must-not-fire: an ordinary Direct message (no reference) filed under a secure matter moves into isolation, as before")]
    public async Task AnOrdinaryDirectMessageFiledUnderASecureMatter_IsReownedAsToday()
    {
        var matter = SecureMatterSharedWithSharee();
        var message = DirectMessageUnder(matter, DirectTeam, privateAbout: null);

        await Reconciler().ReconcileAsync(
            Matter, matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None);

        _world.OwnerOf(Communication, message).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam));
        _shares.WriteLog.Should().Contain(w => w.RecordId == message, "an isolated child carries the record's mirror");
    }

    // ── Under an ORDINARY record that moved business unit ────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under an ordinary matter that moved is not re-owned by the ordinary pass")]
    public async Task APrivateMessageFiledUnderAnOrdinaryMatterThatMoved_IsNotReowned()
    {
        var matter = OrdinaryMatterMovedToB();
        // Owned by a default team (the owner step did not run): the shape the ordinary pass would move.
        var message = DirectMessageUnder(matter, SecureChildShareWorld.GeneralTeam, privateAbout: matter);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, matter, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Id == message);
        _world.OwnerOf(Communication, message).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
        _shares.WriteLog.Should().NotContain(w => w.RecordId == message);
        report.Changes.Should().ContainSingle(c => c.Id == message).Which.Detail.Should().Contain("private message");
    }

    [Fact(DisplayName = "fm 056 must-not-fire: the same default-team message with no reference follows its moved matter, as before")]
    public async Task AnOrdinaryMessageFiledUnderAnOrdinaryMatterThatMoved_FollowsItAsToday()
    {
        var matter = OrdinaryMatterMovedToB();
        var message = DirectMessageUnder(matter, SecureChildShareWorld.GeneralTeam, privateAbout: null);

        await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, matter, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerWrites.Should().Contain(w => w.Id == message, "a default-team row follows its record");
    }

    [Fact(DisplayName = "fm 056: a Direct-team-owned private message under an ordinary matter that moved is not re-owned either")]
    public async Task ADirectTeamOwnedPrivateMessageUnderAnOrdinaryMatterThatMoved_IsNotReowned()
    {
        var matter = OrdinaryMatterMovedToB();
        var message = DirectMessageUnder(matter, DirectTeam, privateAbout: matter);

        await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, matter, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Id == message);
        _world.OwnerOf(Communication, message).Should().Be(DataversePrincipalRef.Team(DirectTeam));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private Guid SecureMatterSharedWithSharee()
    {
        var matter = Guid.NewGuid();
        _world.SecureRoot(Matter, matter);
        _shares.Seed(Matter, matter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        return matter;
    }

    private Guid OrdinaryMatterMovedToB()
    {
        var matter = Guid.NewGuid();
        _world.Add(Matter, matter, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));
        return matter;
    }

    /// <summary>A strict-pair message in a Direct thread, filed under <paramref name="matter"/> outside the BFF and owned by
    /// <paramref name="team"/>; with <paramref name="privateAbout"/>, a private message about that matter.</summary>
    private Guid DirectMessageUnder(Guid matter, Guid team, Guid? privateAbout)
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Sender)),
            ("sprk_threadtype", new OptionSetValue(100000001)),
            ("sprk_isdefaultthread", false));

        var message = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("owningteam", new EntityReference("team", team)),
            ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)),
            ("sprk_regardingmatter", new EntityReference(Matter, matter)),
            ("sprk_sentby", new EntityReference("systemuser", Sender)),
            ("sprk_directrecipient", new EntityReference("systemuser", Recipient)),
        };
        if (privateAbout is { } about)
            columns.Add((PrivateContextColumns.Matter, new EntityReference(Matter, about)));
        _world.Add(Communication, message, columns.ToArray());
        return message;
    }

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(
            () => _world, _shares, new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(() => _world),
            directMessageOwnerTeam: _directory.Lookup());
}
