using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b, pinning the secure-child reconcile after an unfile (no production edit):
/// once the writer's step has returned a message unfiled from a Secure record to the Direct team, the reconcile's pass over its
/// content rows moves them to the Direct team (3a's resolver rule for a Direct-owned message); while the Direct team cannot be
/// identified no content row is re-owned; a message that kept the Secure team keeps its content rows where they are; and an
/// ordinary message's unfile re-owns nothing here (with no recorded creator its owner is kept, #2722).
/// </summary>
/// <remarks>The REAL <see cref="SecureChildReconciler"/> over <see cref="SecureChildShareWorld"/>, its resolver composed with the
/// REAL <see cref="DirectMessageOwnerTeam"/> over <see cref="DirectMessageOwnerTeamDouble"/> as the host composes it.</remarks>
public class SecureChildReconcilerDirectUnfileCascadeTests
{
    private static readonly Guid DirectTeam = Guid.Parse("d1980000-0000-4000-8000-0000000003d1");
    private static readonly Guid Caller = Guid.Parse("c1980000-0000-4000-8000-0000000003a1");
    private static readonly Guid Other = Guid.Parse("c1980000-0000-4000-8000-0000000003a2");

    /// <summary>The content tables whose rows name the message as their parent (<c>sprk_communication</c>).</summary>
    private static readonly string[] ContentTables =
        ["sprk_communicationattachment", "sprk_communicationparticipant", "sprk_emailreviewlog"];

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Caller, Other);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);

    public SecureChildReconcilerDirectUnfileCascadeTests()
    {
        var directUnit = Guid.Parse("d1980000-0000-4000-8000-0000000003b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
    }

    [Fact]
    public async Task AfterASecureUnfile_AMessageTheStepReturnedToTheDirectTeam_TakesItsContentRowsToTheDirectTeam()
    {
        var (message, content) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        Unfile(message, toDirectTeam: true);

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        foreach (var (table, id) in content)
            _world.OwnerWrites.Should().Contain(w => w.Table == table && w.Id == id && w.Owner == DataversePrincipalRef.Team(DirectTeam),
                $"{table} follows its Direct-owned message out of the Secure team");
    }

    [Fact]
    public async Task AfterASecureUnfile_WhileTheDirectTeamCannotBeIdentified_NoContentRowIsReOwned()
    {
        var (message, content) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        Unfile(message, toDirectTeam: true);
        _directory.Faults = true;

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        _world.OwnerWrites.Where(w => content.Contains((w.Table, w.Id))).Should().BeEmpty("never a default team in its place");
    }

    [Fact]
    public async Task AfterASecureUnfile_AMessageThatKeptTheSecureTeam_KeepsItsContentRowsWhereTheyAre()
    {
        var (message, content) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        Unfile(message, toDirectTeam: false);

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        _world.OwnerWrites.Where(w => content.Contains((w.Table, w.Id))).Should().BeEmpty();
    }

    [Fact]
    public async Task AfterAnOrdinaryUnfile_ReOwnsNothing()
    {
        var matter = Guid.NewGuid();
        _world.OrdinaryRoot("sprk_matter", matter);
        var (message, _) = MessageUnder(matter, SecureChildShareWorld.GeneralTeam);
        Unfile(message, toDirectTeam: false);

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(false), CancellationToken.None);

        _world.OwnerWrites.Should().BeEmpty("an ordinary communication with no recorded creator keeps its owner (#2722)");
    }

    // ── uac-r2 197a-2 (#2728): a document linked to the message ONLY through sprk_relatedcommunication ──────────────────────

    [Fact(DisplayName = "#2728 must-fire: after a Secure unfile, a document linked only through sprk_relatedcommunication follows its Direct message")]
    [Trait("status", "issue-2728")]
    public async Task AfterASecureUnfile_ADocumentLinkedOnlyToTheMessage_FollowsItToTheDirectTeam()
    {
        var (message, _) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        var document = DocumentOf(message);
        Unfile(message, toDirectTeam: true);

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(DirectTeam),
            "the document follows its Direct-owned message out of the Secure team, as the message's other content rows do");
    }

    [Fact(DisplayName = "#2728 must-not-fire: while the Direct team cannot be identified, that document is not re-owned")]
    [Trait("status", "issue-2728")]
    public async Task AfterASecureUnfile_WhileTheDirectTeamCannotBeIdentified_TheDocumentIsNotReOwned()
    {
        var (message, _) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        var document = DocumentOf(message);
        Unfile(message, toDirectTeam: true);
        _directory.Faults = true;

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Table == "sprk_document" && w.Id == document, "never a default team in its place");
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
    }

    [Fact(DisplayName = "#2728 must-not-fire: a message that kept the Secure team keeps that document isolated")]
    [Trait("status", "issue-2728")]
    public async Task AfterASecureUnfile_AMessageThatKeptTheSecureTeam_KeepsTheDocumentIsolated()
    {
        var (message, _) = MessageUnder(SecureMatter(), SecureChildShareWorld.SecureTeam);
        var document = DocumentOf(message);
        Unfile(message, toDirectTeam: false);

        await Reconciler(_directory.Lookup()).AfterRefileAsync("sprk_communication", message, () => Task.FromResult(true), CancellationToken.None);

        _world.OwnerWrites.Should().NotContain(w => w.Table == "sprk_document" && w.Id == document);
    }

    /// <summary>A document owned by the Secure team, linked to <paramref name="message"/> ONLY through <c>sprk_relatedcommunication</c>
    /// (an archived .eml or a materialized attachment).</summary>
    private Guid DocumentOf(Guid message)
    {
        var document = Guid.NewGuid();
        _world.Add("sprk_document", document,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)),
            ("sprk_relatedcommunication", new EntityReference("sprk_communication", message)),
            ("versionnumber", 1L));
        return document;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private Guid SecureMatter()
    {
        var matter = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", matter);
        return matter;
    }

    /// <summary>A strict-pair message in a Direct thread, filed under <paramref name="matter"/> and owned by <paramref name="team"/>,
    /// with one row per content table owned by the same team.</summary>
    private (Guid Message, List<(string Table, Guid Id)> Content) MessageUnder(Guid matter, Guid team)
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Caller)),
            ("sprk_threadtype", new OptionSetValue(100000001)),
            ("sprk_isdefaultthread", false));

        var message = Guid.NewGuid();
        var owner = ("owningteam", (object)new EntityReference("team", team));
        _world.Add("sprk_communication", message, owner,
            ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", matter)),
            ("sprk_sentby", new EntityReference("systemuser", Caller)),
            ("sprk_directrecipient", new EntityReference("systemuser", Other)));

        var content = new List<(string Table, Guid Id)>();
        foreach (var table in ContentTables)
        {
            var id = Guid.NewGuid();
            var lookup = table == "sprk_document" ? "sprk_relatedcommunication" : "sprk_communication";
            _world.Add(table, id, owner, (lookup, new EntityReference("sprk_communication", message)));
            content.Add((table, id));
        }

        return (message, content);
    }

    /// <summary>The unfile as the writer leaves it: the filing cleared and, when the Direct rule applied, the message (only) moved
    /// to the Direct team by the step.</summary>
    private void Unfile(Guid message, bool toDirectTeam)
    {
        _world.Set("sprk_communication", message, "sprk_regardingmatter", null);
        if (toDirectTeam)
            _world.MoveOwner("sprk_communication", message, DataversePrincipalRef.Team(DirectTeam));
    }

    /// <summary><see cref="SecureChildShareWorld.ReconcilerOver"/>, with the resolver composed with the Direct lookup as the host
    /// composes it.</summary>
    private SecureChildReconciler Reconciler(DirectMessageOwnerTeam lookup)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world).Object;
        var configuration = SecureChildShareWorld.Configuration();
        var resolver = new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
            entities, configuration, NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance, lookup);
        var synchronizer = new SecureChildShareSynchronizer(
            entities, _shares, SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new SecureChildReconciler(
            entities, resolver, synchronizer, _users, configuration, NullLogger<SecureChildReconciler>.Instance,
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            coreAncestors: SecureChildShareWorld.CoreAncestorsOver(() => _world),
            people: AssignedAccessTestDoubles.Representation(new AssignedAccessTestDoubles.FakeAssignedAccessStore()),
            recordShares: _shares,
            directMessageOwnerTeam: lookup);
    }
}
