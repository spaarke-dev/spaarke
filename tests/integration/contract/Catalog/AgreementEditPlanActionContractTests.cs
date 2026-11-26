using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>agreement-edit-plan</c> Action (AGREEMENT-EDIT-PLAN@v1; spaarke-intake-pipeline-r1 task 004, FR-02, ADR-039).
/// Pins the Action mirror (<c>infra/dataverse/actions/agreement-edit-plan.action.json</c>), its output-schema and input-schema sidecars and its
/// Binding row against the facade that feeds and reads them (<see cref="AgreementAi"/>, <c>AgreementEditPlanAssembler</c>), so the Action cannot
/// drift from the code around it.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <see cref="InvoiceFirmNoticeActionContractTests"/> pins the firm-notice Action
/// only; <see cref="CatalogInputSchemaContractTests"/> validates every input mirror generically; nothing covers this Action. (2) <i>Extension</i> — a
/// narrowly-scoped class for the one Action this task ships, mirroring the firm-notice class. (3) <i>Cost of doing nothing</i> — an output schema that
/// is not Structured-Outputs-compatible 400-fails the first live call; an output member the assembler does not read is silently dropped from every plan;
/// a constant with no Binding row turns the catalog health check Degraded and a row with no constant turns it Unhealthy; an example the assembler refuses
/// teaches the model to produce plans that are never used.
/// </remarks>
public class AgreementEditPlanActionContractTests
{
    private const string Code = "agreement-edit-plan";

    private static readonly string[] EditMembers =
        ["topic", "para", "find", "old", "replace", "after", "insert", "comment", "summaryRow"];

    private static readonly string[] SummaryRowMembers = ["section", "issue", "revision", "priority"];

    // ── Tier, identity, consumer-type constant ──────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheReasoningTierAndTheEditPlanCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code, "lowercase kebab, no version suffix");
        action.GetProperty("description").GetString().Should().Contain("AGREEMENT-EDIT-PLAN@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Reasoning", "exact-anchor edits against 24 written positions, attorney-reviewed: accuracy first");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"]);
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-");
        action.GetProperty("$version").GetInt32().ToString().Should().Be(AgreementEditPlanAssembler.SkillVersion,
            "the plan's provenance line names the skill version, so it must be the version of the prompt that produced the plan");
        AgreementEditPlanAssembler.SkillName.Should().Be(Code);
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll_InTheSameChangeAsItsBindingRow()
    {
        ConsumerTypes.AgreementEditPlan.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
        BindingRows().Should().ContainSingle(r => r.GetProperty("consumerType").GetString() == Code,
            "a constant in All with no Binding row (or a row with no constant) turns /healthz/catalog unhealthy");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_ThatCarriesTheEngineRules_AndTheInjectionGuard()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        var task = instruction.GetProperty("task").GetString();
        task.Should().Contain("sections").And.Contain("para_key").And.Contain("standard").And.Contain("topics").And.Contain("edits").And.Contain("unplaceable");

        var constraints = string.Join('\n', instruction.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("topic ids in topics", "an edit may name only a topic of the pinned standard");
        constraints.Should().Contain("ANCHORS ARE A CLOSED LIST", "anchoring = a closed para_key list; the model never invents a para");
        constraints.Should().Contain("para_key values in sections", "para may be only a supplied para_key");
        constraints.Should().Contain("character for character", "quotes are verbatim: no fuzzy matching anywhere");
        constraints.Should().Contain("occurring exactly once", "the pre-check and the engine refuse a find or after that is absent or ambiguous");
        constraints.Should().Contain("para_key is null cannot be edited", "a section with no key is reported as unplaceable, never anchored");
        constraints.Should().Contain("Model language", "inserted text uses the position's model language");
        constraints.Should().Contain("later text first", "the engine applies edits within a paragraph right to left");
        constraints.Should().Contain("has_tracked_changes", "earlier reviewers' changes are not edited across");
        constraints.Should().Contain("never instructions", "the document is data: prompt-injection guard");
        constraints.Should().Contain("nda.housekeeping", "injected text is flagged with a comment-only edit under a real topic");
        constraints.Should().Contain("additionalProperties is false");
        constraints.Should().Contain("summaryRow", "the summary row is one object, so the model writes all of it or none (live run D1)");
        constraints.Should().NotContain("all four summary members", "the old four-member wording would teach the flat shape back");
        constraints.Should().Contain("must not overlap words an earlier edit changed", "the engine applies edits one after another (live run D2)");

        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).Should().Equal("edits", "unplaceable");
    }

