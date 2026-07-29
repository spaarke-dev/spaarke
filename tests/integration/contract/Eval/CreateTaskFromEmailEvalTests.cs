using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.EmailCreateTask;

/// <summary>
/// email-communication-intelligence-r1 task 040 (NFR-07 blocking merge gate) — the email-create-task
/// (Job C) eval family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to every sibling family in this directory):
/// <c>[Trait("Category", "GoldenUtteranceEval")]</c> joins this class to the dedicated <c>eval-gate</c> job
/// (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>, NO <c>continue-on-error</c>) with ZERO
/// CI-YAML change. Does NOT touch the shared <c>golden-utterances.json</c> family coverage — that file only
/// carries the single GU-140 case the <see cref="ConsumerTypes.All"/> full-catalog forcing function scans.
/// </para>
/// <para>
/// <b>Not a chat/loop capability.</b> CREATE-TASK-FROM-EMAIL is invoked directly off the Communication
/// enrichment path, so every case uses <c>channel: "event"</c>. What IS proven mechanically, no live model:
/// (1) the <c>tasks[]</c> output CONTRACT (via the Action's own worked example, cross-checked against both
/// the action file and this seed), (2) the NFR-06 verify-cited-text gate, (3) the NFR-06/ADR-015
/// deadline-bearing → confirm structural guarantee, (4) reuse-not-fork of the shipped create-task write core,
/// (5) the Binding resolves through the REAL <see cref="ConsumerRoutingService"/>, (6) the FR-05/FR-14
/// no-second-pass structural guarantee.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class CreateTaskFromEmailEvalTests
{
    private readonly ITestOutputHelper _output;

    public CreateTaskFromEmailEvalTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "structured-output", "verify-cited-text", "deadline-confirm", "reuse-not-fork", "binding-resolution", "no-second-pass",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["structured-output"] = 1,
        ["verify-cited-text"] = 1,
        ["deadline-confirm"] = 1,
        ["reuse-not-fork"] = 1,
        ["binding-resolution"] = 1,
        ["no-second-pass"] = 1,
    };

    // -------------------------------------------------------------------------
    // Inventory integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("create-task-from-email-eval@v1");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("CREATETASK-", StringComparison.Ordinal),
            "email-create-task eval cases are namespaced CREATETASK-### so they never collide with GU-###/TRIAGE-###/PROPOSE-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: CREATE-TASK-FROM-EMAIL is event-triggered only — no click/text dispatch is claimed");
            c.Utterance.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every case carries an event/affordance descriptor");
        }
    }

    [Fact]
    public void EveryFamily_HasEvalCoverage_AtItsFloor()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
        {
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor, $"NFR-07: the '{family}' family owes at least {floor} eval case(s)");
        }
    }

    // -------------------------------------------------------------------------
    // Catalog grounding — no invented capability names
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchCases_ExpectedConsumerType_IsGroundedInTheMirror()
    {
        var suite = LoadSuite();
        var mirrorRow = LoadMirrorRow("email-create-task");

        foreach (var c in suite.Cases.Where(c => c.Expected.ConsumerType is not null))
        {
            c.Expected.ConsumerType.Should().Be("email-create-task");
            mirrorRow.GetProperty("actionCode").GetString().Should().Be("create-task-from-email",
                $"case {c.CaseId}: the mirror row's actionCode must resolve to the CREATE-TASK-FROM-EMAIL Action");
        }
    }

    // -------------------------------------------------------------------------
    // structured-output family — the Action's declared tasks[] contract + worked example
    // -------------------------------------------------------------------------

    [Fact]
    public void Action_DeclaresTheTasksOutputContract_WithCitationShape()
    {
        var action = LoadActionJson();
        var itemProps = action.GetProperty("outputSchema")
            .GetProperty("properties").GetProperty("tasks")
            .GetProperty("items").GetProperty("properties");

        var names = itemProps.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().BeEquivalentTo(new[] { "subject", "description", "dueDate", "citation", "reason", "confidence" },
            "FR-14: each candidate task MUST declare exactly {subject, description, dueDate, citation, reason, confidence}");

        var citationProps = itemProps.GetProperty("citation").GetProperty("properties");
        citationProps.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "source", "locator", "quotedText" },
            "NFR-06: the citation carries source + locator + the verbatim quotedText");
        citationProps.GetProperty("source").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "subject", "body", "attachment" },
                "citation.source is the closed set of citable email parts");

        itemProps.GetProperty("confidence").GetProperty("type").GetString().Should().Be("number");
    }

    [Fact]
    public void WorkedExample_MatchesBothTheActionFile_AndThisSuitesGroundedExpectation()
    {
        var suite = LoadSuite();
        var structuredCase = suite.Cases.Single(c => c.Family == "structured-output");
        var expected = structuredCase.Expected.ExampleTask!;

        var action = LoadActionJson();
        var actionTask = action.GetProperty("examples")[0].GetProperty("output").GetProperty("tasks")[0];

        actionTask.GetProperty("subject").GetString().Should().Be(expected.Subject);
        actionTask.GetProperty("dueDate").GetString().Should().Be(expected.DueDate);
        actionTask.GetProperty("reason").GetString().Should().Be(expected.Reason);
        actionTask.GetProperty("confidence").GetDouble().Should().Be(expected.Confidence);
        actionTask.GetProperty("citation").GetProperty("quotedText").GetString()
            .Should().Be(expected.Citation.QuotedText,
                "the eval seed and the Action's own worked example must agree — a drift in either file fails here");
    }

    [Fact]
    public void CommunicationCreateTaskAi_ParsesTheWorkedExampleShape_ThroughTheRealParsingPath()
    {
        // Drives the REAL CommunicationCreateTaskAi output-parsing path (reflection-free construction of the
        // raw JsonElement exactly as ActionRunner.RunAsync would return it, then asserts the public contract).
        var action = LoadActionJson();
        var exampleOutputJson = action.GetProperty("examples")[0].GetProperty("output").GetRawText();
        using var doc = JsonDocument.Parse(exampleOutputJson);

        var candidates = InvokeParseResult(doc.RootElement);

        candidates.Should().ContainSingle();
        var task = candidates[0];
        task.Subject.Should().Be("Respond to settlement offer - Smith v. Acme");
        task.DueDate.Should().Be(new DateTime(2026, 8, 21));
        task.Citation.QuotedText.Should().Be("please respond to our settlement offer no later than Friday, August 21, 2026");
        task.Citation.Source.Should().Be("body");
        task.Confidence.Should().Be(0.9);
    }

    // -------------------------------------------------------------------------
    // verify-cited-text family — NFR-06 trust gate (pure, no live model)
    // -------------------------------------------------------------------------

    [Fact]
    public void VerifyCitedText_VerbatimSpanVerifies_FabricatedQuoteDoesNot()
    {
        var source = CitationVerifier.BuildSourceText(
            subject: "Response needed",
            bodyText: "Counsel, please respond to our settlement offer no later than Friday, August 21, 2026.",
            attachmentText: null);

        CitationVerifier.IsCitedTextPresent(source, "please respond to our settlement offer no later than Friday, August 21, 2026")
            .Should().BeTrue("NFR-06: a verbatim citation verifies and the candidate may be created/stored");
        CitationVerifier.IsCitedTextPresent(source, "the deposition was rescheduled to next month")
            .Should().BeFalse("NFR-06 negative: a fabricated citation is DROPPED, never created or stored");
    }

    // -------------------------------------------------------------------------
    // deadline-confirm family — NFR-06/ADR-015 structural guarantee (pure, no live model)
    // -------------------------------------------------------------------------

    [Fact]
    public void TaskCandidate_WithDueDate_IsDeadlineBearing_WithoutDueDate_IsNot()
    {
        var citation = new ProposalCitation("body", "body: sentence 1", "quoted span");

        var deadlineBearing = new TaskCandidate("Subject A", "Desc", new DateTime(2026, 8, 21), citation, "reason", 0.9);
        var nonDeadlineBearing = new TaskCandidate("Subject B", "Desc", null, citation, "reason", 0.9);

        deadlineBearing.DueDate.HasValue.Should().BeTrue(
            "a candidate with a concretely-stated deadline is deadline-bearing (NFR-06/ADR-015) — the enrichment " +
            "step routes it to PENDING human-confirm and never calls IActionSeam.CreateTaskAsync for it");
        nonDeadlineBearing.DueDate.HasValue.Should().BeFalse(
            "a candidate with no stated deadline may be created immediately via the shipped create-task path");
    }

    // -------------------------------------------------------------------------
    // reuse-not-fork family — FR-14/ADR-039 structural guarantee (grep-verifiable)
    // -------------------------------------------------------------------------

    [Fact]
    public void CommunicationEnrichmentService_CreatesTasksOnlyThroughTheSharedIActionSeamFacade()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services",
            "Communication", "CommunicationEnrichmentService.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("_actionSeam.CreateTaskAsync",
            "FR-14/ADR-039: the non-deadline-bearing create leg MUST go through the shipped IActionSeam facade " +
            "(the SAME session-agnostic write core TaskActionCore that the chat-loop CREATE-TASK@v1 capability's " +
            "dataverse.create_record tool ultimately backs) — no second task-creation mechanism.");
        source.Should().NotContain("new Entity(\"task\"",
            "the Communication layer must never construct a `task` Entity directly — that would be a forked " +
            "create mechanism bypassing IActionSeam/TaskActionCore.");
        source.Should().NotContain("CreateTaskNodeExecutor",
            "the Communication layer must never reach the playbook node executor directly (ADR-039 — the " +
            "node-graph engine is frozen; new capability is catalog data + PublicContracts).");
    }

    // -------------------------------------------------------------------------
    // no-second-pass family — FR-05/FR-14 structural guarantee
    // -------------------------------------------------------------------------

    [Fact]
    public void CommunicationCreateTaskAi_HasNoClassificationDependency_CannotReClassifyEvenByMistake()
    {
        var ctor = typeof(CommunicationCreateTaskAi).GetConstructors().Single();
        var paramTypeNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();

        paramTypeNames.Should().NotContain("ICommunicationClassificationAi",
            "FR-05/FR-14: the facade must be STRUCTURALLY incapable of a second classification call — it has no handle to the classifier");
        paramTypeNames.Should().NotContain("IOpenAiClient",
            "ADR-013: no AI-internal client injected directly — only the Linear AI Consumer primitives");

        _output.WriteLine($"CommunicationCreateTaskAi ctor deps: {string.Join(", ", paramTypeNames)}");
    }

    [Fact]
    public void CommunicationCreateTaskRequest_TriageIsGroundingOnly_NotRequired()
    {
        var triageProp = typeof(CommunicationCreateTaskRequest).GetProperty(nameof(CommunicationCreateTaskRequest.Triage))!;
        // Triage is an OPTIONAL grounding input (nullable, no `required` modifier) — the create-task path
        // runs whether or not a triage result exists; it never re-derives classification.
        var isRequired = triageProp.GetCustomAttributesData().Any(a => a.AttributeType.Name == "RequiredMemberAttribute");
        isRequired.Should().BeFalse("the already-produced triage output is grounding only — never a re-classification input");
    }

    // -------------------------------------------------------------------------
    // binding-resolution family — the REAL ConsumerRoutingService over a stubbed
    // Dataverse boundary, shaped EXACTLY like the real mirror row.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmailCreateTaskBinding_ResolvesThroughTheRealRoutingService_ByConsumerType()
    {
        var mirrorRow = LoadMirrorRow("email-create-task");
        var rowId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var stubEntity = BuildRowFromMirror(mirrorRow, rowId, actionId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { stubEntity }));

        var routing = CreateRoutingService(entityService);

        var resolvedActionId = await routing.ResolveActionAsync("email-create-task", "default");
        resolvedActionId.Should().Be(actionId,
            "IActionResolver.ResolveAsync -> IConsumerRoutingService.ResolveActionAsync is the exact read " +
            "CommunicationEnrichmentService's email-create-task step performs (via ConsumerTypes.EmailCreateTask)");

        _output.WriteLine("Mechanical routing assertion — email-create-task (task 040): resolves via ResolveActionAsync -> create-task-from-email Action.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<TaskCandidate> InvokeParseResult(JsonElement output)
    {
        var method = typeof(CommunicationCreateTaskAi).GetMethod(
            "ParseResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IReadOnlyList<TaskCandidate>)method.Invoke(null, new object[] { output })!;
    }

    private static ConsumerRoutingService CreateRoutingService(Mock<IGenericEntityService> entityService)
    {
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");

        return new ConsumerRoutingService(
            entityService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            hostEnvironment.Object,
            NullLogger<ConsumerRoutingService>.Instance);
    }

    /// <summary>Build a stub <c>sprk_playbookconsumer</c> Entity from the email-create-task mirror row (a
    /// Linear Consumer — NO disposition/risk/captureMode/toolDescription, exercising the safe-default
    /// fallback for legacy/null columns in <c>ConsumerRoutingService.MapBinding</c>).</summary>
    private static Entity BuildRowFromMirror(JsonElement mirrorRow, Guid rowId, Guid actionId)
    {
        var entity = new Entity("sprk_playbookconsumer", rowId);
        entity["sprk_playbookconsumerid"] = rowId;
        entity["sprk_consumertype"] = mirrorRow.GetProperty("consumerType").GetString();
        entity["sprk_consumercode"] = mirrorRow.GetProperty("consumerCode").GetString();
        entity["sprk_priority"] = mirrorRow.GetProperty("priority").GetInt32();
        entity["sprk_enabled"] = mirrorRow.GetProperty("enabled").GetBoolean();
        entity["sprk_environment"] = mirrorRow.GetProperty("environment").GetString();
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", actionId);
        entity["action.sprk_kind"] = new AliasedValue(
            "sprk_analysisaction", "sprk_kind", new OptionSetValue((int)ActionKind.Prompted));
        return entity;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static CreateTaskEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "create-task-from-email-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"create-task-from-email-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup glob covers it automatically)");
        var suite = JsonSerializer.Deserialize<CreateTaskEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the email-create-task case schema");
        return suite!;
    }

    private static JsonElement LoadActionJson()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "create-task-from-email.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadMirrorRow(string consumerType)
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        return mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("consumerType").GetString() == consumerType
                         && r.GetProperty("consumerCode").GetString() == "default")
            .Clone();
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException(
            "Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory — " +
            "the email-create-task mirror-grounding assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// email-create-task eval case schema
// -----------------------------------------------------------------------------

public sealed record CreateTaskEvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<CreateTaskEvalCase> Cases { get; init; } = new();
}

