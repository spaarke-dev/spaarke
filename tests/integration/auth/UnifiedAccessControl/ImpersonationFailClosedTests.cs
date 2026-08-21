using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Negative suite for the impersonated-read seam — the mechanism Phase 1 (spec FR-20, decision B-16)
/// uses to obtain Dataverse's REAL answer for a Type-1 systemuser instead of approximating it.
///
/// These assertions must ALREADY hold; nothing here pins broken behavior. They exist because tasks
/// 035/036 swap production reads onto this seam, and the guard below is the single thing standing
/// between a mis-wired call and an app-only query that would return org-wide rows to a scoped caller.
/// If this guard ever regresses, impersonation silently becomes inert — exactly the failure NFR-04's
/// negative canary (task 034) is designed to catch at the data level. This file guards it at the
/// API level, which is cheap and needs no live tenant.
///
/// Scope boundary: the row-count comparison itself (impersonated read MUST return strictly fewer rows
/// than app-only) requires a live Dataverse and is task 034's job, not this task's.
/// </summary>
public class ImpersonationFailClosedTests
{
    private static DataverseWebApiService NewService() =>
        new(
            new HttpClient(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
                ["Dataverse:ClientSecret"] = "test-secret"
            }).Build(),
            NullLogger<DataverseWebApiService>.Instance);

    /// <summary>
    /// The load-bearing guard (DataverseWebApiService.cs:962-965): an empty caller systemuserid MUST
    /// throw rather than fall back to an app-only query. Fail-closed by construction — the whole
    /// point is that a missing impersonation identity can never silently widen the result set.
    ///
    /// The guard runs before any credential or network use, so this test performs no I/O.
    /// </summary>
    [Fact]
    public async Task RetrieveMultipleImpersonatedAsync_WithEmptyCallerSystemUserId_ThrowsArgumentException()
    {
        // Arrange
        var sut = NewService();

        // Act
        var act = async () => await sut.RetrieveMultipleImpersonatedAsync(
            "sprk_matters",
            "$select=sprk_name",
            callerSystemUserId: Guid.Empty);

        // Assert — refuses rather than degrading to app-only.
        (await act.Should().ThrowAsync<ArgumentException>())
            .And.ParamName.Should().Be("callerSystemUserId");
    }

    /// <summary>
    /// The refusal message must keep naming its reason. Tasks 035/036 wire new callers onto this seam;
    /// if someone "fixes" the exception away, the message is the tripwire a reviewer sees.
    /// </summary>
    [Fact]
    public async Task RetrieveMultipleImpersonatedAsync_WithEmptyCallerSystemUserId_ExplainsFailClosedIntent()
    {
        var act = async () => await NewService().RetrieveMultipleImpersonatedAsync(
            "sprk_matters", odataQuery: null, callerSystemUserId: Guid.Empty);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*fail closed*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RetrieveMultipleImpersonatedAsync_WithBlankEntitySet_Throws(string entitySetName)
    {
        var act = async () => await NewService().RetrieveMultipleImpersonatedAsync(
            entitySetName,
            odataQuery: null,
            callerSystemUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .And.ParamName.Should().Be("entitySetName");
    }

    /// <summary>
    /// Argument validation order matters: a blank entity set AND an empty caller id must still throw
    /// (not proceed). Pins that neither guard can be bypassed by tripping the other first.
    /// </summary>
    [Fact]
    public async Task RetrieveMultipleImpersonatedAsync_WithBothArgumentsInvalid_Throws()
    {
        var act = async () => await NewService().RetrieveMultipleImpersonatedAsync(
            entitySetName: "", odataQuery: null, callerSystemUserId: Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
