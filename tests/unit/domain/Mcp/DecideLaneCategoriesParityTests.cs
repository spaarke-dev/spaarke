// The SERVER half of the shared Decide-lane category corpus (spaarkeai-mcp-bff-api-r1 task 022, spec FR-03).
//
// spaarke_show_decisions tells Copilot the Decide lane's category counts, computed by DecideLaneCategories, a mirror of the Console's
// bucketLaneItems (laneCountFilters.ts). This test and its TypeScript twin
// (src/client/shared/Spaarke.UI.Components/src/components/Worklist/__tests__/laneCountFilters.parity.test.ts) execute the SAME file,
// tests/fixtures/decide-lane-category-parity/cases.json, so a divergence fails on whichever side lags.
//
// KEEP path: tests/unit/domain/** (pure domain logic). Real partition, real corpus file; no mocks.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Mcp;

public sealed class DecideLaneCategoriesParityTests
{
    private sealed record CorpusInvoiceReview(string InvoiceId, string? LineId);

    private sealed record CorpusItem(string Lane, string? WorkType, CorpusInvoiceReview? InvoiceReview);

    private sealed record CorpusCard(string Key, string Label, int Count);

    private sealed record CorpusCase(string Name, CorpusItem[] Items, CorpusCard[] Expected);

    private sealed record Corpus(CorpusCase[] Cases);

    private static readonly Corpus Cases = LoadCorpus();

    private static Corpus LoadCorpus()
    {
        // Walk up to the repo root (the BFF's Program.cs is the marker), as the TypeScript twin does.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
            {
                var path = Path.Combine(dir.FullName, "tests", "fixtures", "decide-lane-category-parity", "cases.json");
                return JsonSerializer.Deserialize<Corpus>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                       ?? throw new InvalidOperationException("The Decide-lane category corpus deserialized to null.");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repo root from test base directory '{AppContext.BaseDirectory}'.");
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases.Cases)
        {
            data.Add(c.Name);
        }

        return data;
    }

    [Fact]
    public void Corpus_HasCases()
    {
        Cases.Cases.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Partition_ForEachCorpusCase_GivesTheConsolesCardsInOrder(string name)
    {
        var testCase = Cases.Cases.Single(c => c.Name == name);
        var members = testCase.Items.Select(i => new DecideLaneMember(i.Lane, i.WorkType, i.InvoiceReview is not null));

        var cards = DecideLaneCategories.Partition(members).Select(c => new CorpusCard(c.Key, c.Label, c.Count));

        cards.Should().Equal(testCase.Expected);
    }

    [Fact]
    public void Keys_AreTheDecideLanesLaneFilterKeys_InCardOrder()
    {
        // The closed enum of spaarke_show_decisions' category: LaneFilterKey restricted to the Decide lane (laneCountFilters.ts L32).
        DecideLaneCategories.Keys.Should().Equal("all", "invoiceDecisions", "askOutsideCounsel", "approveOrRebudget", "chaseReply", "other");
    }

    [Fact]
    public void Resolve_AnEmptyCard_FallsBackToAll_AndAHeldCardStays()
    {
        var cards = DecideLaneCategories.Partition([new DecideLaneMember("Decide", "chaseReply", false)]);

        DecideLaneCategories.Resolve(cards, "chaseReply").Should().Be("chaseReply");
        DecideLaneCategories.Resolve(cards, "approveOrRebudget").Should().Be("all", "resolveLaneFilterKey never leaves a lens on an empty card");
        DecideLaneCategories.Resolve(cards, "invoiceDecisions").Should().Be("all", "the card is not even shown when empty");
    }
}
