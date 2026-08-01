using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Classification;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Classification;

/// <summary>
/// Unit tests for <see cref="AgreementTypeRegistryPromptAssembler"/> — the REGISTRY-DRIVEN "assembly
/// path" the <c>agreement-classify</c> classifier depends on (ai-advanced-capabilities-agreements-r1
/// task 020, FR-07 / design Lens 3d).
/// </summary>
/// <remarks>
/// Maintain-class (protects a concrete behavior, not scaffolding): the acceptance criterion "the type
/// list is assembled from the registry at runtime — adding a stub row extends the candidate set with
/// ZERO code change" is proven here by injecting a stub row and asserting it flows into the materialized
/// classifier prompt. If this behavior regresses (e.g. someone hardcodes the key set), the classifier
/// stops being type-agnostic and every future per-type pack silently fails to route — a concrete
/// contract failure this test catches. Pure logic (no I/O, no mocks), so it is a genuine unit test.
/// </remarks>
public class AgreementTypeRegistryPromptAssemblerTests
{
    private readonly AgreementTypeRegistryPromptAssembler _sut = new();

    // A representative registry snapshot PLUS a stub row that exists in NO shipped artifact — the stub
    // is the whole point: if it flows into the prompt with zero code change, the set is registry-driven.
    private static readonly AgreementTypeRow Nda = new(
        "nda", "NDA Confidentiality",
        "Agreement whose PRIMARY subject is confidentiality / non-disclosure obligations.",
        IsFallback: false, ConfidenceThreshold: null);

    private static readonly AgreementTypeRow General = new(
        "general", "General",
        "Fallback classification for any agreement that does not match a specific registered sub-domain.",
        IsFallback: true, ConfidenceThreshold: null);

    private static readonly AgreementTypeRow EmploymentNullCue = new(
        "employment", "Employment",
        ClassificationCue: null, IsFallback: false, ConfidenceThreshold: null);

    private static readonly AgreementTypeRow AssetPurchaseNullNameAndCue = new(
        "asset-purchase", Name: null,
        ClassificationCue: null, IsFallback: false, ConfidenceThreshold: null);

    private static readonly AgreementTypeRow FranchiseStub = new(
        "franchise-stub", "Franchise Agreement (STUB)",
        "A stub type injected only by this test to prove the classifier's key set extends with zero code.",
        IsFallback: false, ConfidenceThreshold: null);

    // ---------------------------------------------------------------------------------------------
    // The headline acceptance criterion (task 020 #4): a NEW registry row extends the injected key set
    // with ZERO code change — proven at the fixture level with a stub row.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildRegistryBlock_WithStubRow_InjectsTheStubKey_ProvingZeroCodeExtension()
    {
        var rows = new[] { Nda, General, FranchiseStub };

        var block = _sut.BuildRegistryBlock(rows);

        block.Should().Contain("\"franchise-stub\"",
            "a newly registered sprk_agreementtype row MUST extend the classifier's candidate key set " +
            "with no code change — the key set is data-driven from the registry");
        block.Should().Contain("Franchise Agreement (STUB)");
        block.Should().Contain("zero code");
    }

    [Fact]
    public void Materialize_RealClassifierAction_WithStubRow_CarriesStubIntoTheDispatchedPrompt()
    {
        // Ties the zero-code-extension proof to the SHIPPED agreement-classify Action prompt (drift-safe):
        // the assembler substitutes the registry block into the real systemPrompt that ActionRunner dispatches.
        var action = LoadAgreementClassifyAction();
        var rows = new[] { Nda, General, FranchiseStub };

        var materialized = _sut.Materialize(action, rows);

        materialized.SystemPrompt.Should().Contain("\"franchise-stub\"",
            "the stub row flows through the assembler into the exact prompt ActionRunner dispatches");
        materialized.SystemPrompt.Should().NotContain(AgreementTypeRegistryPromptAssembler.PlaceholderToken,
            "the placeholder token must be fully replaced by the registry block before dispatch");
        // Every other Action field is preserved so the materialized Action still routes on the Reasoning tier.
        materialized.ModelTier.Should().Be(action.ModelTier);
        materialized.OutputSchemaJson.Should().Be(action.OutputSchemaJson);
    }

