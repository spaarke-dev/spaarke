using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 5 (AI extract+classify, FR-15) unit tests. The rung resolves <see cref="ICommunicationClassificationAi"/>
/// from a per-eval scope; these tests register a MOCK facade (boundary mock, ADR-038) and run the REAL rung
/// logic. Load-bearing invariants: rung 5 emits ONLY metadata-only signals (Target/RegardingFieldName null) —
/// so it can never resolve a regarding target and never auto-files — and privilege is a flag, never a
/// decision (ADR-015).
/// </summary>
public class AiClassificationRungTests
{
    private readonly Mock<ICommunicationClassificationAi> _classifier = new();

    private AiClassificationRung Build(AiClassificationOptions? opts = null) =>
        new(RungTestSupport.ScopeFactoryFor(_classifier.Object),
            Options.Create(opts ?? new AiClassificationOptions()),
            NullLogger<AiClassificationRung>.Instance);

    private void SetupClassification(CommunicationClassificationResult? result) =>
        _classifier.Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task Evaluate_WhenClassified_EmitsMetadataOnlySignal_NeverATarget()
    {
        SetupClassification(new CommunicationClassificationResult
        {
            Category = "court-notice",
            Urgency = "urgent",
            CandidateRecordTypes = new[] { "sprk_matter" },
            Obligations = new[] { "deadline-response" },
            Rationale = "Mentions a filing deadline.",
        });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var signal = matches.Should().ContainSingle().Subject;
        // METADATA-ONLY: no regarding write, so the mapper can never turn this into a Resolved/auto-file.
        signal.Target.Should().BeNull();
        signal.RegardingFieldName.Should().BeNull();
        signal.Category.Should().Be("court-notice");
        signal.Obligations.Should().Contain("deadline-response");
        signal.Rung.Should().Be(RungKind.AiClassification);
    }

    [Fact]
    public async Task Evaluate_NeverProducesTargetBearingMatch_NoAutoFilePossible()
    {
        SetupClassification(new CommunicationClassificationResult
        {
            Category = "invoice",
            CandidateRecordTypes = new[] { "sprk_invoice", "sprk_matter" },
            PrivilegeFlagged = true,          // even with privilege, still only signals
            Rationale = "Invoice-like content.",
        });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().NotBeEmpty();
        matches.Should().OnlyContain(m => m.Target == null && m.RegardingFieldName == null);
    }

    [Fact]
    public async Task Evaluate_WhenPrivilegeFlagged_EmitsSeparatePrivilegeSignal_NotADecision()
    {
        // ADR-015: privilege is flagged for the reviewer, never filed/redacted on by the engine.
        SetupClassification(new CommunicationClassificationResult
        {
            Category = "attorney-client",
            PrivilegeFlagged = true,
            Rationale = "Privileged legal advice.",
        });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var privilege = matches.Should().Contain(m => m.Category == "privilege-flag").Which;
        privilege.Target.Should().BeNull();
        privilege.RegardingFieldName.Should().BeNull();
        privilege.Provenance.Should().Contain("ADR-015");
    }

    [Fact]
    public async Task Evaluate_WritesCategoryAndRationaleIntoProvenance()
    {
        SetupClassification(new CommunicationClassificationResult
        {
            Category = "general-correspondence",
            Urgency = "routine",
            Rationale = "Routine status update from opposing counsel.",
        });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var signal = matches.Should().ContainSingle().Subject;
        signal.Provenance.Should().Contain("category=general-correspondence");
        signal.Provenance.Should().Contain("Routine status update from opposing counsel.");
    }

    [Fact]
    public async Task Evaluate_WhenDisabled_ReturnsEmpty_AndDoesNotClassify()
    {
        var matches = await Build(new AiClassificationOptions { Enabled = false }).EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _classifier.Verify(
            c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Evaluate_WhenClassificationNull_ReturnsEmpty()
    {
        SetupClassification(null);

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }
}