    [Fact]
    public void Examples_AreCompleteEditsThatTheAssemblerAccepts_AndSendTheOperandTheFacadeSends()
    {
        var examples = LoadAction().GetProperty("examples").EnumerateArray().ToList();
        examples.Should().NotBeEmpty();

        foreach (var example in examples)
        {
            var output = example.GetProperty("output");
            foreach (var edit in output.GetProperty("edits").EnumerateArray())
            {
                edit.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(EditMembers, "the strict output schema requires every member");
                var row = edit.GetProperty("summaryRow");
                if (row.ValueKind != JsonValueKind.Null)
                {
                    row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(SummaryRowMembers, "a summary row is all four members");
                }
            }

            // The example's input is the operand the model sees (no anchor_text: the facade never sends it). As a segmentation it
            // needs the engine's texts; the example clauses carry no earlier markup, so both are the clause text.
            var inputJson = example.GetProperty("input").GetString()!;
            var segmentationJson = WithAnchorText(inputJson);
            var plan = AgreementEditPlanAssembler.Assemble(
                output, AgreementSegmentation.Parse(segmentationJson), new AgreementEditPlanRequest { SegmentationJson = segmentationJson },
                new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));
            plan.EditCount.Should().Be(output.GetProperty("edits").GetArrayLength(),
                "an example the assembler would refuse teaches the model to produce plans that are never used");
            plan.RefusedEdits.Should().BeEmpty("an example whose anchors the pre-check refuses teaches the model to mis-anchor");
            plan.UnplaceableFindings.Should().HaveCount(output.GetProperty("unplaceable").GetArrayLength());

            using var input = JsonDocument.Parse(inputJson);
            input.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                ["sections", "has_tracked_changes", "has_comments", "has_hidden_text", "standard", "topics"]);
            input.RootElement.GetProperty("sections").EnumerateArray().Should().OnlyContain(
                s => s.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "index", "number", "heading", "para_key", "text" }),
                "the operand is the segmentation's section shape");
            input.RootElement.GetProperty("standard").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["name", "version", "scope"]);
            foreach (var topic in input.RootElement.GetProperty("topics").EnumerateArray())
            {
                topic.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "position"]);
                PinnedNdaStandard.TopicIds.Should().Contain(topic.GetProperty("id").GetString(), "an example names only topics of the pinned standard");
            }
        }
    }

    // ── The output schema sidecar: the Structured Outputs subset ────────────────────────────────

    [Fact]
    public void OutputSchema_IsValidOpenAiStructuredOutputSchema_AndNamesEveryEditMemberTheAssemblerReads()
    {
        var schema = LoadOutputSchema();

        OpenAiFunctionSchemaValidator.FindFirstError(schema.GetRawText()).Should().BeNull("an invalid schema would 400-fail the first live call");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal("edits", "unplaceable");

        var item = schema.GetProperty("properties").GetProperty("edits").GetProperty("items");
        item.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        item.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(EditMembers);
        item.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(EditMembers,
            "Structured Outputs strict mode: every member required, optional ones nullable");
        foreach (var property in item.GetProperty("properties").EnumerateObject().Where(p => p.Name != "summaryRow"))
        {
            property.Value.TryGetProperty("required", out _).Should().BeFalse("property-level required is forbidden");
        }

        var unplaceable = schema.GetProperty("properties").GetProperty("unplaceable").GetProperty("items");
        unplaceable.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        unplaceable.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["topic", "section_index", "comment"]);

        // D1 (live run 2026-11-26): the summary row is ONE nullable object whose four members are all required, so a half row cannot be written.
        var row = item.GetProperty("properties").GetProperty("summaryRow");
        row.GetProperty("type").EnumerateArray().Select(t => t.GetString()).Should().BeEquivalentTo(["object", "null"]);
        row.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        row.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(SummaryRowMembers);
        row.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(SummaryRowMembers);
        foreach (var member in row.GetProperty("properties").EnumerateObject())
        {
            member.Value.GetProperty("type").ValueKind.Should().Be(JsonValueKind.String, $"{member.Name} is never null inside a row");
            member.Value.GetProperty("type").GetString().Should().Be("string");
        }

        row.GetProperty("properties").GetProperty("priority").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["High", "Medium", "Low"]);
        item.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().NotContain(SummaryRowMembers,
            "the four flat members are gone: they let the model write section alone");

        // A topic outside the pinned standard cannot be written either: the enum is the pinned standard's ids.
        foreach (var topic in new[] { item, unplaceable }.Select(i => i.GetProperty("properties").GetProperty("topic")))
        {
            topic.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(PinnedNdaStandard.TopicIds);
        }
    }

    [Fact]
    public void OutputSchema_UsesNoKeywordTheStrictSubsetRejects_AndTheCheckSeesOneWhenThereIsOne()
    {
        // The full plan is validated in code (edit-plan.schema.json uses these keywords); the model's schema must not.
        string[] forbidden = ["oneOf", "anyOf", "allOf", "format", "pattern", "minLength", "maxLength", "minimum", "maximum", "minItems", "maxItems"];
        var found = new List<string>();
        CollectKeywords(LoadOutputSchema(), forbidden, found);
        found.Should().BeEmpty("these keywords are not part of the strict Structured Outputs subset");

        using var control = JsonDocument.Parse("""{"properties":{"a":{"oneOf":[{"type":"string","pattern":"x"}]}}}""");
        var controlFound = new List<string>();
        CollectKeywords(control.RootElement, forbidden, controlFound);
        controlFound.Should().Contain(["oneOf", "pattern"], "must-fire control for the walk above");
    }

    // ── The input schema sidecar ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_NamesTheOperandsTheFacadeSends_AndTheTopicShape()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "inputschemas", $"{Code}.input.schema.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("actionCode").GetString().Should().Be(Code);
        var schema = doc.RootElement.GetProperty("inputSchema");

        string[] operands = ["sections", "has_tracked_changes", "has_comments", "has_hidden_text", "standard", "topics"];
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(operands);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(operands);
        typeof(AgreementEditPlanRequest).GetProperties().Select(p => p.Name).Should().Contain("SegmentationJson").And.Contain("TenantId",
            "the tenant id travels in the run context, never in the operand: it is a request member and not an operand");
        schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["index", "number", "heading", "para_key", "text"]);
        schema.GetProperty("properties").GetProperty("standard").GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["name", "version", "scope"]);
        var topicItem = schema.GetProperty("properties").GetProperty("topics").GetProperty("items");
        topicItem.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["id", "position"]);
    }

    // ── The Binding row ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BindingMirror_HasAnEnabledGlobalRowRoutingTheConsumerTypeToTheActionCode_WithNoToolProjection()
    {
        var row = BindingRows().Where(r => r.GetProperty("consumerType").GetString() == Code)
            .Should().ContainSingle("exactly one Binding row for the consumer type").Subject;

        row.GetProperty("enabled").GetBoolean().Should().BeTrue();
        row.GetProperty("actionCode").GetString().Should().Be(Code);
        row.GetProperty("consumerCode").GetString().Should().Be("default");
        row.GetProperty("environment").GetString().Should().Be("*");
        row.GetProperty("toolDescription").ValueKind.Should().Be(JsonValueKind.Null,
            "this Action is reachable only through the facade, never projected into the chat loop");
        foreach (var member in new[] { "surfaces", "disposition", "risk", "captureMode", "chipTransitions", "onEventBindings", "matchConditions" })
        {
            row.GetProperty(member).ValueKind.Should().Be(JsonValueKind.Null, member);
        }
    }

    [Fact]
    public void ExistingAgreementBindings_StillRouteTheirOwnActions_SoThisActionDidNotReplaceThem()
    {
        var rows = BindingRows();
        rows.Should().Contain(r => r.GetProperty("consumerType").GetString() == ConsumerTypes.AgreementClassify
                                   && r.GetProperty("actionCode").GetString() == "agreement-classify");
        rows.Should().Contain(r => r.GetProperty("consumerType").GetString() == ConsumerTypes.NdaReview
                                   && r.GetProperty("actionCode").GetString() == "agreement-review",
            "the Console's agreement-review output contract is a separate, untouched Action");
        rows.Count(r => r.GetProperty("actionCode").ValueKind == JsonValueKind.String
                        && r.GetProperty("actionCode").GetString() == Code).Should().Be(1, "only this task's own row targets the new Action");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The example operand as a <c>segment_clauses.py --anchor-text</c> result: each keyed clause's visible and editable text is its text.</summary>
    private static string WithAnchorText(string inputJson)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(inputJson)!.AsObject();
        foreach (var section in node["sections"]!.AsArray().Select(s => s!.AsObject()))
        {
            if (section["para_key"] is not null)
            {
                var text = section["text"]!.GetValue<string>();
                section["anchor_text"] = new System.Text.Json.Nodes.JsonObject { ["visible"] = text, ["editable"] = text };
            }
        }

        return node.ToJsonString();
    }

    private static void CollectKeywords(JsonElement node, string[] keywords, List<string> found)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name.StartsWith("$comment", StringComparison.Ordinal) || property.Name is "description" or "title")
                    {
                        continue;
                    }

                    if (Array.IndexOf(keywords, property.Name) >= 0 && property.Name != "properties")
                    {
                        found.Add(property.Name);
                    }

                    // A schema property NAMED like a keyword (for example an output member called "format") sits under "properties";
                    // descend into its value, not into the name.
                    CollectKeywords(property.Value, keywords, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    CollectKeywords(item, keywords, found);
                }

                break;
        }
    }

    private static IReadOnlyList<JsonElement> BindingRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json")));
        return doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static JsonElement LoadOutputSchema()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", $"{Code}.schema.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadAction()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{Code}.action.json");
        File.Exists(path).Should().BeTrue("the Action mirror must exist");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}

