// R5 task 016 (FR-A7 / NFR-02) — Briefing-accuracy eval family.
//
// The GoldenUtteranceEvalSuite (same folder) is dispatch/routing-oriented — it proves which
// capability an utterance routes to. It cannot express ITEM-LEVEL briefing accuracy (does each
// rendered row attach the right title to the right link; does the TL;DR only assert real facts).
// So this is a NEW fixture + suite (per the task escalation trigger; schema documented in
// projects/spaarke-daily-update-service-r5/notes/016-briefing-accuracy-corpus-schema-proposal.md).
//
// Each case in briefing-accuracy-corpus.json feeds fixture source records through the REAL
// DailyBriefingNarrator (the deterministic pipeline shipped by tasks 010/011/013/014), mocking
// ONLY the TL;DR LLM call at the IOpenAiClient boundary (ADR-038 §1 — integration-heavy pyramid,
// mock at module boundary, assert observable behavior; the Strict mock also proves "exactly one
// LLM call"). Four deterministic-accuracy properties are asserted:
//   1. zero-cross-pairing     — each bullet's title/regarding/link all come from ITS OWN item.
//   2. aggregation-preference — the deterministic aggregate count reaches the TL;DR; one bullet per item.
//   3. grounding-round-trip   — every fact the TL;DR receives equals the request's own view model.
//   4. tldr-abstraction       — a TL;DR anchor that resolves to no real item is DROPPED, not warned.
//
// [Trait("Category","GoldenUtteranceEval")] enrolls this suite in the dedicated eval-gate CI job
// (.github/workflows/sdap-ci.yml: dotnet test --filter "Category=GoldenUtteranceEval", NO
// continue-on-error) — a red accuracy case fails the merge, satisfying NFR-02 with no CI edit.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Eval.BriefingAccuracy;

