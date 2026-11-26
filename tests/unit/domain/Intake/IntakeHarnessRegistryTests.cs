using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Intake;

/// <summary>
/// Harness selection for the intake pipeline (spaarke-intake-pipeline-r1 task 012, spec FR-08 a–d, FR-10).
/// The registry runs over the REAL <see cref="ConsumerRoutingService"/> so the Binding match (consumer code,
/// <c>requestType</c>/<c>documentType</c> match conditions, target-less-row skip) is the production algorithm;
/// the only double is <see cref="IGenericEntityService"/>, the Dataverse boundary (ADR-038).
/// </summary>
public sealed class IntakeHarnessRegistryTests
{
    private readonly Mock<IGenericEntityService> _dataverse = new();

    // ── FR-08a / FR-08d: a Binding row + one registered harness class is all it takes ──────────────

    [Fact]
    public async Task ResolveAsync_RequestTypeMatchingAnIntakeHarnessBinding_ReturnsTheHarnessNamedByItsConsumerCode()
    {
        var bindingId = Guid.NewGuid();
        SetupBindingRows(IntakeHarnessRow("nda", "{\"requestType\":\"nda\"}", bindingId));
        var nda = new TestHarness("nda");
        var sut = CreateSut(nda, new TestHarness("trademark"));

        var result = await sut.ResolveAsync("nda", documentType: "nda");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.Resolved);
        result.Harness.Should().BeSameAs(nda);
        result.HarnessKey.Should().Be("nda");
        result.BindingId.Should().Be(bindingId);
    }

    [Fact]
    public async Task ResolveAsync_RequestTypeInDifferentCase_ResolvesTheSameHarness()
    {
        SetupBindingRows(IntakeHarnessRow("nda", "{\"requestType\":\"nda\"}"));
        var nda = new TestHarness("nda");
        var sut = CreateSut(nda);

        var result = await sut.ResolveAsync("NDA", documentType: null);

        result.Harness.Should().BeSameAs(nda);
    }

    // ── FR-08c / FR-10: no harness is a result, never an exception ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_RequestTypeWithNoBinding_ReturnsNoBindingWithoutAHarness()
    {
        SetupBindingRows(IntakeHarnessRow("nda", "{\"requestType\":\"nda\"}"));
        var sut = CreateSut(new TestHarness("nda"));

        var result = await sut.ResolveAsync("invoice", documentType: "invoice");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.NoBinding);
        result.HasHarness.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_BindingWhoseKeyHasNoRegisteredHarness_ReturnsUnregisteredKey()
    {
        var bindingId = Guid.NewGuid();
        SetupBindingRows(IntakeHarnessRow("trademark", "{\"requestType\":\"trademark\"}", bindingId));
        var sut = CreateSut(new TestHarness("nda"));

        var result = await sut.ResolveAsync("trademark", documentType: null);

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.UnregisteredKey);
        result.HasHarness.Should().BeFalse();
        result.HarnessKey.Should().Be("trademark");
        result.BindingId.Should().Be(bindingId);
    }

    [Fact]
    public async Task ResolveAsync_DocumentTypeConditionNotMet_ReturnsNoBinding()
    {
        // An nda request whose content classified as something else must not reach the NDA harness (FR-10).
        SetupBindingRows(IntakeHarnessRow("nda", "{\"requestType\":\"nda\",\"documentType\":\"nda\"}"));
        var sut = CreateSut(new TestHarness("nda"));

        var result = await sut.ResolveAsync("nda", documentType: "msa");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.NoBinding);
        result.HasHarness.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_HarnessThatCannotHandleTheRoute_ReturnsDeclined()
    {
        SetupBindingRows(IntakeHarnessRow("nda", "{\"requestType\":\"nda\"}"));
        var sut = CreateSut(new TestHarness("nda", canHandle: route => route.DocumentType == "nda"));

        var result = await sut.ResolveAsync("nda", documentType: null);

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.Declined);
        result.HasHarness.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_RoutingReadFails_ReturnsNoBindingInsteadOfThrowing()
    {
        _dataverse
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        var sut = CreateSut(new TestHarness("nda"));

        var result = await sut.ResolveAsync("nda", documentType: "nda");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.NoBinding);
        result.HasHarness.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("review this NDA please")]
    [InlineData("nda\"}")]
    public async Task ResolveAsync_BlankOrNonTokenRequestType_ReturnsInvalidRequestTypeWithoutRouting(string? requestType)
    {
        var sut = CreateSut(new TestHarness("nda"));

        var result = await sut.ResolveAsync(requestType, documentType: "nda");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.InvalidRequestType);
        result.HasHarness.Should().BeFalse();
        _dataverse.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a value that is not a deterministic token never reaches routing, its cache key or its logs (ADR-015)");
    }

    // ── Construction ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_TwoHarnessesWithTheSameKeyInAnyCase_ThrowsNamingTheKeyAndBothTypes()
    {
        var act = () => CreateSut(new TestHarness("nda"), new OtherTestHarness("NDA"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate intake harness key 'NDA'*")
            .And.Message.Should().Contain(nameof(TestHarness)).And.Contain(nameof(OtherTestHarness));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_HarnessWithBlankKey_Throws(string key)
    {
        var act = () => CreateSut(new TestHarness(key));

        act.Should().Throw<InvalidOperationException>().WithMessage("*blank Key*");
    }

    // ── By-key lookup (a resumed run carries its harness key) ─────────────────────────────────────

    [Theory]
    [InlineData("nda")]
    [InlineData("NDA")]
    public void Resolve_RegisteredKeyInAnyCase_ReturnsTheHarness(string key)
    {
        var nda = new TestHarness("nda");
        var sut = CreateSut(nda);

        var result = sut.Resolve(key, new IntakeHarnessRoute("nda", "nda"));

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.Resolved);
        result.Harness.Should().BeSameAs(nda);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invoice")]
    public void Resolve_MissingOrUnknownKey_ReturnsUnregisteredKeyWithoutAHarness(string? key)
    {
        var sut = CreateSut(new TestHarness("nda"));

        var result = sut.Resolve(key, new IntakeHarnessRoute("nda", null));

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.UnregisteredKey);
        result.HasHarness.Should().BeFalse();
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────────────────────────

    private IntakeHarnessRegistry CreateSut(params IIntakeHarness[] harnesses)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("dev");
        var routing = new ConsumerRoutingService(
            _dataverse.Object,
            new MemoryCache(new MemoryCacheOptions()),
            environment.Object,
            NullLogger<ConsumerRoutingService>.Instance);

        return new IntakeHarnessRegistry(harnesses, routing, NullLogger<IntakeHarnessRegistry>.Instance);
    }

    private void SetupBindingRows(params Entity[] rows) =>
        _dataverse
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_playbookconsumer"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    /// <summary>An <c>intake-harness</c> row as the Binding table stores it (target lookup set: target-less rows never route).</summary>
    private static Entity IntakeHarnessRow(string consumerCode, string? matchConditions, Guid? bindingId = null)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_playbookconsumerid"] = bindingId ?? Guid.NewGuid();
        entity["sprk_consumertype"] = ConsumerTypes.IntakeHarness;
        entity["sprk_consumercode"] = consumerCode;
        entity["sprk_environment"] = "*";
        entity["sprk_priority"] = 500;
        entity["sprk_matchconditions"] = matchConditions;
        entity["sprk_enabled"] = true;
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", Guid.NewGuid());
        return entity;
    }

    private class TestHarness(string key, Func<IntakeHarnessRoute, bool>? canHandle = null) : IIntakeHarness
    {
        public string Key { get; } = key;

        public bool CanHandle(IntakeHarnessRoute route) => canHandle?.Invoke(route) ?? true;

        public Task<IntakeHarnessResult> RunAsync(IntakeHarnessRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new IntakeHarnessResult());

        public Task<IntakeHarnessPersistResult> PersistAsync(
            IntakeHarnessRequest request, IntakeHarnessResult result, CancellationToken cancellationToken) =>
            Task.FromResult(new IntakeHarnessPersistResult());
    }

    private sealed class OtherTestHarness(string key) : TestHarness(key);
}
