using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 198 Part D: what a message's participants keep after it is filed to a record, driven over the
/// REAL <see cref="SecureChildReconciler"/> (its <c>AfterRefileAsync</c>, the step every re-file writer runs), the REAL
/// synchronizer and one in-memory Dataverse (<see cref="SecureChildShareWorld"/>).
/// <list type="bullet">
/// <item>An ORDINARY target: every participant keeps their shares on the message and its content rows.</item>
/// <item>A SECURE target: the message's shares follow the synchronizer (the root's set), as before.</item>
/// <item>A RESTRICTED target (its own value, or inherited through its filing): every user flagged external loses their shares on
/// the message and its content rows (attachment, participant, email review log, document); an internal user, and a user whose
/// flag is blank, keeps them.</item>
/// <item>Fail closed: a user-flag read that faults removes nothing; a user the read does not return is left as is.</item>
/// </list>
/// </summary>
public class RestrictedFilingShareBarTests
{
    private const int Read = 1;

    private static readonly Guid External = Guid.Parse("e0000198-0000-4000-8000-0000000000e1");
    private static readonly Guid Internal = Guid.Parse("e0000198-0000-4000-8000-0000000000a1");
    private static readonly Guid BlankFlag = Guid.Parse("e0000198-0000-4000-8000-0000000000b1");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new();

    public RestrictedFilingShareBarTests()
    {
        _users.Flags[External] = true;
        _users.Flags[Internal] = false;
        _users.Flags[BlankFlag] = null;
    }

