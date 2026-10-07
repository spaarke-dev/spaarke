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
}
