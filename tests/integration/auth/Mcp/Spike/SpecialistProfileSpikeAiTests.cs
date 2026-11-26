// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; never merged). The facade behind the spike tools, with the REAL ContextBinder:
// the five positions reach the model input as declared companions (ADR-043), the profile's tier is set on the Action explicitly (#1934),
// the DEV-only action code is the one read, and a position that does not reach the model input stops the run.

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

public sealed class SpecialistProfileSpikeAiTests
{
    private static readonly ClaimsPrincipal Caller = new(new ClaimsIdentity(
        [new Claim("oid", SpikeToolsTestHost.OwnerOid.ToString()), new Claim("tid", "11111111-2222-4333-8444-555555555555")], "test"));

    [Fact]
    public async Task Review_BindsTheFivePositionsAsDeclaredInputs_AppliesTheProfileTier_AndRunsTheSpikeAction()
    {
        var rig = new Rig(rowTier: AiModelTier.Standard);
        var positions = SpikeTestProfiles.Default.Find("nda", SpikeToolsTestHost.OwnerSystemUserId.ToString("D"))!.ToReviewInputs();

        var result = await rig.Facade.RunAgreementReviewAsync(
            new SpikeReviewRequest(Caller, "The NDA text.", positions, "Reasoning", "corr-1"), CancellationToken.None);

        rig.Actions.Verify(a => a.GetActionByCodeAsync("agreement-review-spike", It.IsAny<CancellationToken>()), Times.Once);
        rig.RanAction!.ModelTier.Should().Be(AiModelTier.Reasoning, "the profile's tier is applied on the Action, over the row's Standard (#1934)");
        rig.RanContext!.ConsumerType.Should().Be(SpecialistProfileSpike.ConsumerType);
        rig.RanContext.TenantId.Should().Be("11111111-2222-4333-8444-555555555555", "the caller's tenant, so reference grounding runs");

        var operand = rig.RanInputs!.Operand;
        operand.Channel.Should().Be(OperandChannel.Input);
        var input = operand.Input!.Value;
        input.GetProperty("documentText").GetString().Should().Be("The NDA text.");
        foreach (var name in SpecialistProfileSpike.PositionInputs)
        {
            input.GetProperty(name).GetRawText().Should().Be(positions[name].GetRawText(), $"{name} reached the model input unchanged");
        }

        result.AppliedModelTier.Should().Be("Reasoning");
        result.ActionCode.Should().Be("agreement-review-spike");
        result.BoundInputNames.Should().BeEquivalentTo(SpecialistProfileSpike.PositionInputs.Prepend("documentText"));
        result.Review.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Review_RefusesWhenAPositionIsMissing_TheTierIsUnknown_OrTheActionIsMissing_AndNeverRunsTheModel()
    {
        var positions = SpikeTestProfiles.Default.Find("nda", SpikeToolsTestHost.TestUserSystemUserId.ToString("D"))!.ToReviewInputs();
        var fourPositions = positions.Where(p => p.Key != "residuals").ToDictionary(p => p.Key, p => p.Value);

        var rig = new Rig(rowTier: AiModelTier.Reasoning);
        await rig.Invoking(r => r.Facade.RunAgreementReviewAsync(new SpikeReviewRequest(Caller, "text", fourPositions, "Reasoning", null), default))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*exactly the positions*");
        await rig.Invoking(r => r.Facade.RunAgreementReviewAsync(new SpikeReviewRequest(Caller, "text", positions, "100000002", null), default))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Unknown model tier*");

        var missingAction = new Rig(rowTier: null);
        await missingAction.Invoking(r => r.Facade.RunAgreementReviewAsync(new SpikeReviewRequest(Caller, "text", positions, "Reasoning", null), default))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*agreement-review-spike*not found*");

        rig.RanInputs.Should().BeNull();
        missingAction.RanInputs.Should().BeNull();
    }

    [Fact]
    public async Task Review_APositionTheBinderDropped_StopsTheRun()
    {
        // A binder that keeps only documentText: what an undeclared input looks like (ADR-043 drops it silently).
        var binder = new Mock<IContextBinder>();
        binder.Setup(b => b.BindAsync(It.IsAny<ContextBindingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BoundInputs
            {
                Context = new ContextEnvelope(),
                Operand = new ResolvedOperand
                {
                    Channel = OperandChannel.Input,
                    Kind = OperandKind.DocumentText,
                    Input = JsonSerializer.SerializeToElement(new { documentText = "text" }),
                },
            });
        var rig = new Rig(rowTier: AiModelTier.Reasoning, binder: binder.Object);
        var positions = SpikeTestProfiles.Default.Find("nda", SpikeToolsTestHost.OwnerSystemUserId.ToString("D"))!.ToReviewInputs();

        await rig.Invoking(r => r.Facade.RunAgreementReviewAsync(new SpikeReviewRequest(Caller, "text", positions, "Reasoning", null), default))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not reach the model input*term*");
        rig.RanInputs.Should().BeNull("the model is never called without the caller's positions");
    }

    [Theory]
    [InlineData(true, "1d02f31c-1872-f011-b4cb-7c1e52671ad0", true)]
    [InlineData(false, null, false)]
    [InlineData(true, "not-a-guid", false)]
    [InlineData(true, "00000000-0000-0000-0000-000000000000", false)]
    public async Task ResolveCaller_PassesOnlyAResolvedDGuid(bool resolved, string? id, bool expected)
    {
        var rig = new Rig(rowTier: AiModelTier.Reasoning);
        rig.Callers.Setup(c => c.ResolveAsync(Caller, It.IsAny<CancellationToken>())).ReturnsAsync(resolved
            ? new CallerSystemUserResolution { IsResolved = true, SystemUserId = id }
            : CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var result = await rig.Facade.ResolveCallerAsync(Caller, default);

        result.IsResolved.Should().Be(expected);
        result.SystemUserId.Should().Be(expected ? id : null);
    }

    private sealed class Rig
    {
        public Rig(AiModelTier? rowTier, IContextBinder? binder = null)
        {
            Actions = new Mock<AnalysisActionService>(
                MockBehavior.Loose,
                new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/",
                }).Build(),
                new NoopCredential(),
                NullLogger<AnalysisActionService>.Instance);
            Actions.Setup(a => a.GetActionByCodeAsync("agreement-review-spike", It.IsAny<CancellationToken>()))
                .ReturnsAsync(rowTier is null ? null : new AnalysisAction
                {
                    Id = Guid.Parse("17285c7b-98e9-f111-84ea-00224829aef9"),
                    Name = "Agreement review (spike)",
                    SystemPrompt = "Review the agreement.",
                    OutputSchemaJson = """{"type":"object"}""",
                    ModelTier = rowTier,
                });

            var runner = new Mock<IActionRunner>();
            runner.Setup(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
                .Callback<AnalysisAction, BoundInputs, LinearRunContext, CancellationToken>((a, i, c, _) => (RanAction, RanInputs, RanContext) = (a, i, c))
                .ReturnsAsync(JsonSerializer.SerializeToElement(new { ok = true }));

            var sessions = new ChatSessionManager(new InMemoryTenantCache(), Mock.Of<IChatDataverseRepository>(), Mock.Of<ILogger<ChatSessionManager>>());
            Facade = new SpecialistProfileSpikeAi(
                Callers.Object,
                Actions.Object,
                binder ?? new ContextBinder(sessions, Mock.Of<ILogger<ContextBinder>>()),
                runner.Object,
                NullLogger<SpecialistProfileSpikeAi>.Instance);
        }

        public Mock<ICallerSystemUserResolver> Callers { get; } = new();

        public Mock<AnalysisActionService> Actions { get; }

        public SpecialistProfileSpikeAi Facade { get; }

        public AnalysisAction? RanAction { get; private set; }

        public BoundInputs? RanInputs { get; private set; }

        public LinearRunContext? RanContext { get; private set; }
    }

    private sealed class NoopCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext c, CancellationToken ct) => new("test", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, CancellationToken ct) =>
            new(new Azure.Core.AccessToken("test", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
