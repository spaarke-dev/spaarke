// spaarkeai-mcp-bff-api-r1 task 015 (B-5; spec FR-05, FR-11, FR-04) - the Decide lane in pages of slim rows, through the REAL
// DecisionCapability and SignalReadService over the same scripted caller Dataverse (World) as SignalReadRouteTests. This file is a part of
// SignalReadRouteTests so it reuses that harness; it lives under tests/integration/regression/ (a KEEP path) and is compiled into the
// IntegrationTests assembly by the link in its csproj, and removed from the unit-test assembly's regression glob.
//
// Seeded world: 200 Decide-lane Signals over 4 matters the caller reads, every text at its longest, mixed severities inside each matter
// (so a matter's best item is not its only item), every 7th Signal privilege-flagged, every 10th a billing line item (invoiceReview with both
// ids). Bob alone reads one more matter whose flagged Signal must never reach Alice.
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    private static readonly ClaimsPrincipal AliceCaller = new(new ClaimsIdentity([new Claim("oid", Alice.ToString("D"))], "Test"));

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record DecideSeed(World World, IReadOnlySet<Guid> Privileged, Guid HiddenPrivileged);

    private static string LongText(string prefix, int length) => (prefix + new string('x', length))[..length];

    private static DecideSeed SeedDecideLane(int count = 200)
    {
        var world = new World();
        var matters = Enumerable.Range(0, 4)
            .Select(n => world.Matter(readers: [Alice], name: LongText($"Matter {n} ", 400), number: $"MAT-{n + 1:0000}")).ToArray();
        var severities = new[] { Critical, Warning, Info };
        var shortNames = new[] { LongText("Budget gap ", 100), LongText("Comes due ", 100), LongText("Heads up ", 100) };
        var start = DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var privileged = new HashSet<Guid>();
        for (var i = 0; i < count; i++)
        {
            (Guid? Type, Guid Id)? subject = null;
            if (i % 10 == 0)
            {
                var invoice = world.Invoice([Alice], VendorOrg, matter: matters[i % 4]);
                subject = (BillingEventType, world.BillingEvent([Alice], invoice));
            }

            var id = world.Signal(Decide, core: (MatterType, matters[i % 4]), subject: subject, severity: severities[i % 3], shortName: shortNames[i % 3],
                title: LongText($"Title {i:000} ", 200), subjectName: LongText($"Subject {i:000} ", 400),
                firstDetected: start.AddHours(i).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                evidence: "[]", sentence: LongText("Sentence ", 2000), privilegeFlagged: i % 7 == 0);
            if (i % 7 == 0) privileged.Add(id);
        }

        var hiddenMatter = world.Matter(readers: [Bob]);
        var hidden = world.Signal(Decide, core: (MatterType, hiddenMatter), privilegeFlagged: true);
        return new DecideSeed(world, privileged, hidden);
    }

    private IServiceScope CapabilityScope(World world, out IDecisionCapability capability)
    {
        _factory.World = world;
        world.ActingAs = Alice;
        var scope = _factory.Services.CreateScope();
        capability = scope.ServiceProvider.GetRequiredService<IDecisionCapability>();
        return scope;
    }

    /// <summary>The Decide lane as the Console's list returns it: groups of Signal ids, in order.</summary>
    private async Task<List<List<Guid>>> ConsoleGroupsAsync(World world)
    {
        using var scope = CapabilityScope(world, out var capability);
        var result = await capability.ListDecideAsync(AliceCaller, CancellationToken.None);
        result.Outcome.Should().Be(SignalReadOutcome.Ok);
        return result.Response!.Lanes.Single(l => l.Lane == "Decide").Groups.Select(g => g.Items.Select(i => i.SignalId).ToList()).ToList();
    }

    private async Task<DecidePageResult> PageAsync(World world, string? cursor)
    {
        using var scope = CapabilityScope(world, out var capability);
        return await capability.ListDecidePageAsync(AliceCaller, cursor, CancellationToken.None);
    }

    /// <summary>Pages from <paramref name="cursor"/> to the end.</summary>
    private async Task<List<DecidePageResult>> AllPagesAsync(World world, string? cursor = null)
    {
        var pages = new List<DecidePageResult>();
        do
        {
            var page = await PageAsync(world, cursor);
            page.Outcome.Should().Be(DecidePageOutcome.Ok);
            pages.Add(page);
            cursor = page.NextCursor;
            pages.Count.Should().BeLessThan(50, "paging ends");
        }
        while (cursor is not null);

        return pages;
    }

    [Fact]
    public async Task DecidePaging_ReturnsEveryItemOnce_InTheConsoleOrder_InPagesOfAtMost50_Under64KiB_EachRowAtMost600Bytes()
    {
        var seed = SeedDecideLane();
        var console = (await ConsoleGroupsAsync(seed.World)).SelectMany(g => g).ToList();
        console.Should().HaveCount(200);

        var pages = await AllPagesAsync(seed.World);

        pages.Select(p => p.Items.Count).Should().Equal(50, 50, 50, 50);
        pages.Select(p => p.NextCursor is null).Should().Equal(false, false, false, true); // only the last page has no next cursor
        pages.SelectMany(p => p.Items).Select(i => i.SignalId).Should().Equal(console, "every item once, in the Console's order");
        foreach (var page in pages)
        {
            JsonSerializer.SerializeToUtf8Bytes(page.Items, WebJson).Length.Should().BeLessThan(64 * 1024);
            foreach (var item in page.Items)
            {
                JsonSerializer.SerializeToUtf8Bytes(item, WebJson).Length.Should().BeLessThanOrEqualTo(WorklistItemSummary.MaxJsonBytes);
            }
        }
    }

    [Fact]
    public async Task DecidePaging_PrivilegeFlagged_IsTrueOnTheFlaggedSignalsOnly_AndNeverOnTheConsolesRestRow()
    {
        var seed = SeedDecideLane();

        var items = (await AllPagesAsync(seed.World)).SelectMany(p => p.Items).ToList();

        items.Where(i => i.PrivilegeFlagged).Select(i => i.SignalId).Should().BeEquivalentTo(seed.Privileged);
        items.Count(i => !i.PrivilegeFlagged).Should().Be(200 - seed.Privileged.Count);
        items.Select(i => i.SignalId).Should().NotContain(seed.HiddenPrivileged, "a flagged Signal on a matter the caller cannot read is not theirs to see");

        // The Console's REST row gains no member (FR-09; the golden baselines pin the bytes, this pins the name).
        var body = await OkAsync(seed.World, Alice);
        AllItems(body).Should().HaveCount(200);
        AllItems(body).Should().OnlyContain(i => !i.EnumerateObject().Any(p => p.Name.Equals("privilegeFlagged", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task DecidePaging_BillingItemsKeepInvoiceReview_AndAPageIsOneBatchedBillingEventRead()
    {
        var seed = SeedDecideLane();
        var before = LineInvoiceReads(seed.World);

        var page = await PageAsync(seed.World, null);

        LineInvoiceReads(seed.World).Should().Be(before + 1, "#2580's one batched BillingEventRead per response, unchanged by paging");
        var billing = page.Items.Where(i => i.InvoiceReview is not null).ToList();
        billing.Should().NotBeEmpty();
        billing.Should().OnlyContain(i => i.InvoiceReview!.InvoiceId != Guid.Empty && i.InvoiceReview.LineId != null);
    }

    // The Start-review walk resolves the item it just decided, so a stale cursor is the common case. For each way items disappear, the
    // rest of the lane arrives exactly once: nothing from page 1 again, nothing missing, no error.
    [Theory]
    [InlineData("the-cursor-item")]
    [InlineData("the-best-item-of-every-group-on-page-1")]
    [InlineData("the-best-item-of-the-cursor-group-and-later-items")]
    [InlineData("the-whole-cursor-group-and-later-items")]
    public async Task DecidePaging_AStaleCursor_ReturnsTheRemainingItemsOnce_WithNoDuplicateAndNoError(string scenario)
    {
        var seed = SeedDecideLane();
        var original = await ConsoleGroupsAsync(seed.World);
        var page1 = await PageAsync(seed.World, null);
        var page1Ids = page1.Items.Select(i => i.SignalId).ToList();
        var cursorItem = page1Ids[^1];
        var cursorGroup = original.Single(g => g.Contains(cursorItem));

        var resolved = scenario switch
        {
            "the-cursor-item" => new HashSet<Guid> { cursorItem },
            "the-best-item-of-every-group-on-page-1" => original.Where(g => page1Ids.Contains(g[0])).Select(g => g[0]).ToHashSet(),
            "the-best-item-of-the-cursor-group-and-later-items" =>
                new HashSet<Guid>([cursorGroup[0], .. original.SelectMany(g => g).Except(page1Ids).Where((_, n) => n % 9 == 0)]),
            _ => new HashSet<Guid>([.. cursorGroup, .. original.SelectMany(g => g).Except(page1Ids).Where((_, n) => n % 9 == 0)]),
        };
        resolved.Should().NotBeEmpty();
        seed.World.Signals.RemoveAll(s => resolved.Contains(s.Id));

        var rest = (await AllPagesAsync(seed.World, page1.NextCursor)).SelectMany(p => p.Items).Select(i => i.SignalId).ToList();

        var cursorAt = original.SelectMany(g => g).ToList().IndexOf(cursorItem);
        var expected = original.SelectMany(g => g).Skip(cursorAt + 1).Where(id => !resolved.Contains(id)).ToList();
        if (scenario == "the-cursor-item")
        {
            rest.Should().Equal(expected, "with only the cursor item gone the order is the Console's");
        }
        else
        {
            // The cursor's group is finished first, so after a resolved best item the order can differ from a fresh list's; the set cannot.
            rest.Should().BeEquivalentTo(expected);
        }

        rest.Should().OnlyHaveUniqueItems();
        rest.Should().NotIntersectWith(page1Ids.Where(id => !resolved.Contains(id)), "nothing already returned comes back");
    }

    [Fact]
    public async Task DecidePaging_AStaleButWellFormedCursor_ForAnItemThatIsGone_IsNotAnError()
    {
        var seed = SeedDecideLane(30);
        var page1 = await PageAsync(seed.World, null);
        var stale = new DecideCursor(
            new RankKey(0, false, DateTimeOffset.Parse("2026-08-01T00:00:00Z")),
            Guid.NewGuid(),
            new RankKey(0, false, DateTimeOffset.Parse("2026-08-01T00:00:00Z")), Guid.NewGuid(), []).Encode();

        var page = await PageAsync(seed.World, stale);

        page.Outcome.Should().Be(DecidePageOutcome.Ok);
        page.Items.Should().NotBeEmpty();
        page1.Items.Should().HaveCount(30);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("e30")]
    public async Task DecidePaging_AMalformedCursor_IsCursorInvalid_AndNothingIsRead(string cursor)
    {
        var seed = SeedDecideLane(5);
        var reads = seed.World.Requests.Count;

        var page = await PageAsync(seed.World, cursor);

        page.Outcome.Should().Be(DecidePageOutcome.CursorInvalid);
        seed.World.Requests.Count.Should().Be(reads, "the cursor is refused before any Dataverse call");
    }

    [Fact]
    public async Task DecidePaging_ATruncatedRealCursor_IsCursorInvalid()
    {
        var seed = SeedDecideLane();
        var cursor = (await PageAsync(seed.World, null)).NextCursor!;

        (await PageAsync(seed.World, cursor[..(cursor.Length / 2)])).Outcome.Should().Be(DecidePageOutcome.CursorInvalid);
    }
}
