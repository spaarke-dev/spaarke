using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Gate;

/// <summary>
/// Confirmation Policy v2 deterministic origin classifier (FR-A1-03 / task 032) — the §3 tree
/// + the six ruled edge cases E-1..E-6. Pure domain logic (ADR-038 unit/domain KEEP category).
///
/// Each ruled row is proven with at least a positive AND a negative case (the origin-classification
/// eval family, task 033, is generated from these same rulings). The classifier is fail-closed:
/// every undecidable path returns <see cref="GateRequestOrigin.Inferred"/>.
/// </summary>
public class RequestOriginClassifierTests
{
    // -------------------------------------------------------------------------
    // §3 base tree — provenance decides before any content is read.
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_ClickPath_IsExplicitByConstruction()
    {
        var request = new GateOriginRequest { Source = GateRequestSource.Click };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Explicit);
    }

    [Theory]
    [InlineData(GateRequestSource.DocumentContent)]
    [InlineData(GateRequestSource.ToolResult)]
    public void Classify_NonUtteranceOrigin_IsInferred_NeverReadAsUtterance(GateRequestSource source)
    {
        // E-3: document-derived / tool-result content is NEVER read as a user utterance, even if
        // it textually "names" an action.
        var request = new GateOriginRequest
        {
            Source = source,
            UtteranceEnumeratesCapability = true, // would-be-explicit text, but the source is untrusted
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    [Fact]
    public void Classify_UndecidableUtterance_FailsClosedToInferred()
    {
        var request = new GateOriginRequest { Source = GateRequestSource.UserUtterance };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    // -------------------------------------------------------------------------
    // E-1 — bare affirmation binds to a single, complete proposal.
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_Affirmation_AfterSingleCompleteProposal_IsExplicit()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            IsBareAffirmation = true,
            PrecedingProposal = new GateProposalContext { ConcreteActionCount = 1, ArgsComplete = true },
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Explicit);
    }

    [Fact]
    public void Classify_Affirmation_AfterTwoProposals_IsInferred()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            IsBareAffirmation = true,
            PrecedingProposal = new GateProposalContext { ConcreteActionCount = 2, ArgsComplete = true },
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred,
            "two proposals ⇒ the affirmation does not bind to a single action (E-1 fails)");
    }

    [Fact]
    public void Classify_Affirmation_AfterIncompleteProposal_IsInferred()
    {
        RequestOriginClassifier.ClassifyAffirmation(
            new GateProposalContext { ConcreteActionCount = 1, ArgsComplete = false })
            .Should().Be(GateRequestOrigin.Inferred);
    }

    [Fact]
    public void Classify_Affirmation_WithNoProposal_IsInferred()
    {
        RequestOriginClassifier.ClassifyAffirmation(null).Should().Be(GateRequestOrigin.Inferred);
    }

    // -------------------------------------------------------------------------
    // E-2 — explicitness survives model-only turns; a user turn resets it.
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_ModelInitiated_SameCapabilityArgs_NoUserTurn_InheritsExplicit()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.ModelInitiated,
            PriorExplicitForSameCapabilityArgs = GateRequestOrigin.Explicit,
            UserTurnSincePriorClassification = false,
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Explicit,
            "explicitness survives model-only intermediate turns for the same (capability, args)");
    }

    [Fact]
    public void Classify_ModelInitiated_AfterInterveningUserTurn_ResetsToInferred()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.ModelInitiated,
            PriorExplicitForSameCapabilityArgs = GateRequestOrigin.Explicit,
            UserTurnSincePriorClassification = true, // a USER turn intervened — resets explicitness
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    [Fact]
    public void Classify_ModelInitiated_NoPriorExplicit_IsInferred()
    {
        var request = new GateOriginRequest { Source = GateRequestSource.ModelInitiated };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    // -------------------------------------------------------------------------
    // E-4 — one utterance, explicit for the ENUMERATED set only.
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_Utterance_EnumeratingCapability_IsExplicit()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            UtteranceEnumeratesCapability = true,
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Explicit);
    }

    [Fact]
    public void Classify_Utterance_NotEnumeratingCapability_IsInferred_ModelAddedExtra()
    {
        // A capability the model added BEYOND the enumerated set carries UtteranceEnumeratesCapability=false
        // ⇒ inferred ⇒ its own gate evaluation (E-4: model-added extras are inferred).
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            UtteranceEnumeratesCapability = false,
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    // -------------------------------------------------------------------------
    // E-5 — elicitation answer inherits the original request's origin.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(GateRequestOrigin.Explicit, GateRequestOrigin.Explicit)]
    [InlineData(GateRequestOrigin.Inferred, GateRequestOrigin.Inferred)]
    public void Classify_ElicitationAnswer_InheritsOriginalOrigin(
        GateRequestOrigin original, GateRequestOrigin expected)
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            IsElicitationAnswer = true,
            OriginalRequestOrigin = original,
        };

        RequestOriginClassifier.Classify(request).Should().Be(expected);
    }

    [Fact]
    public void Classify_ElicitationAnswer_WithNoRecordedOrigin_FailsClosedToInferred()
    {
        var request = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            IsElicitationAnswer = true,
            OriginalRequestOrigin = null,
        };

        RequestOriginClassifier.Classify(request).Should().Be(GateRequestOrigin.Inferred);
    }

    // -------------------------------------------------------------------------
    // E-1 bare-affirmation vocabulary (ElicitationTurnRouter helper, reused by the classifier).
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("go ahead", true)]
    [InlineData("Yes", true)]
    [InlineData("do it!", true)]
    [InlineData("proceed.", true)]
    [InlineData("yes, and also delete the old one", false)] // carries extra content — not bare
    [InlineData("create a task", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClassifyBareAffirmation_MatchesClosedVocabularyOnly(string? message, bool expected)
    {
        ElicitationTurnRouter.ClassifyBareAffirmation(message).Should().Be(expected);
    }
}
