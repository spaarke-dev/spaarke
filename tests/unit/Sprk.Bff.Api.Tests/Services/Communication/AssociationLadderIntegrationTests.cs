using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ladder-integration tests: the REAL <see cref="IncomingAssociationResolver"/> engine composed with the
/// REAL <see cref="AssociationStatusMapper"/> + <see cref="AutoFileGate"/>, a fake deterministic rung, and
/// the REAL rungs 4/5 wired to SPY AI facades — with the Dataverse write boundary mocked so the decision's
/// observable end-state (tier + which rungs were invoked) is asserted, not internals.
///
/// These lock the two governing invariants of the deterministic-first ladder (NFR-05):
///   (1) a high-confidence deterministic match short-circuits — the AI rungs are never invoked;
///   (2) a deterministic miss escalates to rung 4 then rung 5;
///   (3) conflicting high-confidence deterministic matches resolve to Ambiguous (never a guess).
/// </summary>
public class AssociationLadderIntegrationTests
{
    private readonly Mock<IDataverseService> _dv = new();
    private readonly Mock<IRecordMatchingAi> _matcher = new();
    private readonly Mock<ICommunicationClassificationAi> _classifier = new();
    private Dictionary<string, object>? _capturedFields;

    private static readonly Guid CommunicationId = Guid.NewGuid();

    private IncomingAssociationResolver BuildEngine(params IAssociationRung[] deterministicRungs)
    {
        _dv.Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, fields, _) => _capturedFields = fields)
            .Returns(Task.CompletedTask);

        var rungs = new List<IAssociationRung>(deterministicRungs)
        {
            new SemanticMatchRung(RungTestSupport.ScopeFactoryFor(_matcher.Object),
                Options.Create(new SemanticMatchOptions()), NullLogger<SemanticMatchRung>.Instance),
            new AiClassificationRung(RungTestSupport.ScopeFactoryFor(_classifier.Object),
                Options.Create(new AiClassificationOptions()), NullLogger<AiClassificationRung>.Instance),
        };

        return new IncomingAssociationResolver(
            rungs, _dv.Object, _dv.Object, AssociationTestSupport.Mapper(),
            NullLogger<IncomingAssociationResolver>.Instance);
    }

    private static RungMatch DeterministicMatterMatch(Guid matterId, double confidence) => new()
    {
        RegardingFieldName = "sprk_regardingmatter",
        Target = new EntityReference("sprk_matter", matterId) { Name = "Acme Matter" },
        Confidence = confidence,
        Provenance = "explicit:MAT-1",
        Rung = RungKind.ExplicitReference,
    };

    private int CapturedStatus => ((OptionSetValue)_capturedFields!["sprk_associationstatus"]).Value;

    [Fact]
    public async Task Resolve_WhenDeterministicHighConfidence_AutoFilesResolved_AndNeverInvokesAiRungs()
    {
        // Seed a strong (0.95 ≥ 0.85) deterministic match ⇒ auto-file Resolved; the AI rungs (4/5) MUST NOT
        // run (cost containment + the deterministic-short-circuit invariant).
        var det = new FakeRung(RungKind.ExplicitReference, 0,
            DeterministicMatterMatch(Guid.NewGuid(), 0.95));

        await BuildEngine(det).ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        CapturedStatus.Should().Be(AssociationStatusCodes.Resolved);            // 100000000, auto-filed
        _matcher.Verify(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _classifier.Verify(
            c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolve_WhenDeterministicMiss_EscalatesToRung4ThenRung5()
    {
        // Deterministic pass produces nothing ⇒ AI escalation. BOTH rung 4 (search) and rung 5 (classify)
        // fire; rung 4's semantic hit lands in the Suggested band (never auto-file).
        var detMiss = new FakeRung(RungKind.ExplicitReference, 0 /* no matches */);

        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(
                RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.72, "Fuzzy Matter")));
        _classifier.Setup(
                c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationClassificationResult
            {
                Category = "general-correspondence",
                Rationale = "No strong record signal.",
            });

        await BuildEngine(detMiss).ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        _matcher.Verify(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _classifier.Verify(
            c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        CapturedStatus.Should().Be(AssociationStatusCodes.Suggested);          // 100000003 (AI never auto-files)
    }

    [Fact]
    public async Task Resolve_WhenTwoConflictingHighConfidenceMatchesOnSameField_ResolvesAmbiguous()
    {
        // Two distinct matter targets each ≥ threshold on the same field ⇒ the engine will not guess.
        var det = new FakeRung(RungKind.ExplicitReference, 0,
            DeterministicMatterMatch(Guid.NewGuid(), 0.95),
            DeterministicMatterMatch(Guid.NewGuid(), 0.95));

        // AI rungs run (Ambiguous is not an auto-file) but must not change the outcome — metadata-only.
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse());
        _classifier.Setup(
                c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommunicationClassificationResult?)null);

        await BuildEngine(det).ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        CapturedStatus.Should().Be(AssociationStatusCodes.Ambiguous);          // 100000004
        _capturedFields!.Should().NotContainKey("sprk_regardingmatter");       // no guess written
    }
}
