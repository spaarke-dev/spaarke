using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Regression tests for <see cref="LookupChoicesResolver"/> entity-set (collection) name derivation.
///
/// Production bug (email-communication-intelligence-r2, 2026-09-03): the resolver derived the OData
/// entity-set name with a naive <c>logicalName + "s"</c>, producing <c>sprk_triagecategorys</c> for
/// <c>sprk_triagecategory</c>. Dataverse's real set name is <c>sprk_triagecategories</c> (y → ies), so the
/// query 404'd, the <c>$choices</c> resolved to nothing, the TRIAGE-EMAIL prompt never listed the taxonomy
/// names, the model emitted a free-form category, and every email's <c>sprk_triagecategory</c> stayed unset.
/// These lock the corrected pluralization AND prove the common <c>"+ s"</c> case did not regress.
/// </summary>
public class LookupChoicesResolverTests
{
    private static string JpsWithLookup(string choicesRef) =>
        "{\"output\":{\"fields\":[{\"name\":\"category\",\"type\":\"string\",\"$choices\":\"" + choicesRef + "\"}]}}";

    private static (LookupChoicesResolver sut, Mock<IScopeResolverService> scope) Build(string[] returned)
    {
        var scope = new Mock<IScopeResolverService>();
        scope.Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returned);
        scope.Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returned);
        var sut = new LookupChoicesResolver(scope.Object, Mock.Of<ILogger<LookupChoicesResolver>>());
        return (sut, scope);
    }

    [Fact]
    public async Task ResolveFromJps_LookupToConsonantYEntity_UsesIesPluralEntitySetName()
    {
        // Arrange — the exact reference the TRIAGE-EMAIL Action uses.
        var (sut, scope) = Build(new[] { "Court / Filing", "Administrative" });

        // Act
        var result = await sut.ResolveFromJpsAsync(JpsWithLookup("lookup:sprk_triagecategory.sprk_name"));

        // Assert — queries the y→ies collection name, NOT the naive "+ s", and surfaces the values.
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", "sprk_enabled eq true", It.IsAny<CancellationToken>()), Times.Once);
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategorys", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Should().ContainKey("lookup:sprk_triagecategory.sprk_name");
        result["lookup:sprk_triagecategory.sprk_name"].Should().BeEquivalentTo("Court / Filing", "Administrative");
    }

    [Fact]
    public async Task ResolveFromJps_LookupToConsonantEndingEntity_KeepsNaivePluralEntitySetName()
    {
        // Non-regression control: a name that does NOT end in y/s/x/z/ch/sh keeps the naive "+ s" — the
        // fix must not disturb the many lookups that already resolved (e.g. sprk_mattertype_ref).
        var (sut, scope) = Build(new[] { "Litigation" });

        await sut.ResolveFromJpsAsync(JpsWithLookup("lookup:sprk_mattertype_ref.sprk_mattertypename"));

        scope.Verify(s => s.QueryLookupValuesAsync("sprk_mattertype_refs", "sprk_mattertypename", It.IsAny<CancellationToken>()), Times.Once);
    }

    private const string TriageRef = "lookup:sprk_triagecategory.sprk_name";

    [Fact]
    public async Task ResolveFromJps_TriageTaxonomy_EmitsNameThenGuidance_UnderSideKey_EnumKeyStaysBareNames()
    {
        var (sut, scope) = Build(new[] { "Fee / rate change", "Invoice / Billing", "Scheduling" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(
                "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", "sprk_enabled eq true", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fee / rate change"] = "A change to hourly rates or fee arrangements.",
                ["Invoice / Billing"] = "An invoice for work already done.",
            });

        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        result[TriageRef].Should().Equal("Fee / rate change", "Invoice / Billing", "Scheduling");
        result[LookupChoicesResolver.GuidanceKey(TriageRef)].Should().Equal(
            "Fee / rate change — A change to hourly rates or fee arrangements.",
            "Invoice / Billing — An invoice for work already done.",
            "Scheduling");
    }

    [Fact]
    public async Task ResolveChoicesReference_SameShapeAsJpsPath_NamesAndGuidanceFromTheSameEnabledRows()
    {
        // D-117(b): rung 5 resolves the taxonomy reference directly (no JPS); it must get exactly what the
        // TRIAGE-EMAIL Action gets for the same reference.
        var (sut, scope) = Build(new[] { "Fee / rate change", "Scheduling" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(
                "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", "sprk_enabled eq true", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Fee / rate change"] = "A change to rates." });

        var direct = await sut.ResolveChoicesReferenceAsync(TriageRef);
        var viaJps = await Build(new[] { "Fee / rate change", "Scheduling" }).sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        direct[TriageRef].Should().Equal("Fee / rate change", "Scheduling");
        direct[LookupChoicesResolver.GuidanceKey(TriageRef)].Should().Equal("Fee / rate change — A change to rates.", "Scheduling");
        direct[TriageRef].Should().Equal(viaJps[TriageRef]);
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", "sprk_enabled eq true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("downstream:node.field")]
    [InlineData("sprk_triagecategory.sprk_name")]
    public async Task ResolveChoicesReference_UnsupportedReference_ReturnsEmpty_AndReadsNothing(string choicesRef)
    {
        var (sut, scope) = Build(new[] { "x" });

        var result = await sut.ResolveChoicesReferenceAsync(choicesRef);

        result.Should().BeEmpty();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveFromJps_NewTaxonomyRowWithGuidance_IsLiveWithNoDeployment()
    {
        // The zero-deployment property: names and guidance are read per run from the same rows, so a row added
        // in Dataverse appears in both on the next resolution with no code change.
        var (sut, scope) = Build(new[] { "Fee / rate change" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Fee / rate change"] = "old" });
        var before = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        var (sut2, scope2) = Build(new[] { "Fee / rate change", "Brand New Category" });
        scope2.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Fee / rate change"] = "old", ["Brand New Category"] = "new guidance" });
        var after = await sut2.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        before[TriageRef].Should().NotContain("Brand New Category");
        after[TriageRef].Should().Contain("Brand New Category");
        after[LookupChoicesResolver.GuidanceKey(TriageRef)].Should().Contain("Brand New Category — new guidance");
    }

    [Fact]
    public async Task ResolveFromJps_EntityWithoutGuidanceColumn_IssuesNoGuidanceQuery()
    {
        var (sut, scope) = Build(new[] { "Litigation" });

        var result = await sut.ResolveFromJpsAsync(JpsWithLookup("lookup:sprk_mattertype_ref.sprk_mattertypename"));

        scope.Verify(s => s.QueryLookupGuidanceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Keys.Should().OnlyContain(k => !LookupChoicesResolver.IsGuidanceKey(k));
        // F1: the sprk_enabled predicate is per taxonomy. An entity without the column gets the unfiltered
        // overload, because a 400 on a missing column would be swallowed into an empty enum.
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_mattertype_refs", "sprk_mattertypename", It.IsAny<CancellationToken>()), Times.Once);
        scope.Verify(s => s.QueryLookupValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveFromJps_GuidanceReadFails_ListsBareNamesInPrompt_AndEmitsFailureMetric()
    {
        var (sut, scope) = Build(new[] { "Fee / rate change", "Scheduling" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        using var metrics = new FailureMetricCapture();
        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        result[TriageRef].Should().Equal("Fee / rate change", "Scheduling");
        result[LookupChoicesResolver.GuidanceKey(TriageRef)].Should().Equal(new[] { "Fee / rate change", "Scheduling" },
            "when guidance cannot be read the prompt still lists the categories, bare");
        metrics.Reasons(TriageRef).Should().Contain("guidance_read_failed");
    }

    [Fact]
    public async Task ResolveFromJps_NamesReadThrows_EmitsFailureMetric_NotSilent()
    {
        var scope = new Mock<IScopeResolverService>();
        scope.Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));
        var sut = new LookupChoicesResolver(scope.Object, Mock.Of<ILogger<LookupChoicesResolver>>());

        using var metrics = new FailureMetricCapture();
        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        result.Should().BeEmpty("the degradation itself is unchanged (best-effort, NFR-04)");
        metrics.Reasons(TriageRef).Should().Contain("read_failed");
    }

    [Fact]
    public async Task ResolveFromJps_NamesReadReturnsNothing_EmitsFailureMetric()
    {
        // ScopeResolverService swallows an HTTP error into an empty array, so "empty" is how a failed read
        // usually arrives; it must be observable too.
        var (sut, _) = Build(Array.Empty<string>());

        using var metrics = new FailureMetricCapture();
        await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        metrics.Reasons(TriageRef).Should().Contain("no_values");
    }

    private static (LookupChoicesResolver sut, Mock<IScopeResolverService> scope) BuildWithGuidance(
        string[] names, Dictionary<string, string> guidance)
    {
        var (sut, scope) = Build(names);
        scope.Setup(s => s.QueryLookupGuidanceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(guidance);
        return (sut, scope);
    }

    [Fact]
    public async Task ResolveFromJps_TriageTaxonomy_PassesEnabledFilterToBothQueries()
    {
        // F1: a disabled row must be absent from the enum AND the guidance. Dataverse does the filtering, so the
        // unit proof is that BOTH reads carry the predicate; the live test proves the predicate is accepted.
        var (sut, scope) = BuildWithGuidance(new[] { "Fee / rate change" }, new() { ["Fee / rate change"] = "g" });

        await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        scope.Verify(s => s.QueryLookupValuesAsync(
            "sprk_triagecategories", "sprk_name", "sprk_enabled eq true", It.IsAny<CancellationToken>()), Times.Once);
        scope.Verify(s => s.QueryLookupGuidanceAsync(
            "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", "sprk_enabled eq true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFromJps_GuidanceForRowOutsideTheEnum_IsDropped()
    {
        // A row the names query excluded (e.g. disabled) can never reach the prompt through a stale guidance map.
        var (sut, _) = BuildWithGuidance(new[] { "Fee / rate change" },
            new() { ["Fee / rate change"] = "g", ["Disabled Row"] = "should not appear" });

        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        result[LookupChoicesResolver.GuidanceKey(TriageRef)].Should().Equal("Fee / rate change — g");
    }

    [Fact]
    public void LookupUrls_CarryTheSamePerTaxonomyFilter_AndNoFilterWhenNone()
    {
        var values = ScopeResolverService.BuildLookupValuesUrl("sprk_triagecategories", "sprk_name", "sprk_enabled eq true");
        var guidance = ScopeResolverService.BuildLookupGuidanceUrl(
            "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", "sprk_enabled eq true");
        values.Should().EndWith("$filter=statecode eq 0 and (sprk_enabled eq true)");
        guidance.Should().EndWith("$filter=statecode eq 0 and (sprk_enabled eq true)");
        ScopeResolverService.BuildLookupValuesUrl("sprk_mattertype_refs", "sprk_mattertypename")
            .Should().EndWith("$filter=statecode eq 0");
    }

    [Fact]
    public async Task ResolveFromJps_GuidanceOver1000Chars_IsTruncatedTo1000PlusEllipsis()
    {
        var (sut, _) = BuildWithGuidance(new[] { "A" }, new() { ["A"] = new string('x', 1500) });

        var line = (await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef)))[LookupChoicesResolver.GuidanceKey(TriageRef)].Single();

        line.Should().Be("A — " + new string('x', 1000) + "…");
    }

    [Fact]
    public async Task ResolveFromJps_TruncationNeverSplitsASurrogatePair()
    {
        // Char 999 is the high half of an emoji, so cutting at 1000 would leave a lone surrogate.
        var text = new string('x', 999) + "\U0001F600" + new string('y', 100);
        var (sut, _) = BuildWithGuidance(new[] { "A" }, new() { ["A"] = text });

        var line = (await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef)))[LookupChoicesResolver.GuidanceKey(TriageRef)].Single();

        line.Should().Be("A — " + new string('x', 999) + "…");
        line.Any(char.IsSurrogate).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveFromJps_TotalGuidanceBudget_ListsRemainingRowsBare_EnumUnaffected()
    {
        var names = Enumerable.Range(1, 10).Select(i => "Cat" + i).ToArray();
        var guidance = names.ToDictionary(n => n, _ => new string('g', 1000));
        var (sut, _) = BuildWithGuidance(names, guidance);

        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        var lines = result[LookupChoicesResolver.GuidanceKey(TriageRef)];
        lines.Take(8).Should().OnlyContain(l => l.Contains(" — "), "8 x 1000 chars fits the 8000 budget");
        lines.Skip(8).Should().Equal("Cat9", "Cat10");
        result[TriageRef].Should().Equal(names, "the budget never touches the enum");
    }

    [Fact]
    public async Task ResolveFromJps_Cancellation_IsNotCountedAsAFailure()
    {
        var scope = new Mock<IScopeResolverService>();
        scope.Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = new LookupChoicesResolver(scope.Object, Mock.Of<ILogger<LookupChoicesResolver>>());

        using var metrics = new FailureMetricCapture();
        await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        metrics.Reasons(TriageRef).Should().NotContain("read_failed");
    }

    private sealed class FailureMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Reference, string Reason)> _seen = new();

        public FailureMetricCapture()
        {
            _listener.InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == "Sprk.Bff.Api.Ai" && inst.Name == "ai_choices_resolution_failures_total")
                    l.EnableMeasurementEvents(inst);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? reference = null, reason = null;
                foreach (var t in tags)
                {
                    if (t.Key == "reference") reference = t.Value as string;
                    if (t.Key == "reason") reason = t.Value as string;
                }
                lock (_seen) _seen.Add((reference ?? "", reason ?? ""));
            });
            _listener.Start();
        }

        public string[] Reasons(string reference)
        {
            lock (_seen) return _seen.Where(x => x.Reference == reference).Select(x => x.Reason).ToArray();
        }

        public void Dispose() => _listener.Dispose();
    }
}
