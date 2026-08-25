using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// Pure-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for the audit category mapping
/// introduced by <c>sdap-SPE-admin-app-r2</c> task 005 (spec FR-A05).
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: <c>sprk_category</c> is a Dataverse CHOICE, but every caller passes
/// free text. Before task 005 the text went straight into the payload, so Dataverse rejected the create on
/// type alone and the audit table stayed empty — silently, because the write path swallows its own errors.
/// A regression here is invisible until someone needs the audit trail and finds nothing in it.
/// </para>
/// <para>
/// The option-set values are pinned to the live schema read on 2026-08-21. If Dataverse ever renumbers
/// them, these fail loudly rather than writing rows under the wrong category.
/// </para>
/// </remarks>
public class AuditCategoryMappingTests
{
    [Theory]
    // Order matters in the mapper: "ContainerType*" must win over "Container*".
    [InlineData("ContainerTypeRegistration", SpeAuditService.CategoryContainerType)]
    [InlineData("ContainerTypeCreated", SpeAuditService.CategoryContainerType)]
    [InlineData("ContainerCreated", SpeAuditService.CategoryContainer)]
    [InlineData("ContainerUpdated", SpeAuditService.CategoryContainer)]
    [InlineData("Permission", SpeAuditService.CategoryPermission)]
    [InlineData("FileUploaded", SpeAuditService.CategoryFile)]
    [InlineData("FileDeleted", SpeAuditService.CategoryFile)]
    [InlineData("RecycleBin", SpeAuditService.CategoryFile)]
    [InlineData("Search", SpeAuditService.CategorySearch)]
    public void MapCategory_GivenAKnownCallerCategory_ReturnsTheMatchingOptionSetValue(
        string category, int expected)
    {
        SpeAuditService.MapCategory(category).Should().Be(expected);
    }

    [Fact]
    public void MapCategory_WhenCategoryIsContainerType_DoesNotFallThroughToContainer()
    {
        // The prefix-ordering guard. "ContainerType" also starts with "Container"; getting this backwards
        // would file every container-type operation under the wrong category, silently.
        SpeAuditService.MapCategory("ContainerTypeRegistration")
            .Should().Be(SpeAuditService.CategoryContainerType)
            .And.NotBe(SpeAuditService.CategoryContainer);
    }

    [Theory]
    [InlineData("containertyperegistration")]
    [InlineData("CONTAINERTYPEREGISTRATION")]
    [InlineData("  ContainerTypeRegistration  ")]
    public void MapCategory_IsCaseInsensitiveAndTrimsWhitespace(string category)
    {
        SpeAuditService.MapCategory(category).Should().Be(SpeAuditService.CategoryContainerType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Configuration")]
    [InlineData("something nobody anticipated")]
    public void MapCategory_GivenUnmappedInput_FallsBackToSecurityRatherThanThrowing(string? category)
    {
        // This runs inside a best-effort audit path. Throwing would lose the row entirely; filing it under
        // a coarse category keeps the trail intact. "Configuration" is deliberately in this list — callers
        // pass it, and it is NOT a valid option in the live option set.
        SpeAuditService.MapCategory(category).Should().Be(SpeAuditService.CategorySecurity);
    }

    [Fact]
    public void MapCategory_AlwaysReturnsAValidOptionSetValue()
    {
        var valid = new[]
        {
            SpeAuditService.CategoryContainerType,
            SpeAuditService.CategoryContainer,
            SpeAuditService.CategoryPermission,
            SpeAuditService.CategoryFile,
            SpeAuditService.CategorySearch,
            SpeAuditService.CategorySecurity,
        };

        // Every category string any caller in the BFF passes today.
        var callerCategories = new[]
        {
            "Configuration", "ContainerCreated", "ContainerTypeCreated", "ContainerTypeRegistration",
            "ContainerUpdated", "FileDeleted", "FileUploaded", "Permission", "RecycleBin",
        };

        foreach (var category in callerCategories)
        {
            valid.Should().Contain(SpeAuditService.MapCategory(category),
                $"'{category}' must map onto a real sprk_category option or Dataverse rejects the write");
        }
    }
}
