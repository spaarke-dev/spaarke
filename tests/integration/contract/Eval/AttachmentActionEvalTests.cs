using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.AttachmentAction;

/// <summary>
/// email-communication-intelligence-r1 task 041 (FR-13 / NFR-07 blocking merge gate) — the
/// attachment-grounded action-extraction eval family. The heaviest eval obligation in r1: it must demonstrate
/// extraction of an action present ONLY in an attachment, cited to that attachment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to every sibling family in this directory):
/// <c>[Trait("Category", "GoldenUtteranceEval")]</c> joins this class to the dedicated <c>eval-gate</c> job
/// with ZERO CI-YAML change. FR-13 introduces NO new ConsumerType (it reuses task 040's
/// <c>create-task-from-email</c> Action + <c>ICommunicationCreateTaskAi</c> facade), so it does NOT touch the
/// shared <c>golden-utterances.json</c> <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.ConsumerTypes"/>
/// full-catalog forcing function — that scan is already satisfied by task 040's GU-140.
/// </para>
/// <para>
/// <b>What is proven mechanically, no live model:</b> (1) the machine-verified, code-derived attachment-locator
/// gate (<see cref="AttachmentActionGate.VerifyAgainstAttachment"/>) — an action present ONLY in an attachment
/// is verified to that attachment + page; (2) the NFR-08 cost gate
/// (<see cref="AttachmentActionGate.IsLikelyActionTrigger"/>); (3) reuse-not-fork (grep-verifiable — no new
/// Action / facade / ConsumerType / create mechanism); (4) the NFR-06/ADR-015 deadline-bearing → confirm reuse.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class AttachmentActionEvalTests
{
    private readonly ITestOutputHelper _output;

    public AttachmentActionEvalTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "attachment-only-locator", "cost-gate", "reuse-not-fork", "deadline-confirm",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["attachment-only-locator"] = 1,
        ["cost-gate"] = 1,
        ["reuse-not-fork"] = 1,
        ["deadline-confirm"] = 1,
    };

    // ── Inventory integrity ──────────────────────────────────────────────────────

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("attachment-action-eval@v1");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("ATTACHACTION-", StringComparison.Ordinal),
            "attachment-action eval cases are namespaced ATTACHACTION-### so they never collide with GU-###/CREATETASK-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: attachment-action is event-triggered only");
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

    [Fact]
    public void DispatchCases_ReuseTheCreateTaskFromEmailAction_NoInventedConsumer()
    {
        var suite = LoadSuite();
        foreach (var c in suite.Cases.Where(c => c.Expected.ConsumerType is not null))
        {
            c.Expected.ConsumerType.Should().Be("email-create-task",
                $"case {c.CaseId}: FR-13 REUSES task 040's create-task Binding — it does not invent a new ConsumerType");
        }
    }

    // ── attachment-only-locator family — the NFR-07 heart (REAL gate, no live model) ──

    [Fact]
    public void VerifyAgainstAttachment_ActionOnlyInAttachment_IsVerifiedToAttachmentAndDerivedPage()
    {
        // An action ("please countersign ... and return") stated ONLY in the attachment (page 2). The gate
        // locates the verbatim span and returns the code-derived page — this IS the FR-13 NFR-07 property.
        var attachment = new AttachmentExtractedText(
            "agreement.pdf", null,
            "Cover page. Master Services Agreement.\nSection 3. Please countersign the enclosed agreement and return it.",
            new[]
            {
                new ExtractedPage(1, "Cover page. Master Services Agreement."),
                new ExtractedPage(2, "Section 3. Please countersign the enclosed agreement and return it."),
            });

        var (present, page) = AttachmentActionGate.VerifyAgainstAttachment(
            attachment, "Please countersign the enclosed agreement and return it");

        present.Should().BeTrue("the cited action span is verbatim-present in the attachment");
        page.Should().Be(2, "the page is CODE-DERIVED by locating the span — cited to the attachment + page 2");

        // NEGATIVE: a span that appears only in the email body (not the attachment) is rejected — the gate is
        // attachment-scoped, so an action must genuinely live in the attachment to be cited to it.
        var (bodyOnlyPresent, _) = AttachmentActionGate.VerifyAgainstAttachment(
            attachment, "kindly review the cover letter attached separately");
        bodyOnlyPresent.Should().BeFalse("NFR-06: a span not verbatim-present in THIS attachment is not cited to it");

        // A page-straddling span is attachment-verified but has no single derivable page (locator degrades to
        // the file name only) — still honest: verified to the attachment, page unknown.
        var (straddlePresent, straddlePage) = AttachmentActionGate.VerifyAgainstAttachment(
            attachment, "Master Services Agreement. Section 3.");
        straddlePresent.Should().BeTrue("the span is verbatim-present across the page boundary");
        straddlePage.Should().BeNull("a page-straddling span is attachment-verified but has no single derivable page");

        // Casing + collapsed-whitespace differences do not defeat a genuine verbatim quote (same normalization
        // as the shipped CitationVerifier).
        var (normalizedPresent, normalizedPage) = AttachmentActionGate.VerifyAgainstAttachment(
            attachment, "please   COUNTERSIGN the enclosed AGREEMENT and return it");
        normalizedPresent.Should().BeTrue("whitespace/case normalization matches the shipped verify-cited-text gate");
        normalizedPage.Should().Be(2);

        _output.WriteLine("ATTACHACTION-001: action present only in attachment -> verified to attachment agreement.pdf p.2");
    }

    // ── cost-gate family — NFR-08 deterministic pre-filter (REAL gate) ──

    [Fact]
    public void IsLikelyActionTrigger_FlagsActionBearingAttachments_SkipsInertOnes()
    {
        AttachmentActionGate.IsLikelyActionTrigger("Please countersign the enclosed agreement and return it.")
            .Should().BeTrue("an action-bearing attachment earns an LLM extraction pass");
        AttachmentActionGate.IsLikelyActionTrigger("You must file your response no later than August 21, 2026.")
            .Should().BeTrue();
        AttachmentActionGate.IsLikelyActionTrigger("Exhibit A. Corporate org chart. Figure 1 shows the reporting lines.")
            .Should().BeFalse("NFR-08: an inert reference attachment is skipped — no LLM extraction pass");
    }

    // ── reuse-not-fork family — no new Action / facade / ConsumerType / create mechanism ──

    [Fact]
    public void AttachmentActionStep_ReusesShippedPrimitives_AndForksNothing()
    {
        var enrichmentSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Communication",
            "CommunicationEnrichmentService.cs"));

        enrichmentSource.Should().Contain("RunEmailAttachmentActionAsync", "the FR-13 step exists");
        // dotnet-10-upgrade-r1 task 020 (H2 R3) resolved ICommunicationCreateTaskAi from a per-operation
        // service scope (local `createTaskAi`) instead of a constructor-injected field (`_createTaskAi`) to
        // fix a captive-dependency (singleton→scoped) — behavior-preserving, verified by task 021. The FR-13
        // facade reuse is unchanged; only the accessor form changed field→scoped-local.
        enrichmentSource.Should().Contain("createTaskAi.ExtractAsync",
            "FR-13 REUSES the create-task-from-email extraction facade — no new extraction facade");
        enrichmentSource.Should().Contain("_actionSeam.CreateTaskAsync",
            "FR-13 REUSES the shipped IActionSeam create leg — no forked create mechanism");
        enrichmentSource.Should().NotContain("new Entity(\"task\"",
            "the Communication layer must never construct a task Entity directly (would bypass IActionSeam/TaskActionCore)");

        // No new Action file authored for attachment-action (the reused Action is create-task-from-email).
        var actionsDir = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions");
        Directory.EnumerateFiles(actionsDir, "*.json")
            .Select(Path.GetFileName)
            .Should().NotContain(f => f!.Contains("attachment", StringComparison.OrdinalIgnoreCase),
                "FR-13 authors NO new catalog Action — it reuses create-task-from-email.action.json (§11 reuse)");

        // No new routed ConsumerType constant (the audit `sprk_actor` label is a plain provenance string, not a
        // closed-catalog routing key, so it does not appear in ConsumerTypes.cs).
        var consumerTypesSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Ai", "PublicContracts", "ConsumerTypes.cs"));
        consumerTypesSource.Should().NotContain("attachment-action",
            "FR-13 introduces NO new closed-catalog ConsumerType — routing reuses EmailCreateTask");
    }

    // ── deadline-confirm family — NFR-06/ADR-015 reuse of the DueDate.HasValue branch ──

    [Fact]
    public void AttachmentActionStep_RoutesDeadlineBearingCandidates_ThroughTheConfirmBranch()
    {
        var enrichmentSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Communication",
            "CommunicationEnrichmentService.cs"));

        // The attachment step reuses the same deadline-bearing guarantee: a candidate.DueDate.HasValue is
        // stored PENDING and never reaches the create leg.
        enrichmentSource.Should().Contain("candidate.DueDate.HasValue",
            "NFR-06/ADR-015: a deadline-bearing attachment candidate is routed to human-confirm, never auto-finalized");
        _output.WriteLine("ATTACHACTION-004: deadline-bearing attachment candidate -> PENDING confirm branch (reused).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AttachmentActionEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "attachment-action-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"attachment-action-eval-cases.json must be copied to test output at {path} " +
            "(the contract-Eval ItemGroup glob in Sprk.Bff.Api.Tests.csproj covers it automatically)");
        var suite = JsonSerializer.Deserialize<AttachmentActionEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the attachment-action case schema");
        return suite!;
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
            "the attachment-action reuse-not-fork assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// attachment-action eval case schema
// -----------------------------------------------------------------------------

public sealed record AttachmentActionEvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<AttachmentActionEvalCase> Cases { get; init; } = new();
}

public sealed record AttachmentActionEvalCase
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
    public AttachmentActionEvalExpected Expected { get; init; } = new();
}

public sealed record AttachmentActionEvalExpected
{
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    [JsonPropertyName("actionCode")]
    public string? ActionCode { get; init; }

    [JsonPropertyName("attachmentScopedLocatorVerified")]
    public bool? AttachmentScopedLocatorVerified { get; init; }

    [JsonPropertyName("costGateSkipsInertAttachments")]
    public bool? CostGateSkipsInertAttachments { get; init; }

    [JsonPropertyName("reusesShippedCreatePath")]
    public bool? ReusesShippedCreatePath { get; init; }

    [JsonPropertyName("deadlineBearingNeverAutoFinalizes")]
    public bool? DeadlineBearingNeverAutoFinalizes { get; init; }
}
