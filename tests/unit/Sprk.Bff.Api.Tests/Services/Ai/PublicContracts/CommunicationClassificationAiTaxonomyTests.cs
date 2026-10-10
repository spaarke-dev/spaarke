using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// spaarke-ontology-platform-r1 D-117(b): rung 5's classifier (<see cref="CommunicationClassificationAi"/>) reads the
/// editable <c>sprk_triagecategory</c> taxonomy (names + classifier guidance) through the same
/// <see cref="LookupChoicesResolver"/> path as the TRIAGE-EMAIL Action, and emits <c>triageCategory</c> as an
/// additive field. Runs the REAL facade and the REAL resolver; mocks only the Dataverse read
/// (<see cref="IScopeResolverService"/>) and the model (<see cref="IOpenAiClient"/>) boundaries. No fee/scope word
/// appears in production code: every taxonomy string asserted here comes from the mocked rows.
/// </summary>
public class CommunicationClassificationAiTaxonomyTests
{
    private const string TaxonomyRef = "lookup:sprk_triagecategory.sprk_name";

    private readonly Mock<IScopeResolverService> _dataverse = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly List<(string SystemPrompt, string Schema)> _calls = new();

    private CommunicationClassificationResult? _modelReturns = new() { Category = "invoice" };

    public CommunicationClassificationAiTaxonomyTests()
    {
        _openAi
            .Setup(o => o.GetStructuredCompletionAsync<CommunicationClassificationResult>(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, BinaryData, string, string, CancellationToken>((messages, schema, _, _, _) =>
                _calls.Add((((SystemChatMessage)messages.First()).Content[0].Text, schema.ToString())))
            .ReturnsAsync(() => _modelReturns!);
    }

    private void Rows(string[] names, IReadOnlyDictionary<string, string>? guidance)
    {
        _dataverse
            .Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
        _dataverse
            .Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(guidance ?? new Dictionary<string, string>());
    }

    private CommunicationClassificationAi Build(string? taxonomyRef = TaxonomyRef) =>
        new(_openAi.Object,
            Options.Create(new DocumentIntelligenceOptions()),
            new LookupChoicesResolver(_dataverse.Object, NullLogger<LookupChoicesResolver>.Instance),
            Options.Create(new AiClassificationOptions { TriageTaxonomyChoicesRef = taxonomyRef }),
            NullLogger<CommunicationClassificationAi>.Instance);

    private static JsonElement Property(string schema, string name) =>
        JsonDocument.Parse(schema).RootElement.GetProperty("properties").GetProperty(name);

    private static string[] Required(string schema) =>
        JsonDocument.Parse(schema).RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Classify_ComposesPromptFromTaxonomyRows_AndConstrainsTriageCategoryToTheirNames()
    {
        Rows(new[] { "Alpha row", "Beta row" },
            new Dictionary<string, string> { ["Alpha row"] = "Use for alpha things.", ["Beta row"] = "Use for beta things." });

        await Build().ClassifyAsync("subject", "body");

        var (prompt, schema) = _calls.Should().ContainSingle("FR-05: the taxonomy adds no model call").Subject;
        prompt.Should().Contain("Triage taxonomy:");
        prompt.Should().Contain("- Alpha row — Use for alpha things.");
        prompt.Should().Contain("- Beta row — Use for beta things.");

        var triage = Property(schema, "triageCategory");
        triage.GetProperty("type").GetString().Should().Be("string");
        triage.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().Equal("Alpha row", "Beta row");
        Required(schema).Should().Contain("triageCategory", "strict structured output requires every property");

        // Reads the same enabled rows as the TRIAGE-EMAIL Action ($choices path, task 072 filter).
        _dataverse.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", "sprk_enabled eq true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Classify_WhenARowsGuidanceIsEdited_ThePromptChangesWithNoCodeChange()
    {
        var names = new[] { "Alpha row", "Beta row" };
        Rows(names, new Dictionary<string, string> { ["Alpha row"] = "Original guidance." });
        await Build().ClassifyAsync("s", "b");

        Rows(names, new Dictionary<string, string> { ["Alpha row"] = "Edited guidance an admin wrote." });
        await Build().ClassifyAsync("s", "b");

        _calls.Should().HaveCount(2);
        _calls[0].SystemPrompt.Should().Contain("- Alpha row — Original guidance.");
        _calls[1].SystemPrompt.Should().Contain("- Alpha row — Edited guidance an admin wrote.");
        _calls[1].SystemPrompt.Should().NotContain("Original guidance.");
        _calls[0].SystemPrompt.Replace("Original guidance.", "Edited guidance an admin wrote.")
            .Should().Be(_calls[1].SystemPrompt, "the row edit is the only difference between the two prompts");
    }

    [Fact]
    public async Task Classify_WhenARowIsAddedOrRenamed_TheEnumFollowsTheRows()
    {
        Rows(new[] { "Alpha row" }, null);
        await Build().ClassifyAsync("s", "b");

        Rows(new[] { "Alpha row (renamed)", "Gamma row" }, null);
        await Build().ClassifyAsync("s", "b");

        Property(_calls[0].Schema, "triageCategory").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Alpha row");
        Property(_calls[1].Schema, "triageCategory").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Alpha row (renamed)", "Gamma row");
        _calls[1].SystemPrompt.Should().Contain("- Gamma row");
    }

    [Fact]
    public async Task Classify_TaxonomyIsAdditive_BasePromptAndExistingSchemaFieldsAreUnchanged()
    {
        await Build(taxonomyRef: null).ClassifyAsync("s", "b");
        Rows(new[] { "Alpha row" }, new Dictionary<string, string> { ["Alpha row"] = "g" });
        await Build().ClassifyAsync("s", "b");

        var (offPrompt, offSchema) = _calls[0];
        var (onPrompt, onSchema) = _calls[1];

        onPrompt.Should().StartWith(offPrompt.TrimEnd(), "the code-constant prompt is kept verbatim; the taxonomy is appended");
        foreach (var field in new[] { "candidateRecordTypes", "category", "urgency", "obligations", "suggestedActions", "privilegeFlagged", "rationale" })
        {
            System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(Property(onSchema, field).GetRawText()),
                    System.Text.Json.Nodes.JsonNode.Parse(Property(offSchema, field).GetRawText()))
                .Should().BeTrue($"'{field}' keeps its contract for its existing consumers");
        }
        Property(onSchema, "category").GetProperty("type").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(new[] { "string", "null" }, "category stays free-form (the review UI keys on values like 'invoice')");
    }

    [Fact]
    public async Task Classify_WhenTaxonomyTurnedOff_SendsThePreD117PromptAndSchema_AndReadsNothing()
    {
        await Build(taxonomyRef: "").ClassifyAsync("s", "b");

        var (prompt, schema) = _calls.Should().ContainSingle().Subject;
        prompt.Should().NotContain("triageCategory");
        JsonDocument.Parse(schema).RootElement.GetProperty("properties").TryGetProperty("triageCategory", out _).Should().BeFalse();
        Required(schema).Should().NotContain("triageCategory");
        _dataverse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Classify_WhenTaxonomyResolvesNoRows_FallsBackToThePreD117PromptAndSchema()
    {
        Rows(Array.Empty<string>(), null);

        var result = await Build().ClassifyAsync("s", "b");

        var (prompt, schema) = _calls.Should().ContainSingle("a taxonomy read problem never blocks classification").Subject;
        prompt.Should().NotContain("Triage taxonomy:");
        JsonDocument.Parse(schema).RootElement.GetProperty("properties").TryGetProperty("triageCategory", out _).Should().BeFalse();
        result!.TriageCategory.Should().BeNull();
    }

    [Fact]
    public async Task Classify_WhenGuidanceReadFails_StillListsTheNamesAndConstrainsTheEnum()
    {
        _dataverse
            .Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Alpha row", "Beta row" });
        _dataverse
            .Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("guidance column read failed"));

        await Build().ClassifyAsync("s", "b");

        var (prompt, schema) = _calls.Should().ContainSingle().Subject;
        prompt.Should().Contain("- Alpha row").And.Contain("- Beta row");
        Property(schema, "triageCategory").GetProperty("enum").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("Beta row", "Beta row")]
    [InlineData("beta ROW", "Beta row")]
    [InlineData("Not a row", null)]
    [InlineData(null, null)]
    public async Task Classify_ReturnsTriageCategoryAsACanonicalTaxonomyNameOrNull(string? modelSays, string? expected)
    {
        Rows(new[] { "Alpha row", "Beta row" }, null);
        _modelReturns = new CommunicationClassificationResult { Category = "general-correspondence", TriageCategory = modelSays };

        var result = await Build().ClassifyAsync("s", "b");

        result!.TriageCategory.Should().Be(expected);
        result.Category.Should().Be("general-correspondence", "the free-form category is passed through untouched");
    }

    [Fact]
    public async Task Classify_WhenTwoRowsShareAName_TheEnumHasNoDuplicate()
    {
        Rows(new[] { "Alpha row", "Alpha row", "Beta row" },
            new Dictionary<string, string> { ["Alpha row"] = "g" });

        await Build().ClassifyAsync("s", "b");

        var (prompt, schema) = _calls.Should().ContainSingle().Subject;
        Property(schema, "triageCategory").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Alpha row", "Beta row");
        prompt.Split('\n').Count(l => l.StartsWith("- Alpha row", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Classify_WhenModelReturnsNull_ReturnsNull()
    {
        Rows(new[] { "Alpha row" }, null);
        _modelReturns = null;

        var result = await Build().ClassifyAsync("s", "b");

        result.Should().BeNull();
    }
}