[Trait("Category", "GoldenUtteranceEval")]
public sealed class BriefingAccuracyEvalSuiteTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "zero-cross-pairing", "aggregation-preference", "grounding-round-trip", "tldr-abstraction",
    };

    // -------------------------------------------------------------------------
    // Inventory integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void Corpus_Loads_WithUniqueIds_ClosedFamilies_AndCoversAllFourProperties()
    {
        var corpus = LoadCorpus();

        corpus.SchemaVersion.Should().NotBeNullOrWhiteSpace("the corpus declares its schema version");
        corpus.Cases.Should().HaveCountGreaterOrEqualTo(6, "FR-A7: the accuracy family seeds cases across four properties");
        corpus.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");

        foreach (var c in corpus.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            c.Channels.Should().NotBeNullOrEmpty($"case {c.CaseId}: every accuracy case carries source records");
        }

        corpus.Cases.Select(c => c.Family.ToLowerInvariant()).Distinct()
            .Should().BeEquivalentTo(Families, "every one of the four accuracy properties is exercised by ≥1 case");

        // The load-bearing regression case must exist (would FAIL under the pre-R5 cross-pairing bug).
        corpus.Cases.Should().Contain(c => c.CaseId == "BA-CP-002",
            "≥1 case is constructed to fail under the old per-channel LLM narrate leg and pass now (FR-A7 acceptance)");
    }

    // -------------------------------------------------------------------------
    // Family 1 — zero cross-item pairing
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> CrossPairingCases() => CaseIdsForFamily("zero-cross-pairing");

    [Theory]
    [MemberData(nameof(CrossPairingCases))]
    public async Task ZeroCrossPairing_EachBulletIsSelfConsistentWithItsOwnSourceItem(string caseId)
    {
        var tc = Case(caseId);
        var req = BuildRequest(tc);
        var response = await Narrate(req);

        var allItems = AllItems(tc);

        foreach (var channel in tc.Channels)
        {
            var narrative = response.ChannelNarratives.Single(n =>
                string.Equals(n.Category, channel.Category, StringComparison.OrdinalIgnoreCase));
            narrative.Bullets.Should().HaveCount(channel.Items.Length,
                $"case {caseId} channel '{channel.Category}': one deterministic bullet per source item (no aggregation)");

            foreach (var item in channel.Items)
            {
                // Look the bullet up BY the source item id — a cross-paired bullet (item A's title on
                // item B's row) would surface here as a title/link that doesn't match this item.
                var bullet = narrative.Bullets.Should().ContainSingle(b => b.ItemIds.Contains(item.Id),
                    $"case {caseId}: item {item.Id} produces exactly one bullet keyed on its own id").Subject;

                bullet.Narrative.Should().Be(item.Title,
                    $"case {caseId}: the bullet for {item.Id} echoes ITS OWN title verbatim (no cross-item text)");

                var (type, id, name) = ExpectedLinkTarget(item);
                bullet.PrimaryEntityType.Should().Be(type, $"case {caseId}: link target type for {item.Id}");
                bullet.PrimaryEntityId.Should().Be(id, $"case {caseId}: link target id for {item.Id} (never another item's)");
                bullet.PrimaryEntityName.Should().Be(name, $"case {caseId}: link target name for {item.Id}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Family 2 — aggregation preference (the deterministic aggregate reaches the TL;DR)
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> AggregationCases() => CaseIdsForFamily("aggregation-preference");

    [Theory]
    [MemberData(nameof(AggregationCases))]
    public async Task AggregationPreference_TldrReceivesTheDeterministicAggregate_OneBulletPerItem(string caseId)
    {
        var tc = Case(caseId);
        var req = BuildRequest(tc);

        string? capturedPrompt = null;
        var response = await Narrate(req, onTldrPrompt: p => capturedPrompt = p);

        // One bullet per item across all channels — no LLM aggregation collapses items.
        var totalBullets = response.ChannelNarratives.Sum(n => n.Bullets.Length);
        totalBullets.Should().Be(AllItems(tc).Count, $"case {caseId}: every source item is its own bullet");

        // The TL;DR LLM receives the deterministic aggregate — the count is handed to it, not computed by it.
        var facts = ExtractFacts(capturedPrompt);
        facts.TotalNotificationCount.Should().Be(tc.TotalNotificationCount,
            $"case {caseId}: the TL;DR's aggregate total equals the deterministic view-model total");
        facts.CategoryCounts.Select(c => (c.Name, c.Count)).Should()
            .BeEquivalentTo(tc.Categories.Select(c => (c.Name, c.Count)),
                $"case {caseId}: per-category aggregates handed to the TL;DR equal the real per-category counts");
    }

    // -------------------------------------------------------------------------
    // Family 3 — grounding round-trip (every TL;DR fact traces to the view model)
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> GroundingCases() => CaseIdsForFamily("grounding-round-trip");

    [Theory]
    [MemberData(nameof(GroundingCases))]
    public async Task GroundingRoundTrip_EveryTldrFactTracesToTheViewModel_NoRawItemDump(string caseId)
    {
        var tc = Case(caseId);
        var req = BuildRequest(tc);

        string? capturedPrompt = null;
        await Narrate(req, onTldrPrompt: p => capturedPrompt = p);

        var facts = ExtractFacts(capturedPrompt);
        facts.TotalNotificationCount.Should().Be(tc.TotalNotificationCount, $"case {caseId}: total traces to the view model");
        facts.CategoryCounts.Select(c => (c.Name, c.Count)).Should()
            .BeEquivalentTo(tc.Categories.Select(c => (c.Name, c.Count)), $"case {caseId}: category counts trace to the view model");

        // Every record name handed to the TL;DR is a real item title or regarding name — the LLM's
        // allow-list is grounded; it cannot be handed a fabricated name to echo.
        var realNames = AllItems(tc)
            .SelectMany(i => new[] { i.Title, i.RegardingName })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        facts.RecordNames.Should().NotContain(n => !realNames.Contains(n),
            $"case {caseId}: no TL;DR record name may be a fabricated name — every one traces to a real source item (grounded allow-list)");

        // Negative half: the raw per-item channel dump never reaches the LLM — only aggregated facts do.
        capturedPrompt.Should().NotContain("sourceEntityType",
            $"case {caseId}: the TL;DR receives aggregated facts, not the raw channel-item dump (ADR-015)");
    }

    // -------------------------------------------------------------------------
    // Family 4 — TL;DR abstraction (binary anchor resolution; non-resolving anchor dropped)
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> TldrAbstractionCases() => CaseIdsForFamily("tldr-abstraction");

    [Theory]
    [MemberData(nameof(TldrAbstractionCases))]
    public async Task TldrAbstraction_ResolvesRealAnchorsToTheirOwnItem_AndDropsFabricatedOnes(string caseId)
    {
        var tc = Case(caseId);
        tc.TldrSummary.Should().NotBeNull($"case {caseId}: a tldr-abstraction case supplies the simulated TL;DR summary");

        var req = BuildRequest(tc);
        var response = await Narrate(req, tldrSummary: tc.TldrSummary);

        var itemsById = AllItems(tc).ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        // Every produced itemRef points at a REAL request item (binary existence — no dangling ref).
        if (tc.Expect.EveryItemRefResolves == true)
        {
            // NotContain(negation), not OnlyContain — an empty itemRefs set is a valid, correct
            // outcome (FR-A5), and FluentAssertions' OnlyContain fails on empty collections.
            response.Tldr.ItemRefs.Should().NotContain(r => !itemsById.ContainsKey(r.ItemId),
                $"case {caseId}: no TL;DR itemRef may point at a non-existent item (FR-A5 binary resolution)");
        }

        // Named real anchors resolve — AND resolve to the item whose OWN name is that anchor (no cross-item).
        foreach (var anchor in tc.Expect.ResolvedAnchors ?? Array.Empty<string>())
        {
            var itemRef = response.Tldr.ItemRefs.Should().ContainSingle(r => r.AnchorText == anchor,
                $"case {caseId}: anchor '{anchor}' names a real item and must resolve to an itemRef").Subject;
            itemsById.Should().ContainKey(itemRef.ItemId);
            var owning = itemsById[itemRef.ItemId];
            (string.Equals(owning.RegardingName, anchor, StringComparison.OrdinalIgnoreCase)
             || string.Equals(owning.Title, anchor, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue(
                    $"case {caseId}: anchor '{anchor}' resolved to item {itemRef.ItemId}, whose own regarding/title " +
                    "must equal the anchor — binary resolution never binds an anchor to a different item");
        }

        // Fabricated / non-resolving anchors are DROPPED — no itemRef, no warning residue.
        foreach (var dropped in tc.Expect.DroppedAnchors ?? Array.Empty<string>())
        {
            response.Tldr.ItemRefs.Should().NotContain(r => r.AnchorText == dropped,
                $"case {caseId}: '{dropped}' resolves to no source item and MUST be dropped (FR-A6: no warn/withhold band)");
        }

        // Empty itemRefs is valid and is NOT itself a low-quality signal (FR-A5).
        if ((tc.Expect.ResolvedAnchors?.Length ?? 0) == 0)
        {
            response.Tldr.ItemRefs.Should().BeEmpty($"case {caseId}: a summary that names no real record produces no itemRefs");
        }
    }

    // -------------------------------------------------------------------------
    // Narrator drive + boundary mocks (ADR-038: module-boundary mock, no Mock<HttpMessageHandler>)
    // -------------------------------------------------------------------------

    private static async Task<DailyBriefingNarrateResponse> Narrate(
        DailyBriefingNarrateRequest req,
        Action<string>? onTldrPrompt = null,
        string? tldrSummary = null)
    {
        var (actions, llm, scrubber) = BuildBoundaryMocks(onTldrPrompt, tldrSummary);
        var sut = new DailyBriefingNarrator(
            actions.Object, llm.Object, scrubber.Object, NullLogger<DailyBriefingNarrator>.Instance);
        return await sut.NarrateAsync(req, CancellationToken.None);
    }

    private static (Mock<AnalysisActionService> actions, Mock<IOpenAiClient> llm, Mock<IEntityNameScrubber> scrubber)
        BuildBoundaryMocks(Action<string>? onTldrPrompt, string? tldrSummary)
    {
        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);

        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeTldrAction());

        var tldrJson = JsonSerializer.Serialize(new
        {
            summary = tldrSummary ?? "You have items to review today.",
            keyTakeaways = Array.Empty<string>(),
            topAction = string.Empty,
        });

        // Strict — the TL;DR action is the ONLY LLM call the narrator may make (task 010). Any other
        // invocation (e.g. a resurrected per-channel leg) throws MockException and fails the case.
        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        var setup = llm.Setup(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(), It.IsAny<BinaryData>(), TldrActionCode.Replace('-', '_'),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()));
        if (onTldrPrompt is not null)
        {
            setup.Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, _, _, _, _, _, _) => onTldrPrompt(prompt));
        }
        setup.ReturnsAsync(tldrJson);

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult { ScrubbedText = string.Empty, RemovedTerms = Array.Empty<string>() });

        return (actions, llm, scrubber);
    }

    private static AnalysisAction MakeTldrAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = TldrActionCode,
        SystemPrompt = "TLDR.",
        OutputSchemaJson = """{"type":"object","properties":{"summary":{"type":"string"},"keyTakeaways":{"type":"array","items":{"type":"string"}},"topAction":{"type":"string"}},"required":["summary","keyTakeaways","topAction"],"additionalProperties":false}""",
        SortOrder = 0,
        ExecutorType = ExecutorType.AiAnalysis,
        OwnerType = ScopeOwnerType.System,
        Temperature = 0.0m,
    };

    private static IConfiguration BuildTestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/",
            })
            .Build();

    private sealed class TestNoopTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext c, CancellationToken ct)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, CancellationToken ct)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    // -------------------------------------------------------------------------
    // Fixture → view-model mapping + helpers
    // -------------------------------------------------------------------------

    private static DailyBriefingNarrateRequest BuildRequest(BriefingAccuracyCase tc) => new()
    {
        Categories = (tc.Categories ?? Array.Empty<CategoryFixture>())
            .Select(c => new NotificationCategoryDto { Name = c.Name, Count = c.Count, UnreadCount = c.UnreadCount })
            .ToArray(),
        PriorityItems = (tc.PriorityItems ?? Array.Empty<PriorityFixture>())
            .Select(p => new PriorityItemDto { Category = p.Category, Title = p.Title, DueDate = p.DueDate })
            .ToArray(),
        TotalNotificationCount = tc.TotalNotificationCount,
        Channels = (tc.Channels ?? Array.Empty<ChannelFixture>())
            .Select(ch => new ChannelNarrationInput
            {
                Category = ch.Category,
                Label = ch.Label,
                Items = (ch.Items ?? Array.Empty<ItemFixture>())
                    .Select(i => new ChannelItemDto
                    {
                        Id = i.Id,
                        Title = i.Title,
                        RegardingName = i.RegardingName,
                        RegardingEntityType = i.RegardingEntityType,
                        RegardingId = i.RegardingId,
                        SourceEntityType = i.SourceEntityType,
                    })
                    .ToArray(),
            })
            .ToArray(),
    };

    // The narrator's 3-tier click-through resolution (BuildDeterministicBullet): regarding first,
    // else source-row fallback, else unlinked.
    private static (string type, string id, string name) ExpectedLinkTarget(ItemFixture item)
    {
        if (!string.IsNullOrEmpty(item.RegardingId))
        {
            return (item.RegardingEntityType, item.RegardingId, item.RegardingName);
        }
        if (!string.IsNullOrEmpty(item.SourceEntityType) && !string.IsNullOrEmpty(item.Id))
        {
            return (item.SourceEntityType, item.Id, item.Title);
        }
        return (string.Empty, string.Empty, string.Empty);
    }

    private static List<ItemFixture> AllItems(BriefingAccuracyCase tc) =>
        (tc.Channels ?? Array.Empty<ChannelFixture>()).SelectMany(ch => ch.Items ?? Array.Empty<ItemFixture>()).ToList();

    private static TldrFactsDto ExtractFacts(string? capturedPrompt)
    {
        capturedPrompt.Should().NotBeNull("the TL;DR LLM call must have been made (facts are asserted from its prompt)");
        const string marker = "## Input\n\n";
        var idx = capturedPrompt!.IndexOf(marker, StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0, "CallLlmStructuredAsync always appends a \"## Input\" section");
        var json = capturedPrompt[(idx + marker.Length)..].TrimEnd('\n');
        var facts = JsonSerializer.Deserialize<TldrFactsDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        facts.Should().NotBeNull("the \"## Input\" section is the deterministic TldrFactsDto");
        return facts!;
    }

    // -------------------------------------------------------------------------
    // Corpus loading (mirrors GoldenUtteranceEvalSuiteTests.LoadSuite path convention)
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static BriefingAccuracyCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "briefing-accuracy-corpus.json");
        File.Exists(path).Should().BeTrue(
            $"briefing-accuracy-corpus.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval **/*.json ItemGroup)");
        var corpus = JsonSerializer.Deserialize<BriefingAccuracyCorpus>(File.ReadAllText(path), JsonOptions);
        corpus.Should().NotBeNull("the corpus must deserialize into the accuracy-case schema");
        return corpus!;
    }

    private static BriefingAccuracyCase Case(string caseId) =>
        LoadCorpus().Cases.Single(c => c.CaseId == caseId);

    private static IEnumerable<object[]> CaseIdsForFamily(string family) =>
        LoadCorpus().Cases
            .Where(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
            .Select(c => new object[] { c.CaseId });
}