    // ---------------------------------------------------------------------------------------------
    // Fallback via the sprk_isfallback FLAG — never a magic "general" string.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildRegistryBlock_SelectsFallbackViaFlag_NotAMagicKeyString()
    {
        // The fallback row here is deliberately keyed "zzz-fallback", NOT "general", to prove selection
        // is by the sprk_isfallback flag, not by matching a hardcoded key literal.
        var fallback = new AgreementTypeRow(
            "zzz-fallback", "Catch-all", "Broad fallback guidance.", IsFallback: true, ConfidenceThreshold: null);
        var rows = new[] { Nda, fallback };

        var block = _sut.BuildRegistryBlock(rows);

        block.Should().Contain("[FALLBACK", "the fallback row is flagged in-line via its sprk_isfallback flag");
        block.Should().Contain("The fallback key is \"zzz-fallback\".",
            "the trailing summary names the fallback key derived from the flag, never a hardcoded 'general'");
    }

    // ---------------------------------------------------------------------------------------------
    // Graceful handling of null cues / null names (task 001: cues populated only on nda + general today).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildRegistryBlock_NullCue_EmitsKeyDerivedGuidance_SoTheTypeStaysClassifiable()
    {
        var rows = new[] { EmploymentNullCue, General };

        var block = _sut.BuildRegistryBlock(rows);

        block.Should().Contain("\"employment\"");
        block.Should().Contain("No specific classification cue is registered for this type yet",
            "a cue-less row must still emit a key-derived guidance line so the type remains classifiable");
        block.Should().Contain("ordinary meaning of \"Employment\"",
            "the guidance derives the human label from the display name when present");
    }

    [Fact]
    public void BuildRegistryBlock_NullNameAndCue_DerivesTitleFromHyphenatedKey()
    {
        var rows = new[] { AssetPurchaseNullNameAndCue, General };

        var block = _sut.BuildRegistryBlock(rows);

        block.Should().Contain("\"asset-purchase\"");
        block.Should().Contain("Asset Purchase",
            "a null display name derives a title-cased label from the hyphenated key");
    }

    [Fact]
    public void BuildRegistryBlock_PopulatedCue_IsPreservedOnASingleLine()
    {
        var multiLineCue = new AgreementTypeRow(
            "lease", "Lease / Real Property",
            "A lease of real property.\n  Distinguishing signals: premises, term, rent,\n  and landlord/tenant covenants.",
            IsFallback: false, ConfidenceThreshold: null);
        var rows = new[] { multiLineCue, General };

        var block = _sut.BuildRegistryBlock(rows);

        block.Should().Contain("A lease of real property. Distinguishing signals: premises, term, rent, and landlord/tenant covenants.",
            "a multi-line cue is collapsed to one prompt line so the block renders cleanly");
    }

    // ---------------------------------------------------------------------------------------------
    // Deterministic ordering + placeholder substitution guarantees.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildRegistryBlock_OrdersSpecificTypesBeforeFallback_AlphabeticallyByKey()
    {
        var rows = new[] { General, Nda, EmploymentNullCue }; // deliberately unsorted, fallback first

        var block = _sut.BuildRegistryBlock(rows);

        var idxEmployment = block.IndexOf("\"employment\"", StringComparison.Ordinal);
        var idxNda = block.IndexOf("\"nda\"", StringComparison.Ordinal);
        var idxGeneral = block.IndexOf("\"general\"", StringComparison.Ordinal);

        idxEmployment.Should().BeLessThan(idxNda, "specific types sort alphabetically by key");
        idxNda.Should().BeLessThan(idxGeneral, "the fallback row sorts after all specific types");
    }

    [Fact]
    public void Materialize_PromptWithoutPlaceholder_ReturnsActionUnchanged()
    {
        var action = new AnalysisAction { Name = "x", SystemPrompt = "A classifier prompt with no placeholder." };

        var result = _sut.Materialize(action, new[] { Nda, General });

        result.SystemPrompt.Should().Be(action.SystemPrompt,
            "the assembler must not append the block unanchored when the placeholder is absent (misconfiguration guard)");
    }

    [Fact]
    public void BuildRegistryBlock_NoRows_StatesNoneRegistered_WithoutThrowing()
    {
        var block = _sut.BuildRegistryBlock(Array.Empty<AgreementTypeRow>());

        block.Should().Contain("No agreement types are currently registered");
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static AnalysisAction LoadAgreementClassifyAction()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "agreement-classify.action.json");
        File.Exists(path).Should().BeTrue($"the agreement-classify Action mirror must exist at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var systemPrompt = doc.RootElement.GetProperty("systemPrompt").GetString();
        systemPrompt.Should().NotBeNullOrWhiteSpace();
        return new AnalysisAction
        {
            Name = doc.RootElement.GetProperty("name").GetString() ?? "Agreement Classify",
            SystemPrompt = systemPrompt!,
            ModelTier = AiModelTier.Reasoning,
            OutputSchemaJson = doc.RootElement.GetProperty("outputSchema").GetRawText()
        };
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
