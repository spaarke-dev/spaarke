using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="ConstrainedFieldResolver"/> orchestration (spec FR-B1, task 010). Uses a test
/// subclass that overrides the candidate-sourcing seam so the resolve → confidence → result flow is covered
/// with canned candidates — no Dataverse, no <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038).
/// </summary>
public class ConstrainedFieldResolverTests
{
    private static readonly IReadOnlyList<FieldCandidate> PracticeAreas =
    [
        new FieldCandidate("11111111-1111-1111-1111-111111111111", "Litigation"),
        new FieldCandidate("22222222-2222-2222-2222-222222222222", "Employment Law"),
        new FieldCandidate("33333333-3333-3333-3333-333333333333", "Intellectual Property"),
    ];

    /// <summary>Resolver with a substituted candidate set — the Dataverse sourcing seam is overridden.</summary>
    private sealed class StubResolver(IReadOnlyList<FieldCandidate> candidates) : ConstrainedFieldResolver(
        new HttpClient(),
        BuildConfig(),
        Mock.Of<TokenCredential>(),
        NullLogger<ConstrainedFieldResolver>.Instance,
        BuildMetadata(),
        BuildCache())
    {
        protected override Task<IReadOnlyList<FieldCandidate>> GetCandidatesAsync(
            string entityLogicalName, string attributeLogicalName, CancellationToken cancellationToken)
            => Task.FromResult(candidates);

        private static IConfiguration BuildConfig() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
            .Build();

        private static IDistributedCache BuildCache() =>
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        private static MetadataService BuildMetadata() =>
            new(Mock.Of<IDataverseService>(), BuildCache(), NullLogger<MetadataService>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_ExactPracticeArea_ReturnsHighWithRecordId()
    {
        var resolver = new StubResolver(PracticeAreas);

        var result = await resolver.ResolveAsync("sprk_matter", "sprk_practicearea", "Litigation", CancellationToken.None);

        result.Confidence.Should().Be(ResolutionConfidence.High);
        result.Resolved.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNoneWithCandidatesForPicker()
    {
        var resolver = new StubResolver(PracticeAreas);

        var result = await resolver.ResolveAsync("sprk_matter", "sprk_practicearea", "Bankruptcy", CancellationToken.None);

        result.Confidence.Should().Be(ResolutionConfidence.None);
        result.Resolved.Should().BeNull();
        result.Candidates.Should().HaveCount(3); // picker can render the full closed set
    }

    [Fact]
    public async Task ResolveAsync_FuzzyMatch_ReturnsLowWithTopCandidateFirst()
    {
        var resolver = new StubResolver(PracticeAreas);

        var result = await resolver.ResolveAsync("sprk_matter", "sprk_practicearea", "Employmnt Law", CancellationToken.None);

        result.Confidence.Should().Be(ResolutionConfidence.Low);
        result.Resolved.Should().Be("22222222-2222-2222-2222-222222222222");
        result.Candidates[0].Value.Should().Be("22222222-2222-2222-2222-222222222222"); // picker defaults to top
    }

    [Fact]
    public async Task ResolveAsync_NoClosedSet_ReturnsNoneWithoutThrowing()
    {
        // ADR-032 quiet no-op: a field whose sourcing yields no candidates resolves to None, never throws.
        var resolver = new StubResolver([]);

        var act = async () => await resolver.ResolveAsync("sprk_matter", "sprk_matterdescription", "anything", CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Confidence.Should().Be(ResolutionConfidence.None);
        result.Subject.Resolved.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_BlankProposal_ReturnsNone(string proposal)
    {
        var resolver = new StubResolver(PracticeAreas);

        var result = await resolver.ResolveAsync("sprk_matter", "sprk_practicearea", proposal, CancellationToken.None);

        result.Confidence.Should().Be(ResolutionConfidence.None);
    }

    [Fact]
    public void Resolver_HasNoLlmDependency()
    {
        // FR-B1 invariant: the resolver is deterministic — no OpenAI / LLM / chat-agent type is injected.
        var ctor = typeof(ConstrainedFieldResolver).GetConstructors().Single();
        var paramTypeNames = ctor.GetParameters()
            .Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)
            .ToList();

        foreach (var forbidden in new[] { "OpenAi", "Llm", "ChatCompletion", "ChatClient", "AgentFactory", "IChatAgent" })
        {
            paramTypeNames.Should().NotContain(
                n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                because: $"the constrained-field resolver must never take an LLM dependency ({forbidden})");
        }
    }
}