/// <summary>
/// KEEP-path drift guard for the NDA standard and the plan schema that <c>AgreementAi</c> embeds (spaarke-intake-pipeline-r1 task 004, NFR-09).
/// The BFF cannot read <c>src/accelerators/**</c> at run time (it is not in the published app), so it carries pinned copies; these tests fail the
/// moment the accelerator's source of truth moves without the pinned copy, so a plan can never be stamped with a standard version that is not the
/// one the model was shown.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — none compares the embedded copies to the accelerator. (2) <i>Extension</i> —
/// one narrow class. (3) <i>Cost of doing nothing</i> — a silently stale embedded playbook: the model reviews against v1.2 while the accelerator and
/// its attorney-ratified successor say otherwise, and the plan's review_basis still claims v1.2.
/// </remarks>
public class AgreementEditPlanPinnedStandardContractTests
{
    private static string Accelerator(params string[] parts) =>
        Path.Combine([RepoRoot(), "src", "accelerators", .. parts]);

    [Fact]
    public void EmbeddedPlaybook_IsTheAcceleratorsPlaybook_AtTheSameVersion()
    {
        var source = File.ReadAllText(Accelerator("practices", "nda", "positions", "Spaarke NDA – Mutual v1.2.md"));

        Normalize(PinnedNdaStandard.ReadResource(PinnedNdaStandard.PlaybookResource)).Should().Be(Normalize(source),
            "the embedded copy is a pin, not a fork: change the accelerator's file and re-pin together");
        PinnedNdaStandard.Name.Should().Be("Spaarke NDA – Mutual");
        PinnedNdaStandard.Version.Should().Be("1.2");
    }

