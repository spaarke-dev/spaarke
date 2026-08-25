// Task 051 (spaarkeai-compose-r8, FR-C03) — the ACTION-SCHEMA ↔ ANCHOR-CONSUMER parity slice.
//
// WHY THIS TEST EXISTS. During task 051 the anchor CONSUMER was built first — server and client could
// both accept a `target_para_id` and place an edit deterministically, with green tests — while every
// edit-producing Action still declared only `target_text` in its output schema. Those schemas run under
// Azure OpenAI Structured Outputs with `additionalProperties: false`, so the model was structurally
// INCAPABLE of returning the field the consumer was waiting for. The consumer was unreachable and the
// tests could not see it, because nothing tied the catalog contract to the type that consumes it.
//
// This is that tie. It fails if either side moves:
//   - the Action stops declaring `target_para_id` (or renames it) → the model can no longer answer,
//   - `ProposedEdit`'s JSON name changes → the model's answer stops binding,
//   - the schema stops being valid OpenAI-subset JSON Schema → the request 400s at runtime, which no
//     amount of C#-side testing would otherwise reveal.
//
// SCOPE. Only the three SELECTION-SCOPED Actions are asserted to carry the anchor. `compose-revise-document`
// is the whole-document pass: it has no selection, so it needs an enumerated paragraph LIST rather than a
// single id, and it is covered by its own task. The negative below pins that boundary deliberately so the
// omission reads as a decision rather than an oversight.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam"): tests/integration/seam/**. Drives the REAL
// catalog seed files, the REAL schema validator, the REAL JSON binding, and the REAL anchor pass over a
// REAL corpus projection. No mocks.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeEditActionAnchorContractSeamTests
{
    /// <summary>The three Actions whose dispatch sites supply `targetParaId` today (task 051).</summary>
    public static TheoryData<string> AnchoredEditActions() => new()
    {
        "compose-draft-alternative",
        "compose-make-concise",
        "compose-rewrite-instruction",
    };

    private static JsonElement OutputSchema(string actionCode)
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json");
        File.Exists(path).Should().BeTrue($"the catalog seed for '{actionCode}' must exist at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("outputSchema").Clone();
    }

    private static string SystemPrompt(string actionCode)
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("systemPrompt").GetString() ?? string.Empty;
    }

    /// <summary>Walks up from the test binary to the repo root (the directory holding `infra/`).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "infra", "dataverse", "actions")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to locate the repo's infra/dataverse/actions directory");
        return dir!.FullName;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The catalog side — the model must be ABLE to answer with an anchor
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AnchoredEditActions))]
    public void EditAction_DeclaresTargetParaId_SoStructuredOutputsCanReturnIt(string actionCode)
    {
        var schema = OutputSchema(actionCode);

        schema.GetProperty("properties").TryGetProperty("target_para_id", out var prop)
            .Should().BeTrue("without the property, additionalProperties:false makes the anchor unreturnable");

        // Structured Outputs requires EVERY property to appear in `required`; nullability is expressed by
        // the type union, not by omission from that list.
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("target_para_id");

        prop.GetProperty("type").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "string", "null" },
                "null is the honest answer when no anchor was supplied — which is what keeps the legacy "
                + "target_text path reachable for anchorless callers instead of failing the request");
    }

    [Theory]
    [MemberData(nameof(AnchoredEditActions))]
    public void EditAction_OutputSchema_IsValidOpenAiSubset(string actionCode)
    {
        // A malformed schema 400s at request time — a failure mode no C#-side test would otherwise surface.
        OpenAiFunctionSchemaValidator.FindFirstError(OutputSchema(actionCode).GetRawText())
            .Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(AnchoredEditActions))]
    public void EditAction_Prompt_TellsTheModelToEchoTheIdentifierRatherThanInventOne(string actionCode)
    {
        var prompt = SystemPrompt(actionCode);

        prompt.Should().Contain("targetParaId", "the model must be told the input field exists");
        prompt.Should().Contain("target_para_id", "and which output field carries it");
        prompt.Should().Contain("VERBATIM",
            "echoing an identifier is a COPY operation; asking the model to reproduce it loosely would "
            + "reintroduce exactly the generation-is-lossy failure the anchor removes");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The INPUT side — the model must be TOLD the anchor before it can echo one back
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AnchoredEditActions))]
    public void EditAction_DeclaresTargetParaIdAsAnInput_SoContextBinderRendersItIntoThePrompt(string actionCode)
    {
        // ContextBinder renders a companion input ONLY when the Action DECLARES it (declaration is the
        // contract). Without this the arg is accepted and silently dropped before the prompt — the
        // model is then asked to echo an identifier it was never given, and answers null every time.
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        doc.RootElement.TryGetProperty("inputSchema", out var input)
            .Should().BeTrue("a null sprk_inputschema means no companion input can ever reach the model");

        var props = input.GetProperty("properties");
        props.TryGetProperty("selectionText", out _)
            .Should().BeTrue("declaring a schema also CONSTRAINS operand resolution — the operand must stay declared");
        props.TryGetProperty("targetParaId", out _)
            .Should().BeTrue("this is the field that carries the anchor to the model");
    }

    [Fact]
    public void ActionDeployScript_WritesTheInputSchemaColumn()
    {
        // The seed file is only half the contract: if the deploy script does not map `inputSchema` onto
        // `sprk_inputschema`, an authored declaration never leaves the repo and the runtime behaves as if
        // it were absent. That is a silent, environment-only failure no C#-side test would otherwise catch.
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Deploy-AnalysisAction.ps1"));

        script.Should().Contain("sprk_inputschema",
            "an authored inputSchema must actually be deployed, or the declaration is inert in every environment");
        script.Should().Contain("$action.inputSchema",
            "and it must be sourced from the seed file's own inputSchema member");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The binding side — the model's answer must reach the consumer, and place
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AnchoredEditActions))]
    public void ModelPayloadShapedByTheSchema_BindsToProposedEdit_AndPlacesAtTheAnchor(string actionCode)
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var target = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        // A payload with exactly the keys this Action's schema declares — the shape Structured Outputs
        // guarantees. Built from the schema's own property names, so a rename on either side fails here.
        var payload = BuildPayloadFromSchema(OutputSchema(actionCode), target);

        var edit = JsonSerializer.Deserialize<ProposedEdit>(payload);

        edit.Should().NotBeNull();
        edit!.TargetParaId.Should().Be(target, "the wire name must bind to the property the anchor pass reads");

        var result = ComposeEditAnchorPass.Validate(
            documentText: "irrelevant — the anchor must place without reading document prose",
            edits: new[] { edit },
            referenceMap: projection.ParaIdMap,
            textValidator: new ThrowIfTextSearched());

        result.IsValid.Should().BeTrue();
        result.Verdicts[0].ResolvedParaId.Should().Be(target);
    }

    [Fact]
    public void NullAnchor_StillBinds_AndFallsThroughToTheLegacyTextPath()
    {
        // The nullable half of the contract: an anchorless caller must keep working unchanged until the
        // retirement task lands. If this ever fails, `["string","null"]` was narrowed to `"string"`.
        var edit = JsonSerializer.Deserialize<ProposedEdit>(
            """{"target_text":"confidential","target_para_id":null,"new_text":"secret","match_mode":"strict"}""");

        edit!.TargetParaId.Should().BeNull();

        var recorder = new RecordingTextValidator();
        ComposeEditAnchorPass.Validate(
            "The Receiving Party shall keep the information confidential.",
            new[] { edit }, referenceMap: null, textValidator: recorder);

        recorder.Seen.Should().ContainSingle("a null anchor is not an anchor — the legacy path still owns it");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The whole-document pass (task 054) — anchored by a CLOSED SET rather than a single id
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    //
    // Task 051 left this Action deliberately unanchored and pinned the omission, because requiring an id
    // the model was never given would refuse every whole-document edit — strictly worse than the prose
    // matching it replaces. Task 054 supplies the set, so the pin is replaced by its positive form.
    //
    // The set is NOT a separate list. It is the document text itself, annotated with each paragraph's
    // paraId, supplied as the `documentText` operand by the Compose editor pane (which is the only holder
    // of the COMPLETE current set — the server's ChatSession.ReferenceMap is a Load-time snapshot that
    // omits paragraphs typed since, and carries no text to annotate with). Ids must appear where the
    // content is read: naming one is then a COPY, whereas matching a paragraph to an id in a side list is
    // a generation, which is the lossy step Track C exists to delete.
    // Full reasoning: projects/spaarkeai-compose-r8/notes/054-closed-set-supply-decision.md

    /// <summary>The two anchored item channels a whole-document revision emits.</summary>
    public static TheoryData<string> WholeDocumentAnchoredChannels() => new() { "edits", "comments" };

    [Theory]
    [MemberData(nameof(WholeDocumentAnchoredChannels))]
    public void WholeDocumentReviseAction_DeclaresTargetParaIdOnEveryAnchoredChannel(string channel)
    {
        // `comments` matters as much as `edits`: flag-risks emits an EMPTY edits array by contract, so the
        // highest-volume whole-document capability is 100% comment-anchored. Leaving comments on prose
        // would leave that capability entirely on the path this project is retiring.
        var items = OutputSchema("compose-revise-document")
            .GetProperty("properties").GetProperty(channel).GetProperty("items");

        items.GetProperty("properties").TryGetProperty("target_para_id", out var prop)
            .Should().BeTrue($"additionalProperties:false makes the anchor unreturnable on {channel}[] without it");

        items.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("target_para_id",
                "Structured Outputs requires EVERY property in `required`; nullability rides the type union");

        prop.GetProperty("type").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "string", "null" },
                "null is the honest answer when the model could not identify a paragraph — it keeps the "
                + "legacy text path reachable for that item instead of failing the whole batch");
    }

    [Fact]
    public void WholeDocumentReviseAction_OutputSchema_IsValidOpenAiSubset()
    {
        // Adding a nested property to two array item schemas is exactly where an invalid-subset mistake
        // hides — and it would 400 at request time, not in any C#-side assertion.
        OpenAiFunctionSchemaValidator.FindFirstError(OutputSchema("compose-revise-document").GetRawText())
            .Should().BeNull();
    }

    [Fact]
    public void WholeDocumentReviseAction_DeclaresTheOperandAndItsCompanions()
    {
        // Declaration is the contract (ADR-043 Amendment 1). Two distinct consequences here:
        //   - `documentText` declared + supplied moves this dispatch onto the STRUCTURED-operand path.
        //     Without it, HasStructuredOperand is false, the file-operand path runs, and that path passes
        //     NO Args and NO InputSchemaJson to ContextBinder — so nothing the caller sent reaches the
        //     prompt at all.
        //   - `revisionIntent` / `instruction` then ride as declared companions. They do not reach the
        //     model today, which is why the systemPrompt's four INSTRUCTIONS-BY-INTENT branches cannot
        //     currently be selected.
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", "compose-revise-document.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        doc.RootElement.TryGetProperty("inputSchema", out var input)
            .Should().BeTrue("a null sprk_inputschema means no companion input can ever reach the model");

        var props = input.GetProperty("properties");
        props.TryGetProperty("documentText", out _)
            .Should().BeTrue("the annotated document IS the closed set, and it is the declared operand");
        props.TryGetProperty("revisionIntent", out _)
            .Should().BeTrue("without it the model cannot tell flag-risks from improve-clarity");
        props.TryGetProperty("instruction", out _)
            .Should().BeTrue("the custom intent is defined entirely by this field");
    }

    [Fact]
    public void WholeDocumentReviseAction_Prompt_BindsTheModelToTheSuppliedIds()
    {
        var prompt = SystemPrompt("compose-revise-document");

        prompt.Should().Contain("target_para_id", "the model must be told which output field carries the anchor");
        prompt.Should().Contain("VERBATIM",
            "echoing an id is a COPY; asking for it loosely reintroduces generation-is-lossy");
        prompt.Should().Contain("revisionIntent",
            "the intent now reaches the model as a declared input, so the prompt must name it as such");
        prompt.Should().Contain("CLOSED SET",
            "the model must be bound to ids that appear in the supplied text — an invented id is refused, "
            + "so an unbounded prompt would produce silent per-item failures instead of anchored edits");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Any call means an anchored edit reached the text-search leg — the FR-C01/C02 regression.</summary>
    private sealed class ThrowIfTextSearched : IComposeEditValidator
    {
        public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
            => throw new InvalidOperationException(
                "Text search was invoked for an edit carrying a deterministic anchor.");
    }

    private sealed class RecordingTextValidator : IComposeEditValidator
    {
        private readonly ComposeEditValidator _real = new();
        public List<ProposedEdit> Seen { get; } = new();

        public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
        {
            Seen.AddRange(edits);
            return _real.Validate(documentText, edits);
        }
    }

    private static ComposeDocxProjection ProjectCorpus(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(Path.Combine(corpusDir, fileName));
        return new ComposeDocxProjectionBuilder().Build(bytes);
    }

    /// <summary>
    /// Emits a JSON object carrying exactly the scalar keys the Action's schema declares — so the payload
    /// under test is derived from the catalog contract rather than hand-copied from it. `sources` (the one
    /// array property) is emitted empty, which is what the schema itself describes for an ungrounded edit.
    /// </summary>
    private static string BuildPayloadFromSchema(JsonElement schema, string paraId)
    {
        var parts = new List<string>();
        foreach (var prop in schema.GetProperty("properties").EnumerateObject())
        {
            var value = prop.Name switch
            {
                "target_para_id" => JsonSerializer.Serialize(paraId),
                "match_mode" => "\"strict\"",
                "sources" => "[]",
                _ when IsArray(prop.Value) => "[]",
                _ => JsonSerializer.Serialize($"value-for-{prop.Name}"),
            };
            parts.Add($"{JsonSerializer.Serialize(prop.Name)}:{value}");
        }

        return "{" + string.Join(",", parts) + "}";
    }

    private static bool IsArray(JsonElement propSchema)
        => propSchema.TryGetProperty("type", out var t)
           && ((t.ValueKind == JsonValueKind.String && t.GetString() == "array")
               || (t.ValueKind == JsonValueKind.Array
                   && t.EnumerateArray().Any(x => x.GetString() == "array")));
}
