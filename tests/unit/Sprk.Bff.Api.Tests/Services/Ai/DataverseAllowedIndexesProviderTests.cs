using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for multi-container-multi-index-r1 Phase G (task 102)
/// <see cref="DataverseAllowedIndexesProvider"/>.
///
/// Coverage:
///   • Lookup-from-Dataverse populates the cache; subsequent calls do NOT re-query.
///   • Empty Dataverse result → appsettings fallback + single WARNING log per TTL.
///   • Dataverse exception → appsettings fallback + single WARNING log per TTL.
///   • Case-insensitive Contains.
///   • Empty/whitespace indexName returns false without consulting Dataverse.
/// </summary>
public sealed class DataverseAllowedIndexesProviderTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly Mock<ILogger<DataverseAllowedIndexesProvider>> _loggerMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private DataverseAllowedIndexesProvider CreateProvider(string[]? appsettingsAllowed = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(_entityServiceMock.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = Options.Create(new AiSearchOptions
        {
            AllowedIndexes = appsettingsAllowed ?? Array.Empty<string>()
        });

        return new DataverseAllowedIndexesProvider(
            serviceProvider,
            _cache,
            options,
            _loggerMock.Object);
    }

    private static Entity CatalogRow(string indexName)
    {
        var e = new Entity("sprk_aisearchindex", Guid.NewGuid());
        e["sprk_searchindexname"] = indexName;
        return e;
    }

    private static EntityCollection Rows(params Entity[] rows)
    {
        var c = new EntityCollection();
        foreach (var r in rows) c.Entities.Add(r);
        return c;
    }

    [Fact]
    public async Task IsAllowedAsync_NameInDataverse_ReturnsTrue()
    {
        // Arrange
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(
                CatalogRow("spaarke-knowledge-index-v2"),
                CatalogRow("spaarke-file-index")));

        var provider = CreateProvider();

        // Act
        var result = await provider.IsAllowedAsync("spaarke-knowledge-index-v2", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_NameNotInDataverse_ReturnsFalse()
    {
        // Arrange
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(CatalogRow("spaarke-knowledge-index-v2")));

        var provider = CreateProvider();

        // Act
        var result = await provider.IsAllowedAsync("nonexistent-index", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAllowedAsync_CaseInsensitiveMatch()
    {
        // Arrange — Azure AI Search index names are case-insensitive at service level
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(CatalogRow("spaarke-knowledge-index-v2")));

        var provider = CreateProvider();

        // Act
        var result = await provider.IsAllowedAsync("SPAARKE-KNOWLEDGE-INDEX-V2", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_RepeatedCalls_HitsCacheNotDataverse()
    {
        // Arrange — verify the cache: 100 rapid calls trigger ONE Dataverse fetch.
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(CatalogRow("spaarke-file-index")));

        var provider = CreateProvider();

        // Act
        for (var i = 0; i < 100; i++)
        {
            await provider.IsAllowedAsync("spaarke-file-index", CancellationToken.None);
        }

        // Assert
        _entityServiceMock.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IsAllowedAsync_DataverseEmpty_FallsBackToAppsettingsWithWarning()
    {
        // Arrange — Dataverse returns 0 rows; appsettings fallback kicks in.
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows());

        var provider = CreateProvider(appsettingsAllowed: new[] { "spaarke-knowledge-index-v2" });

        // Act
        var allowedFromAppsettings = await provider.IsAllowedAsync("spaarke-knowledge-index-v2", CancellationToken.None);
        var notInAnySource = await provider.IsAllowedAsync("evil-index", CancellationToken.None);

        // Assert
        allowedFromAppsettings.Should().BeTrue("appsettings fallback is the floor when Dataverse is empty");
        notInAnySource.Should().BeFalse();

        // ONE warning (per TTL cycle) regardless of how many IsAllowedAsync calls fire.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("PhaseG.AllowedIndexes")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task IsAllowedAsync_DataverseThrows_FallsBackToAppsettingsWithWarning()
    {
        // Arrange — Dataverse throws; appsettings fallback kicks in.
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 503"));

        var provider = CreateProvider(appsettingsAllowed: new[] { "spaarke-file-index" });

        // Act
        var allowed = await provider.IsAllowedAsync("spaarke-file-index", CancellationToken.None);

        // Assert
        allowed.Should().BeTrue();

        // Warning logged once (and the failure result is cached for the TTL — no spam).
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("PhaseG.AllowedIndexes")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsAllowedAsync_NullOrWhitespaceName_ReturnsFalseWithoutQueryingDataverse(string? indexName)
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.IsAllowedAsync(indexName!, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _entityServiceMock.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IsAllowedAsync_RowWithBlankName_IsIgnored()
    {
        // Arrange — defensive: empty name in catalog should not match anything (incl. empty input).
        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(
                CatalogRow(""),
                CatalogRow("spaarke-knowledge-index-v2")));

        var provider = CreateProvider();

        // Act
        var emptyMatch = await provider.IsAllowedAsync(" ", CancellationToken.None);
        var validMatch = await provider.IsAllowedAsync("spaarke-knowledge-index-v2", CancellationToken.None);

        // Assert
        emptyMatch.Should().BeFalse();
        validMatch.Should().BeTrue();
    }
}
