using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="LiveFactNode"/> (D-P12 task 022, refactored r2 Wave D5
/// task 034 to dispatch by entity-type via <c>IReadOnlyDictionary&lt;string,
/// ILiveFactResolver&gt;</c>). Covers happy path + validation + unsupported-predicate +
/// null-resolution + multi-entity dispatch + unknown-scheme error paths.
/// </summary>
public sealed class LiveFactNodeTests
{
    private const string MatterGuid = "11111111-1111-1111-1111-111111111111";
    private const string ProjectGuid = "22222222-2222-2222-2222-222222222222";
    private const string InvoiceGuid = "33333333-3333-3333-3333-333333333333";
    private const string MatterSubject = "matter:" + MatterGuid;
    private const string ProjectSubject = "project:" + ProjectGuid;
    private const string InvoiceSubject = "invoice:" + InvoiceGuid;

    /// <summary>
    /// Builds a <see cref="LiveFactNode"/> with a multi-entity resolver registry. All three
    /// entity-type keys are wired to the supplied mock by default; per-entity callers can
    /// supply their own dictionary via <paramref name="resolvers"/> to test
    /// scheme-mismatch + missing-resolver paths.
    /// </summary>
    private static LiveFactNode BuildNode(
        Mock<ILiveFactResolver>? resolverMock = null,
        IReadOnlyDictionary<string, ILiveFactResolver>? resolvers = null,
        ISubjectParser? parser = null)
    {
        if (resolvers is null)
        {
            var mock = resolverMock ?? new Mock<ILiveFactResolver>();
            resolvers = new Dictionary<string, ILiveFactResolver>(StringComparer.OrdinalIgnoreCase)
            {
                ["matter"] = mock.Object,
                ["project"] = mock.Object,
                ["invoice"] = mock.Object
            };
        }

        parser ??= new SubjectParser(Options.Create(new SubjectSchemeCatalogOptions()));

        // LiveFactNode resolves the Scoped resolver dictionary from IServiceScopeFactory
        // (dotnet-10-upgrade task 020, R7). Wire a scope chain that yields the test dictionary.
        return new LiveFactNode(ScopeFactoryReturning(resolvers), parser, NullLogger<LiveFactNode>.Instance);
    }

    /// <summary>Builds a mock IServiceScopeFactory whose scope resolves the supplied resolver map.</summary>
    private static IServiceScopeFactory ScopeFactoryReturning(IReadOnlyDictionary<string, ILiveFactResolver> resolvers)
    {
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider
            .Setup(sp => sp.GetService(typeof(IReadOnlyDictionary<string, ILiveFactResolver>)))
            .Returns(resolvers);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static FactArtifact BuildFact(string subject, string predicate, double rawValue) => new()
    {
        Id = $"fact:{subject}:{predicate}",
        Subject = subject,
        Predicate = predicate,
        Value = new Value { Raw = JsonDocument.Parse(rawValue.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement, DisplayHint = "currency-usd" },
        Evidence = new[] { new EvidenceRef { RefType = "fact-source", Ref = $"dataverse://sprk_matter/{subject}#{predicate}" } },
        AsOf = DateTimeOffset.UtcNow,
        ProducedBy = new ProducedBy { Kind = "query", Id = "query://matter-totalspend", Version = "v1" },
        Scope = new Scope { TenantId = "tenant-x" },
        TenantId = "tenant-x"
    };

    [Fact]
    public void SupportedActionTypes_ContainsLiveFact()
    {
        var node = BuildNode();
        node.SupportedExecutorTypes.Should().ContainSingle().Which.Should().Be(ExecutorType.LiveFact);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_EmitsFactArtifactWithConfidence1()
    {
        var resolver = new Mock<ILiveFactResolver>();
        var fact = BuildFact(MatterSubject, "totalSpend", 287500.0);
        resolver.Setup(r => r.ResolveAsync(MatterSubject, "totalSpend", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fact);

        var node = BuildNode(resolverMock: resolver);
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{MatterSubject}}", "predicate": "totalSpend" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Confidence.Should().Be(1.0); // Facts are always certain per design.md §2.1.
        var emitted = result.GetData<FactArtifact>();
        emitted.Should().NotBeNull();
        emitted!.Subject.Should().Be(MatterSubject);
        emitted.Predicate.Should().Be("totalSpend");
        emitted.Evidence.Should().NotBeEmpty(); // D-04 provenance contract.
    }

    [Fact]
    public async Task ExecuteAsync_MissingConfig_ReturnsValidationError()
    {
        var node = BuildNode();
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.LiveFact, configJson: null);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("subject", because: "validation should mention missing required field");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedPredicate_ReturnsInvalidConfiguration()
    {
        var resolver = new Mock<ILiveFactResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LiveFactNotSupportedException(MatterSubject, "nonExistent"));

        var node = BuildNode(resolverMock: resolver);
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{MatterSubject}}", "predicate": "nonExistent" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
    }

    [Fact]
    public async Task ExecuteAsync_NullResolution_SubjectNotFoundError()
    {
        var resolver = new Mock<ILiveFactResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactArtifact?)null);

        var node = BuildNode(resolverMock: resolver);
        // Use a GUID that resolves correctly through the parser but maps to a non-existent
        // matter row at the resolver layer (the mock returns null per the contract).
        var notFoundSubject = "matter:99999999-9999-9999-9999-999999999999";
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{notFoundSubject}}", "predicate": "totalSpend" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    // ─── r2 Wave D5 (task 034) — multi-entity dispatch tests ─────────────────


    [Fact]
    public async Task ExecuteAsync_UnknownScheme_ReturnsInvalidConfiguration()
    {
        // Subject parser rejects unknown schemes; LiveFactNode surfaces this as
        // InvalidConfiguration so playbook authoring errors are loud.
        var node = BuildNode();
        var unknownSubject = $"client:{Guid.NewGuid()}";
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{unknownSubject}}", "predicate": "anything" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
        result.ErrorMessage.Should().Contain("client", because: "the error should identify the unregistered scheme");
    }

    [Fact]
    public async Task ExecuteAsync_NoResolverForScheme_ReturnsInvalidConfiguration()
    {
        // Scheme is in the catalog but no matching resolver is registered. Defensive: this
        // catches a misconfigured DI registration (catalog scheme without a paired resolver).
        var resolvers = new Dictionary<string, ILiveFactResolver>(StringComparer.OrdinalIgnoreCase)
        {
            // matter intentionally omitted — but matter IS in the default catalog so the
            // parser will succeed; the dispatcher MUST then fail with a clear message.
            ["project"] = Mock.Of<ILiveFactResolver>(),
            ["invoice"] = Mock.Of<ILiveFactResolver>()
        };

        var node = BuildNode(resolvers: resolvers);
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{MatterSubject}}", "predicate": "attorney" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
        result.ErrorMessage.Should().Contain("matter", because: "the error should identify the missing scheme registration");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidGuid_ReturnsInvalidConfiguration()
    {
        // The dispatcher parses the subject BEFORE calling the resolver. Non-GUID forms
        // surface as InvalidConfiguration via the parser, not as resolver exceptions.
        var node = BuildNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            """{ "subject": "matter:not-a-guid", "predicate": "attorney" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
    }

    [Fact]
    public void Constructor_NullParser_Throws()
    {
        Action act = () => new LiveFactNode(Mock.Of<IServiceScopeFactory>(), null!, NullLogger<LiveFactNode>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("subjectParser");
    }
}
