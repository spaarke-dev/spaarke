using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message is never filed; UAC-r2: fail closed wherever
/// a filing is read). A private message (a <c>sprk_communication</c> holding a reference lookup) has NO core ancestor: filed
/// under an intermediate outside the BFF it is never stamped, a stamp it holds is cleared (the job, a repair, a cascade), and a
/// record filed under it derives nothing from it. An ordinary filed communication is stamped as before.
/// </summary>
public class CoreAncestorStampPrivateMessageTests
{
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-0000000005b6");
    private static readonly Guid Event = Guid.Parse("05600000-0000-0000-0000-000000000e01");
    private static readonly Guid Message = Guid.Parse("05600000-0000-0000-0000-000000000c01");
    private static readonly Guid Todo = Guid.Parse("05600000-0000-0000-0000-000000000101");

    // ── the 5-minute job ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under an event outside the BFF is not stamped by the job")]
    public async Task Job_DoesNotStampAPrivateMessageFiledOutOfBand()
    {
        var world = World(privateMessage: true, heldStamp: null);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_communication", Message, "sprk_regardingmatter").Should().BeNull();
        world.PatchesTo("sprk_communication", Message).Should().BeEmpty();
    }

    [Fact(DisplayName = "fm 056 must-fire: a stamp a private message holds is cleared by the job")]
    public async Task Job_ClearsAStampAPrivateMessageHolds()
    {
        // The held copy equals the event's root: only the private-message rule makes it stale.
        var world = World(privateMessage: true, heldStamp: MatterB);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_communication", Message, "sprk_regardingmatter").Should().BeNull();
        world.Lookup("sprk_communication", Message, "sprk_regardingevent").Should().Be(Event,
            "the filing itself is the two-minute share job's to clear, not the stamp job's");
    }

    [Fact(DisplayName = "fm 056 must-not-fire: an ordinary communication filed under an event is stamped by the job as before")]
    public async Task Job_StampsAnOrdinaryFiledCommunication()
    {
        var world = World(privateMessage: false, heldStamp: null);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_communication", Message, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "fm 056 must-fire: a to-do filed under a private message derives no stamp from it; a copy it holds is cleared")]
    public async Task Job_ARecordUnderAPrivateMessage_DerivesNothingFromIt()
    {
        foreach (var held in new Guid?[] { null, MatterB })
        {
            // The message carries a matter filed outside the BFF (its own direct link, not a stamp).
            var world = new StampWorld()
                .WithColumn("sprk_communication", PrivateContextColumns.Matter)
                .Row("sprk_matter", MatterB)
                .Row("sprk_communication", Message,
                    [("sprk_regardingmatter", "sprk_matter", MatterB), (PrivateContextColumns.Matter, "sprk_matter", MatterB)])
                .Row("sprk_todo", Todo,
                    held is { } m
                        ? [("sprk_regardingcommunication", "sprk_communication", Message), ("sprk_regardingmatter", "sprk_matter", m)]
                        : [("sprk_regardingcommunication", "sprk_communication", Message)],
                    pairId: Message.ToString());

            var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

            run.Success.Should().BeTrue(run.ErrorMessage);
            world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().BeNull($"held {held?.ToString() ?? "nothing"}");
        }
    }

    // ── a repair and a cascade ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a repair of a private message clears its stamp and sets none")]
    public async Task RestampChild_ClearsAPrivateMessagesStamp()
    {
        var world = World(privateMessage: true, heldStamp: MatterB);

        var report = await world.Restamper.RestampChildAsync("sprk_communication", Message);

        report.Failures.Should().BeEmpty();
        world.Lookup("sprk_communication", Message, "sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "fm 056 must-fire: the cascade from its event does not stamp a private message; an ordinary sibling is stamped")]
    public async Task Cascade_DoesNotStampAPrivateMessage()
    {
        var ordinary = Guid.Parse("05600000-0000-0000-0000-000000000c02");
        var world = World(privateMessage: true, heldStamp: null)
            .Row("sprk_communication", ordinary, [("sprk_regardingevent", "sprk_event", Event)], pairId: Event.ToString());

        var report = await world.Restamper.AfterWriteAsync("sprk_event", Event, ["sprk_regardingmatter"]);

        report.Failures.Should().BeEmpty();
        world.Lookup("sprk_communication", Message, "sprk_regardingmatter").Should().BeNull();
        world.Lookup("sprk_communication", ordinary, "sprk_regardingmatter").Should().Be(MatterB);
    }

    // ── create-time stamping ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056: the send path's private message (unfiled) is classified unfiled and plans no stamp")]
    public async Task CreateTime_TheSentPrivateMessage_PlansNoStamp()
    {
        var world = World(privateMessage: false, heldStamp: null);
        var sent = new Entity("sprk_communication") { [PrivateContextColumns.Matter] = new EntityReference("sprk_matter", MatterB) };

        CoreAncestorResolver.ClassifyStampSource("sprk_communication", sent).Kind
            .Should().Be(StampSourceKind.NotFiledUnderAnIntermediate);
        (await world.Restamper.PlanStampOfRowAsync("sprk_communication", sent, CancellationToken.None)).Should().BeNull();
    }

    [Fact(DisplayName = "fm 056 must-fire: a create of a private message naming an intermediate plans no stamp")]
    public async Task CreateTime_APrivateMessageNamingAnIntermediate_PlansNoStamp()
    {
        var world = World(privateMessage: false, heldStamp: null);
        var merged = new Entity("sprk_communication")
        {
            ["sprk_regardingevent"] = new EntityReference("sprk_event", Event),
            [PrivateContextColumns.Matter] = new EntityReference("sprk_matter", MatterB),
        };

        (await world.Restamper.PlanStampOfRowAsync("sprk_communication", merged, CancellationToken.None)).Should().BeNull();

        merged.Attributes.Remove(PrivateContextColumns.Matter);
        (await world.Restamper.PlanStampOfRowAsync("sprk_communication", merged, CancellationToken.None))
            .Should().ContainKey("sprk_regardingmatter", "control: the same create without the reference is stamped");
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An event under matter B and a communication filed under it (outside the BFF); with
    /// <paramref name="privateMessage"/> it is a private message about matter B, and <paramref name="heldStamp"/> is its copy.</summary>
    private static StampWorld World(bool privateMessage, Guid? heldStamp)
    {
        var lookups = new List<(string, string, Guid)> { ("sprk_regardingevent", "sprk_event", Event) };
        if (heldStamp is { } held) lookups.Add(("sprk_regardingmatter", "sprk_matter", held));
        if (privateMessage) lookups.Add((PrivateContextColumns.Matter, "sprk_matter", MatterB));

        return new StampWorld()
            .WithColumn("sprk_communication", PrivateContextColumns.Matter)
            .Row("sprk_matter", MatterB)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_communication", Message, [.. lookups], pairId: Event.ToString());
    }

    private static CoreAncestorStampReconciliationJob Job(StampWorld world)
    {
        var services = new ServiceCollection();
        services.AddSingleton(world.Service);
        services.AddSingleton<IIdempotencyService>(new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance));
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        return new CoreAncestorStampReconciliationJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            world.Resolver,
            world.Restamper,
            TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<CoreAncestorStampReconciliationJob>.Instance);
    }

    private static JobRunContext Context() =>
        new(Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>(), 1);
}