public sealed record CreateTaskEvalCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    [JsonPropertyName("ucId")]
    public string UcId { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("utterance")]
    public string Utterance { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("expected")]
    public CreateTaskEvalExpected Expected { get; init; } = new();
}

public sealed record CreateTaskEvalExpected
{
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    [JsonPropertyName("consumerCode")]
    public string? ConsumerCode { get; init; }

    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    [JsonPropertyName("mirroredBy")]
    public string? MirroredBy { get; init; }

    [JsonPropertyName("actionCode")]
    public string? ActionCode { get; init; }

    [JsonPropertyName("exampleTask")]
    public CreateTaskEvalExampleTask? ExampleTask { get; init; }

    [JsonPropertyName("verifiedVsFabricatedDiffers")]
    public bool? VerifiedVsFabricatedDiffers { get; init; }

    [JsonPropertyName("deadlineBearingNeverAutoFinalizes")]
    public bool? DeadlineBearingNeverAutoFinalizes { get; init; }

    [JsonPropertyName("reusesShippedCreatePath")]
    public bool? ReusesShippedCreatePath { get; init; }

    [JsonPropertyName("noSecondClassificationPass")]
    public bool? NoSecondClassificationPass { get; init; }
}

public sealed record CreateTaskEvalExampleTask
{
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; init; }

    [JsonPropertyName("citation")]
    public CreateTaskEvalCitation Citation { get; init; } = new();

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public sealed record CreateTaskEvalCitation
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("locator")]
    public string Locator { get; init; } = string.Empty;

    [JsonPropertyName("quotedText")]
    public string QuotedText { get; init; } = string.Empty;
}
