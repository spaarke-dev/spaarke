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
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", It.IsAny<CancellationToken>()), Times.Once);
        scope.Verify(s => s.QueryLookupValuesAsync("sprk_triagecategorys", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
                "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", It.IsAny<CancellationToken>()))
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
    public async Task ResolveFromJps_NewTaxonomyRowWithGuidance_IsLiveWithNoDeployment()
    {
        // The zero-deployment property: names and guidance are read per run from the same rows, so a row added
        // in Dataverse appears in both on the next resolution with no code change.
        var (sut, scope) = Build(new[] { "Fee / rate change" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Fee / rate change"] = "old" });
        var before = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        var (sut2, scope2) = Build(new[] { "Fee / rate change", "Brand New Category" });
        scope2.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Keys.Should().OnlyContain(k => !LookupChoicesResolver.IsGuidanceKey(k));
    }

    [Fact]
    public async Task ResolveFromJps_GuidanceReadFails_KeepsBareNames_AndEmitsFailureMetric()
    {
        var (sut, scope) = Build(new[] { "Fee / rate change", "Scheduling" });
        scope.Setup(s => s.QueryLookupGuidanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        using var metrics = new FailureMetricCapture();
        var result = await sut.ResolveFromJpsAsync(JpsWithLookup(TriageRef));

        result[TriageRef].Should().Equal("Fee / rate change", "Scheduling");
        result.Should().NotContainKey(LookupChoicesResolver.GuidanceKey(TriageRef));
        metrics.Reasons(TriageRef).Should().Contain("guidance_read_failed");
    }

    [Fact]
    public async Task ResolveFromJps_NamesReadThrows_EmitsFailureMetric_NotSilent()
    {
        var scope = new Mock<IScopeResolverService>();
        scope.Setup(s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