// -----------------------------------------------------------------------------
// Accuracy-case schema (item-level — DATA, so makers add cases without code)
// -----------------------------------------------------------------------------

public sealed record BriefingAccuracyCorpus
{
    public string? SchemaVersion { get; init; }
    public List<BriefingAccuracyCase> Cases { get; init; } = new();
}

public sealed record BriefingAccuracyCase
{
    public string CaseId { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int TotalNotificationCount { get; init; }
    public CategoryFixture[] Categories { get; init; } = Array.Empty<CategoryFixture>();
    public PriorityFixture[] PriorityItems { get; init; } = Array.Empty<PriorityFixture>();
    public ChannelFixture[] Channels { get; init; } = Array.Empty<ChannelFixture>();
    /// <summary>Simulated TL;DR LLM summary text (tldr-abstraction cases drive anchor resolution off it).</summary>
    public string? TldrSummary { get; init; }
    public ExpectFixture Expect { get; init; } = new();
}

public sealed record CategoryFixture
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public int UnreadCount { get; init; }
}

public sealed record PriorityFixture
{
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset? DueDate { get; init; }
}

public sealed record ChannelFixture
{
    public string Category { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public ItemFixture[] Items { get; init; } = Array.Empty<ItemFixture>();
}

public sealed record ItemFixture
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string RegardingName { get; init; } = string.Empty;
    public string RegardingEntityType { get; init; } = string.Empty;
    public string RegardingId { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
}

public sealed record ExpectFixture
{
    public bool? PerItemProvenance { get; init; }
    public bool? TldrFactsTotalMatches { get; init; }
    public bool? EveryItemRefResolves { get; init; }
    public string[]? ResolvedAnchors { get; init; }
    public string[]? DroppedAnchors { get; init; }
}