    private SecureChildReconciler Reconciler() => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _users);

    private Task RefileAsync(Guid message) =>
        Reconciler().AfterRefileAsync("sprk_communication", message, () => Task.FromResult(false), CancellationToken.None);

    /// <summary>A message filed through <paramref name="filing"/> with one attachment, participant, review log and document row.</summary>
    private (Guid Message, IReadOnlyList<(string Table, Guid Id)> Rows) Message(params (string Column, string Target, Guid Id)[] filing)
    {
        var (message, attachment, participant, reviewLog, document) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryChild("sprk_communication", message, filing)
            .OrdinaryChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_communicationparticipant", participant, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_emailreviewlog", reviewLog, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_document", document, ("sprk_relatedcommunication", "sprk_communication", message));
        return (message, new[]
        {
            ("sprk_communication", message), ("sprk_communicationattachment", attachment),
            ("sprk_communicationparticipant", participant), ("sprk_emailreviewlog", reviewLog), ("sprk_document", document),
        });
    }

    private void ShareWith(IEnumerable<(string Table, Guid Id)> rows, params Guid[] users)
    {
        foreach (var (table, id) in rows)
        foreach (var user in users)
            _shares.Seed(table, id, DataversePrincipalRef.User(user), Read);
    }

    private Guid Matter(int? accessPermission)
    {
        var matter = Guid.NewGuid();
        _world.OrdinaryRoot("sprk_matter", matter);
        if (accessPermission is { } value)
            _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(value));
        return matter;
    }

    private bool Holds(string table, Guid id, Guid user) => _shares.MaskOf(table, id, DataversePrincipalRef.User(user)) is > 0;

    [Fact(DisplayName = "198 D pin (must-not-fire): filed to an ORDINARY record, every participant keeps the message and content-row shares")]
    public async Task FiledToAnOrdinaryRecord_EveryParticipantKeepsTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, External, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        foreach (var user in new[] { External, Internal, BlankFlag })
            Holds(table, id, user).Should().BeTrue($"{table} is filed under an ordinary record");
        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        _users.Queries.Should().BeEmpty("a target that is not Restricted never reads the users");
    }

    [Fact(DisplayName = "198 D must-fire: filed to a RESTRICTED record, an external participant loses every share, content rows included")]
    public async Task FiledToARestrictedRecord_AnExternalParticipantLosesTheirShares_ContentRowsIncluded()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, External, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"{table} is filed under a Restricted record and the user is flagged external");
            Holds(table, id, Internal).Should().BeTrue($"an internal user keeps {table}");
            Holds(table, id, BlankFlag).Should().BeTrue($"a blank flag is internal (owner round 67), so {table} is kept");
        }
    }

    [Fact(DisplayName = "198 D must-fire: a target Restricted by INHERITANCE (an event under a Restricted matter) bars the external participant too")]
    public async Task FiledToARecordThatInheritsRestricted_AnExternalParticipantLosesTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var evt = Guid.NewGuid();
        _world.OrdinaryChild("sprk_event", evt, ("sprk_regardingmatter", "sprk_matter", matter));
        var (message, rows) = Message(("sprk_regardingevent", "sprk_event", evt));
        ShareWith(rows, External, Internal);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"{table} inherits Restricted through the event");
            Holds(table, id, Internal).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 D must-not-fire: filed to a Restricted record, internal and blank-flag participants keep everything")]
    public async Task FiledToARestrictedRecord_InternalParticipantsKeepTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, Internal).Should().BeTrue();
            Holds(table, id, BlankFlag).Should().BeTrue();
        }

        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact(DisplayName = "198 D fail closed + R3 must-fire: a re-file whose user read faults removes nothing; after a restart the job's sweep removes the share")]
    public async Task FiledToARestrictedRecord_WhenTheUserFlagsCannotBeRead_NothingIsRemoved_ThenTheSweepAfterARestartRemovesIt()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Restricted);
        ShareWith(rows, External, Internal);
        _users.Faults = true;

        await RefileAsync(message);

        foreach (var (table, id) in rows)
            Holds(table, id, External).Should().BeTrue("a fault is unknown, and unknown never removes (nor grants)");
        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();

        // R3 (finding 3): the next pass is real. The BFF restarts (a NEW job instance: its carry is empty, and the re-file's
        // in-memory state is gone). The stored value already equals the inherited one, so nothing is written; the sweep still
        // finds the Restricted message and bars it.
        _users.Faults = false;
        var run = await RunNewJobInstanceAsync();

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"the sweep after the restart bars {table}");
            Holds(table, id, Internal).Should().BeTrue();
        }

        _world.AccessPermissionWrites.Should().NotContain(w => w.Id == message, "its value was already right: no write led to the bar");
        run.GetProperty("sweep").GetProperty("rows").GetInt32().Should().BeGreaterThan(0);
        run.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "198 D fail closed + R3 must-fire: an unread user is left at the re-file; the job's sweep after a restart removes it once it reads as external")]
    public async Task FiledToARestrictedRecord_AnUnreadUserIsLeft_TheExternalOneStillLosesTheirShares_ThenTheSweepRemovesTheUnreadOne()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Restricted);
        var unread = Guid.NewGuid(); // not in the directory
        ShareWith(rows, External, unread);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, unread).Should().BeTrue("an unreadable user is logged and left, never decided");
            Holds(table, id, External).Should().BeFalse();
        }

        // R3: the next pass is real and lands — a restart, then the sweep, once the user reads as flagged external.
        _users.Flags[unread] = true;
        await RunNewJobInstanceAsync();

        foreach (var (table, id) in rows)
            Holds(table, id, unread).Should().BeFalse($"the sweep after the restart bars {table}");
    }

    // ── R3 (fix round 1): the job bars every Restricted communication it examines; the record-anchored grant bars at grant time ──

    [Fact(DisplayName = "198 R3 must-not-fire (job): on a Restricted filing, internal and blank-flag users keep everything")]
    public async Task TheJob_OnARestrictedFiling_KeepsInternalAndBlankFlagShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Restricted);
        ShareWith(rows, Internal, BlankFlag);

        var run = await RunNewJobInstanceAsync();

        run.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(1, "the job did look at it");
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Internal).Should().BeTrue();
            Holds(table, id, BlankFlag).Should().BeTrue("a blank flag is internal (owner round 67)");
        }

        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact(DisplayName = "198 R3 pin (job): a SECURE message under a Standard root is the synchronizer's; the bar does not run on it")]
    public async Task TheJob_OnASecureStandardMessage_DoesNotRunTheBar()
    {
        var secure = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", secure);
        _world.Set("sprk_matter", secure, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Standard));
        _shares.Seed("sprk_matter", secure, DataversePrincipalRef.User(Internal), Read);
        var message = Guid.NewGuid();
        _world.SecureChild("sprk_communication", message, ("sprk_regardingmatter", "sprk_matter", secure));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Standard);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Internal), Read);

        var run = await RunNewJobInstanceAsync();

        run.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(0, "a Standard target is not Restricted");
        Holds("sprk_communication", message, Internal).Should().BeTrue("the root shares it");
    }

    [Fact(DisplayName = "198 R3 must-fire (grant time): a message created under an already-Restricted record is not granted to an external anchor member")]
    public async Task AnchorGrant_UnderAnAlreadyRestrictedRecord_DoesNotGrantTheExternalMember()
    {
        // Inherited only: the message's own value is not stamped, so the bar must walk its filing.
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, _) = Message(("sprk_regardingmatter", "sprk_matter", matter));

        await AnchorGrantAsync(message, matter, External, Internal, BlankFlag);

        Holds("sprk_communication", message, External).Should().BeFalse("Restricted wins over AC6 for a user flagged external");
        Holds("sprk_communication", message, Internal).Should().BeTrue();
        Holds("sprk_communication", message, BlankFlag).Should().BeTrue("a blank flag is internal");
    }

    [Fact(DisplayName = "198 R3 must-fire (grant time, own value): the Restricted value stamped at create bars the external anchor member")]
    public async Task AnchorGrant_OnAMessageStampedRestrictedAtCreate_DoesNotGrantTheExternalMember()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, _) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Restricted);

        await AnchorGrantAsync(message, matter, External, Internal);

        Holds("sprk_communication", message, External).Should().BeFalse();
        Holds("sprk_communication", message, Internal).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R3 must-not-fire (AC6): under a NON-Restricted record every anchor member is granted, and no user is read")]
    public async Task AnchorGrant_UnderAStandardRecord_GrantsEveryMember_AsBefore()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, _) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Standard);

        await AnchorGrantAsync(message, matter, External, Internal, BlankFlag);

        foreach (var user in new[] { External, Internal, BlankFlag })
            Holds("sprk_communication", message, user).Should().BeTrue("a non-Restricted filing grants exactly as before");
        _users.Queries.Should().BeEmpty("the bar reads no user when the filing is not Restricted");
    }

    [Fact(DisplayName = "198 R3 fail closed (grant time): whether the filing is Restricted cannot be decided → the external member is not granted, the internal one is")]
    public async Task AnchorGrant_WhenRestrictedCannotBeDecided_GrantsTheInternalMemberOnly()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, _) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        _world.FailingRowReadsOf("sprk_matter", matter).FailingIdListReadsOf("sprk_matter", matter); // the walk cannot read the record above it

        await AnchorGrantAsync(message, matter, External, Internal);

        Holds("sprk_communication", message, External).Should().BeFalse();
        Holds("sprk_communication", message, Internal).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R3 must-fire (job): a legacy share of an external anchor member under an already-Restricted record is removed by the sweep")]
    public async Task TheJob_ALegacyExternalShare_UnderAnAlreadyRestrictedRecord_IsRemovedByTheSweep()
    {
        // Granted before the grant-time bar existed; created under an already-Restricted record, never re-filed, value already right.
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        StampedAsTheRefileWriterDoes(message, InheritedAccessPermission.Restricted);
        ShareWith(rows, External, Internal);

        await RunNewJobInstanceAsync();

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"the sweep bars {table}");
            Holds(table, id, Internal).Should().BeTrue();
        }

        _world.AccessPermissionWrites.Should().NotContain(w => w.Id == message);
    }

    /// <summary>The value the re-file writer (and the create path) stamps on the message before the after-filing step runs.</summary>
    private void StampedAsTheRefileWriterDoes(Guid message, int value) =>
        _world.Set("sprk_communication", message, InheritedAccessPermission.Column, new OptionSetValue(value));

    private static readonly Guid AnchorThread = Guid.Parse("e0000198-0000-4000-8000-0000000000f1");

    /// <summary>
    /// The record-anchored grant at create (<see cref="Sprk.Bff.Api.Services.Communication.Access.DirectThreadAccessService"/>),
    /// as the host composes it: this world's reads, the share seam, the user reader and the column probe; the anchor's members
    /// are <paramref name="members"/>.
    /// </summary>
    private async Task AnchorGrantAsync(Guid message, Guid matter, params Guid[] members)
    {
        var derivation = new Mock<IThreadMembershipDerivationService>();
        derivation
            .Setup(d => d.DeriveAnchorGranteeUserIdsAsync("sprk_matter", matter, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(members.ToList());
        var service = new DirectThreadAccessService(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares,
            new Lazy<IThreadMembershipDerivationService>(() => derivation.Object),
            SecureChildShareWorld.Configuration(),
            NullLogger<DirectThreadAccessService>.Instance,
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            DirectMessageOwnerTeamDouble.Unconfigured(),
            _users,
            SecureChildShareWorld.CoreAncestorsOver(() => _world));

        await service.GrantMessageAccessAsync(message, ThreadAssignment.Anchored(AnchorThread, "sprk_matter", matter));
    }

    private static readonly DateTimeOffset JobStart = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One run of a NEW <see cref="SecureChildReconciliationJob"/> instance over this world, as the host builds it after a
    /// restart: empty carry, its window from the first-run lookback, and its sweep from the start. Returns its
    /// <c>accessPermission</c> report.
    /// </summary>
    private async Task<JsonElement> RunNewJobInstanceAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => _world).Object);
        services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _users));
        services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => _world, _shares));
        services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => _world));
        services.AddSingleton<DataverseWebApiClient>(_ => _users);
        services.AddSingleton<IDataverseRecordShareService>(_ => _shares);
        using var provider = services.BuildServiceProvider();

        var job = new SecureChildReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(JobStart),
            new ConfigurationBuilder().Build(), NullLogger<SecureChildReconciliationJob>.Instance);
        var result = await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("accessPermission").Clone();
    }

    [Fact(DisplayName = "198 D pin: filed to a SECURE record, the message's shares follow the synchronizer (the root's set)")]
    public async Task FiledToASecureRecord_TheMessageSharesFollowTheSynchronizer()
    {
        var secure = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", secure);
        _shares.Seed("sprk_matter", secure, DataversePrincipalRef.User(Internal), Read);
        var message = Guid.NewGuid();
        _world.SecureChild("sprk_communication", message, ("sprk_regardingmatter", "sprk_matter", secure));
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Internal), Read);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(BlankFlag), Read);

        await RefileAsync(message);

        Holds("sprk_communication", message, Internal).Should().BeTrue("the root shares it");
        Holds("sprk_communication", message, BlankFlag).Should().BeFalse("the root does not share it: the mirror takes it off");
        _users.Queries.Should().BeEmpty("a Secure target that is not Restricted is the synchronizer's alone");
    }
}
