using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Unit tests for <see cref="ExternalDataService"/> covering the public surface introduced by
/// smart-todo-decoupling-r3 task 007 (FR-29). The service interacts with Dataverse via
/// HttpClient + managed-identity tokens, so end-to-end query/write semantics are exercised
/// by integration tests; here we verify the static helpers + the ADR-024 resolver-URL contract.
///
/// Product portability: <see cref="ExternalDataService.BuildRecordUrl"/> MUST return a
/// RELATIVE URL — no org URL or tenant id may be hard-coded.
/// </summary>
[Trait("status", "repaired")]
public class ExternalDataServiceTests
{
    // =========================================================================
    // BuildRecordUrl (ADR-024 resolver field — sprk_regardingrecordurl)
    //
    // Mirrors the contract enforced by TodoRegardingBuilder.BuildRecordUrl. The URL is
    // RELATIVE so it works across environments (dev, staging, prod) without rebaking
    // the BFF or DTO contract per environment.
    // =========================================================================

    [Fact]
    public void BuildRecordUrl_IsRelative_NoOrgUrlHardcoded()
    {
        // Arrange
        var entityName = "sprk_project";
        var recordId = "11111111-1111-1111-1111-111111111111";

        // Act
        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        // Assert
        url.Should().StartWith("/main.aspx",
            "the record URL must be relative — host origin is resolved at click time per ADR-024 portability");
        url.Should().NotStartWith("http://",
            "no absolute org URL allowed (product portability)");
        url.Should().NotStartWith("https://",
            "no absolute org URL allowed (product portability)");
        url.Should().NotContain(".dynamics.com",
            "no Dataverse org host may be embedded in DTO contracts");
        url.Should().NotContain(".crm.",
            "no Dataverse org host may be embedded in DTO contracts");
    }

    [Fact]
    public void BuildRecordUrl_EncodesEntityAndIdInQuery()
    {
        var entityName = "sprk_project";
        var recordId = "22222222-2222-2222-2222-222222222222";

        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        url.Should().Contain("etn=sprk_project");
        url.Should().Contain($"id={recordId}");
        url.Should().Contain("pagetype=entityrecord");
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_event")]
    [InlineData("sprk_invoice")]
    [InlineData("contact")]
    public void BuildRecordUrl_WorksAcrossAllRegardingTargetEntities(string entityName)
    {
        // The resolver URL pattern is uniform across all 11 sprk_todo regarding targets
        // (per entity-schema.md). The External Access surface only writes the project
        // regarding here, but the helper is entity-agnostic for symmetry with
        // TodoRegardingBuilder.
        var recordId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        url.Should().Contain($"etn={entityName}");
        url.Should().Contain($"id={recordId}");
        url.Should().StartWith("/main.aspx");
    }
}
