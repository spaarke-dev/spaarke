using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner, 2026-11-22, design note Q5): a private message — a communication holding a
/// context reference (<see cref="PrivateContextColumns"/>) — is never filed and never moved to another thread, by any writer. The
/// one refusal is in the re-file core (<see cref="IRecordOwnershipResolver.ReparentAsync"/>), asked on the row it reads, before
/// anything is written: <see cref="RecordOwnerRefusal.PrivateMessageNotFileable"/>, the change never applied, no owner written.
/// </summary>
/// <remarks>The REAL <see cref="RecordOwnershipResolver"/> over <see cref="FakeDirectory"/>, seeded as
/// <see cref="RecordOwnershipResolverDirectUnfileTests"/> seeds its Direct message.</remarks>
public class RecordOwnershipResolverPrivateMessageNeverFiledTests
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid ThreadId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b4");
    private static readonly Guid MatterId = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000001");
    private static readonly Guid AboutMatterId = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000002");
    private static readonly Guid OtherThreadId = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000003");
    private static readonly Guid RecordTypeId = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000004");
    private static readonly Guid EventId = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000005");
    private static readonly Guid DirectTeam = Guid.Parse("d1d1d1d1-0056-4056-8056-000000000006");

    private readonly RecordingLogger _log = new();

    /// <summary>Every change that sets, moves or clears a filing link of a communication.</summary>
    public static TheoryData<string> FilingChanges => new()
    {
        "sets an ownership parent",
        "sets a typed regarding that files under no parent (payload links)",
        "sets a typed regarding that files under no parent (parent changes)",
        "sets the regarding pair",
        "moves it to another thread",
        "clears its thread",
    };

    public static TheoryData<string, string> EveryColumnAndChange()
    {
        var data = new TheoryData<string, string>();
        foreach (var column in PrivateContextColumns.All)
        {
            foreach (var change in FilingChanges)
                data.Add(column, (string)change[0]);
        }

        return data;
    }

    // ── (i) Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryColumnAndChange))]
    public async Task APrivateMessage_IsNeverFiledOrMoved_RefusedWithTheCode_BeforeAnythingIsWritten(string column, string change)
    {
        var directory = World().WithPrivateMessage(column, AboutMatterId);
        var request = Change(change);
        var applied = false;

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue($"{change} on a private message ({column})");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        resolution.RefusalCode.Should().Be("sdap.communication.private.not_fileable", "the code is a stable contract");
        applied.Should().BeFalse("the change is never applied");
        directory.Assignments.Should().BeEmpty("no owner is written");
        directory.ConditionalWrites.Should().BeEmpty();
        var row = directory.Row("sprk_communication", MessageId);
        row.GetAttributeValue<EntityReference>("sprk_communicationthread")?.Id.Should().Be(ThreadId, "the thread is untouched");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().BeNull();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("NOT written", StringComparison.Ordinal));
    }

    // ── (ii) Must not fire ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FilingChanges))]
    public async Task AnOrdinaryDirectMessage_TheSameChange_IsNotRefusedForPrivacy_AndIsWritten(string change)
    {
        var directory = World().WithUnfiledDirectMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = Change(change);
        var applied = false;

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.RefusalCode.Should().NotBe(RecordOwnerRefusal.PrivateMessageNotFileable);
        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue("a message with no context reference is filed as before");
    }

    [Fact]
    public async Task ANonCommunicationChild_HoldingAColumnOfThatName_IsReFiledAsBefore()
    {
        var directory = World().WithRecord("sprk_event", EventId, FakeDirectory.GeneralBu, owningTeam: FakeDirectory.GeneralTeam,
            extra: new() { [PrivateContextColumns.Matter] = new EntityReference("sprk_matter", AboutMatterId) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_event",
            RecordId = EventId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            },
        };
        var applied = false;

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue();
        directory.Assignments.Should().ContainSingle().Which.Entity.Should().Be("sprk_event");
    }

    [Theory]
    [InlineData("rebinds the thread it holds")]
    [InlineData("clears a regarding it does not hold")]
    public async Task APrivateMessage_AChangeThatMovesNoFilingLink_IsWritten(string change)
    {
        var directory = World().WithPrivateMessage(PrivateContextColumns.Matter, AboutMatterId);
        var request = Base() with
        {
            ParentChanges = change == "rebinds the thread it holds"
                ? new Dictionary<string, EntityReference?> { ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ThreadId) }
                : new Dictionary<string, EntityReference?> { ["sprk_regardingaccount"] = null },
        };
        var applied = false;

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue("a change that files nothing and moves no thread is not a filing");
    }

    [Fact]
    public async Task AnEnvironmentWithoutTheReferenceColumns_PassesTheCheck()
    {
        var directory = World().WithUnfiledDirectMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        foreach (var column in PrivateContextColumns.All)
            directory.WithoutColumn("sprk_communication", column);
        var request = Change("sets an ownership parent");
        var applied = false;

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue("no private message can exist where the columns do not");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static RecordReparent Base() => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = MessageId,
        ParentChanges = new Dictionary<string, EntityReference?>(),
        CallerSystemUserId = FakeDirectory.CallerUserId,
        WhenUnfiled = UnfiledOwnership.UnfiledCommunication,
        SecureExitCaller = new Sprk.Bff.Api.Services.Access.SecureRemovalCaller(
            _ => Task.FromResult<Guid?>(FakeDirectory.CallerUserId),
            (_, _) => Task.FromResult(Spaarke.Dataverse.AccessRights.Read)),
    };

    private static RecordReparent Change(string change) => change switch
    {
        "sets an ownership parent" => Base() with
        {
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) },
        },
        "sets a typed regarding that files under no parent (payload links)" => Base() with
        {
            RegardingLinksSet = new Dictionary<string, EntityReference>
            {
                ["sprk_regardingperson"] = new EntityReference("contact", DirectUnfileWorldExtensions.ContactId),
            },
        },
        "sets a typed regarding that files under no parent (parent changes)" => Base() with
        {
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingperson"] = new EntityReference("contact", DirectUnfileWorldExtensions.ContactId),
            },
        },
        "sets the regarding pair" => Base() with
        {
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RecordTypeId),
            },
        },
        "moves it to another thread" => Base() with
        {
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", OtherThreadId),
            },
        },
        "clears its thread" => Base() with
        {
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_communicationthread"] = null },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    /// <summary>The standard directory plus the Direct unit and team, an ordinary matter to file under, the matter a private
    /// message is about, and a second Direct thread.</summary>
    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithBusinessUnit(DirectMessageOwnerTeamDouble.DirectUnitId, DirectMessageOwnerTeamDouble.BusinessUnitName,
            parent: FakeDirectory.GeneralBu)
        .WithTeam(DirectTeam, DirectMessageOwnerTeamDouble.DirectUnitId, isDefault: false, teamType: 0,
            DirectMessageOwnerTeamDouble.OwnerTeamName)
        .WithOrdinaryRoot("sprk_matter", MatterId)
        .WithOrdinaryRoot("sprk_matter", AboutMatterId)
        .WithRecord("sprk_communicationthread", OtherThreadId, FakeDirectory.ChildBu, extra: new()
        {
            ["sprk_threadtype"] = new OptionSetValue(100000001),
            ["sprk_isdefaultthread"] = false,
        });

    private static Func<CancellationToken, Task> Applies(FakeDirectory directory, RecordReparent request, Action alsoDone) =>
        _ =>
        {
            var written = request.ParentChanges.ToDictionary(change => change.Key, change => (object?)change.Value);
            foreach (var (column, link) in request.RegardingLinksSet)
                written[column] = link;
            directory.Write(request.EntityLogicalName, request.RecordId, written);
            alsoDone();
            return Task.CompletedTask;
        };

    private RecordOwnershipResolver Resolver(FakeDirectory directory) =>
        directory.Resolver(logger: _log,
            directMessageOwnerTeam: new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).Lookup());

    private sealed class RecordingLogger : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}

/// <summary>Seeds a private message: the unfiled Direct message of <see cref="DirectUnfileWorldExtensions"/> holding one context
/// reference.</summary>
internal static class PrivateMessageWorldExtensions
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");

    public static FakeDirectory WithPrivateMessage(this FakeDirectory directory, string referenceColumn, Guid aboutMatter)
    {
        directory.WithUnfiledDirectMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            [referenceColumn] = new EntityReference(
                PrivateContextColumns.ColumnByTable.Single(entry => entry.Value == referenceColumn).Key, aboutMatter),
        });
        return directory;
    }
}
