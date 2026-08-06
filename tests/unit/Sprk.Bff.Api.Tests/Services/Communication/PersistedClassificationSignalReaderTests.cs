using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// email-communication-intelligence-r1 task 023 (FR-05). Proves
/// <see cref="PersistedClassificationSignalReader"/> reconstructs the classification
/// <see cref="Engine.Rungs.AiClassificationRung"/> produced from the persisted
/// <c>sprk_associationprovenance</c> shape — WITHOUT re-invoking classification (no second full LLM pass).
/// </summary>
/// <remarks>
/// Drives the REAL <see cref="AiClassificationRung.EvaluateAsync"/> (mocked classifier boundary, same
/// pattern as <see cref="AiClassificationRungTests"/>) to obtain the ACTUAL <c>RungMatch.Provenance</c>
/// string format, builds the <see cref="AssociationProvenance"/> exactly as
/// <see cref="AssociationStatusMapper.Decide"/> would (metadata-only signals path), and asserts the
/// reader recovers the original classification fields — a regression pin tied to production output, not a
/// hand-typed mirror of the format string.
/// </remarks>
public class PersistedClassificationSignalReaderTests
{
    private readonly Mock<ICommunicationClassificationAi> _classifier = new();

    private AiClassificationRung BuildRung() =>
        new(RungTestSupport.ScopeFactoryFor(_classifier.Object),
            Options.Create(new AiClassificationOptions()),
            NullLogger<AiClassificationRung>.Instance);

    private static AssociationProvenance BuildProvenance(IReadOnlyList<RungMatch> matches)
    {
        // Mirrors AssociationStatusMapper.Decide's metadata-only signal projection exactly (the same
        // filter + field mapping it applies before persisting sprk_associationprovenance).
        var signals = matches
            .Where(m => (m.Target is null || m.RegardingFieldName is null) && m.Category is not null)
            .Select(m => new SignalTrace
            {
                Category = m.Category!,
                Confidence = m.Confidence,
                Provenance = m.Provenance,
                Obligations = m.Obligations,
            })
            .ToList();

        return new AssociationProvenance
        {
            Direction = "Incoming",
            RungsFired = new[] { "AiClassification" },
            Candidates = Array.Empty<CandidateTrace>(),
            Signals = signals,
            Decision = new AssociationDecisionTrace
            {
                Status = "PendingReview",
                AutoFiled = false,
                KillSwitchEnabled = true,
                AutoFileThreshold = 0.85,
                TopDeterministicConfidence = 0.0,
                TopConfidence = 0.60,
                AiInvolved = true,
                Reason = "test",
            },
        };
    }

    [Fact]
    public async Task TryReconstruct_FromRealRungOutput_RecoversClassificationFields_NoSecondPass()
    {
        var original = new CommunicationClassificationResult
        {
            Category = "court-notice",
            Urgency = "urgent",
            CandidateRecordTypes = new[] { "sprk_matter" },
            Obligations = new[] { "respond-by-deadline", "calendar-hearing" },
            SuggestedActions = new[] { "calendar-deadline" },
            PrivilegeFlagged = false,
            Rationale = "Court-imposed response deadline for the matter.",
        };
        _classifier.Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        var matches = await BuildRung().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var provenance = BuildProvenance(matches);

        var reconstructed = PersistedClassificationSignalReader.TryReconstruct(provenance);

        reconstructed.Should().NotBeNull("the persisted metadata-only signal carries the classification — no second LLM call is needed to recover it");
        reconstructed!.Category.Should().Be("court-notice");
        reconstructed.Urgency.Should().Be("urgent");
        reconstructed.CandidateRecordTypes.Should().ContainSingle().Which.Should().Be("sprk_matter");
        reconstructed.Obligations.Should().BeEquivalentTo(new[] { "respond-by-deadline", "calendar-hearing" });
        reconstructed.SuggestedActions.Should().ContainSingle().Which.Should().Be("calendar-deadline");
        reconstructed.PrivilegeFlagged.Should().BeFalse();
        reconstructed.Rationale.Should().Be("Court-imposed response deadline for the matter.");
    }

    [Fact]
    public async Task TryReconstruct_WhenClassificationFlaggedPrivilege_RecoversFlagFromSeparateSignal()
    {
        _classifier.Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationClassificationResult
            {
                Category = "general-correspondence",
                Urgency = "routine",
                PrivilegeFlagged = true,
                Rationale = "Discusses legal strategy.",
            });

        var matches = await BuildRung().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);
        matches.Should().HaveCount(2, "a privilege-flagged classification emits a SECOND metadata-only signal (ADR-015)");

        var provenance = BuildProvenance(matches);
        var reconstructed = PersistedClassificationSignalReader.TryReconstruct(provenance);

        reconstructed.Should().NotBeNull();
        reconstructed!.PrivilegeFlagged.Should().BeTrue("ADR-015: privilege is a flag, reconstructed from the separate privilege-flag signal, never a triage decision");
    }

    [Fact]
    public void TryReconstruct_WhenNoSignals_ReturnsNull_NonFatal()
    {
        var provenance = new AssociationProvenance
        {
            Direction = "Incoming",
            RungsFired = Array.Empty<string>(),
            Candidates = Array.Empty<CandidateTrace>(),
            Signals = Array.Empty<SignalTrace>(),
            Decision = new AssociationDecisionTrace
            {
                Status = "PendingReview",
                AutoFiled = false,
                KillSwitchEnabled = true,
                AutoFileThreshold = 0.85,
                TopDeterministicConfidence = 0.0,
                TopConfidence = 0.0,
                AiInvolved = false,
                Reason = "no rungs fired",
            },
        };

        PersistedClassificationSignalReader.TryReconstruct(provenance).Should().BeNull(
            "NFR-04: no persisted AI-classify signal (e.g. rung 5 never ran) must degrade to null, never throw");
    }

    [Fact]
    public void TryReadFromProvenanceJson_WhenColumnEmpty_ReturnsNull()
    {
        PersistedClassificationSignalReader.TryReadFromProvenanceJson(null).Should().BeNull();
        PersistedClassificationSignalReader.TryReadFromProvenanceJson("").Should().BeNull();
        PersistedClassificationSignalReader.TryReadFromProvenanceJson("not json").Should().BeNull();
    }
}
