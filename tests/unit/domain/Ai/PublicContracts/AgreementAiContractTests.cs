using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.PublicContracts;

/// <summary>
/// spaarke-intake-pipeline-r1 task 004: the kill-switch peer and the pure decision rules of the <see cref="IAgreementAi"/> contracts.
/// The run-path behaviour is in <c>tests/integration/seam/Intake/AgreementAiEditPlanSeamTests.cs</c>.
/// </summary>
public sealed class AgreementAiContractTests
{
    public delegate Task Capability(IAgreementAi ai);

    public static TheoryData<string, Capability> Capabilities() => new()
    {
        { "ClassifyAsync", ai => ai.ClassifyAsync(new AgreementClassifyRequest { DocumentText = "x" }) },
        { "BuildEditPlanAsync", ai => ai.BuildEditPlanAsync(new AgreementEditPlanRequest { SegmentationJson = "{}" }) },
    };

    // ADR-032 P3: with the compound AI gate off, both methods fail fast with the facade's own error code, so the intake
    // orchestrator holds the run instead of reading an empty classification or sending a document out with no review.
    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task NullAgreementAi_CapabilityMethod_ThrowsFeatureDisabledWithAgreementErrorCode(string method, Capability call)
    {
        var sut = new NullAgreementAi(NullLogger<NullAgreementAi>.Instance);

        var ex = await FluentActions.Awaiting(() => call(sut)).Should().ThrowAsync<FeatureDisabledException>(method);

        ex.Which.ErrorCode.Should().Be("ai.agreement.disabled");
    }

    [Theory]
    [InlineData(true, "nda", 0.9, 0.85, true)]
    [InlineData(true, "NDA", 0.85, 0.85, true)]
    [InlineData(true, "nda", 0.84, 0.85, false)]
    [InlineData(true, "services", 0.99, 0.85, false)]
    [InlineData(false, "nda", 0.99, 0.85, false)]
    public void IsSubDomain_AppliesTheCallersThresholdToTheTopCandidateOnly(bool isAgreement, string key, double confidence, double minConfidence, bool expected)
    {
        var result = new AgreementClassifyResult
        {
            IsAgreement = isAgreement,
            Candidates = [new AgreementCandidate(key, confidence)],
            Composite = false,
        };

        result.IsSubDomain(AgreementSubDomains.Nda, minConfidence).Should().Be(expected);
    }

    [Fact]
    public void IsSubDomain_WhenNdaIsOnlyTheSecondCandidate_IsFalse_BecauseTheTopCandidateDecides()
    {
        var result = new AgreementClassifyResult
        {
            IsAgreement = true,
            Candidates = [new AgreementCandidate("services", 0.9), new AgreementCandidate("nda", 0.7)],
            Composite = true,
        };

        result.IsSubDomain(AgreementSubDomains.Nda, 0.5).Should().BeFalse();
    }

    [Fact]
    public void IsSubDomain_WithNoCandidates_IsFalse()
    {
        new AgreementClassifyResult { IsAgreement = false, Candidates = [], Composite = false }
            .IsSubDomain(AgreementSubDomains.Nda, 0.0).Should().BeFalse();
    }
}