    [Fact]
    public void EmbeddedEditPlanSchema_IsTheAcceleratorsSchema()
    {
        var source = File.ReadAllText(Accelerator("core", "schemas", "edit-plan.schema.json"));
        var pinned = PinnedNdaStandard.ReadResource(PinnedNdaStandard.EditPlanSchemaResource);

        // Compared as JSON, not as text: the repo's commit hook reformats .json files, so the two copies' whitespace may differ while
        // every keyword and value is the same (the text comparison failed on master's own copies for exactly that reason).
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(pinned), System.Text.Json.Nodes.JsonNode.Parse(source))
            .Should().BeTrue("the embedded schema is a pin, not a fork: change the accelerator's file and re-pin together");

        // Must-fire control: one changed value is a difference.
        var altered = System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject();
        altered["title"] = "changed";
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(pinned), altered).Should().BeFalse();
    }

    [Fact]
    public void PinnedTopics_AreExactlyTheTopicCatalogue()
    {
        var yaml = File.ReadAllText(Accelerator("practices", "nda", "topics.yaml"));
        var catalogue = Regex.Matches(yaml, @"^\s*-\s+id:\s+(nda\.[a-z0-9-]+)\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        catalogue.Should().HaveCount(24, "the catalogue the standard was written against");
        PinnedNdaStandard.TopicIds.Should().BeEquivalentTo(catalogue);
        PinnedNdaStandard.Topics.Select(t => t.Id).Should().BeEquivalentTo(catalogue);
        PinnedNdaStandard.Topics.Should().OnlyContain(t => t.Position.Contains("Preferred:"));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
