using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 task 030 (FR-E2): <see cref="StatedProfileReader"/> reads the
/// caller's typed <c>sprk_userprofile</c> row (keyed by <c>sprk_systemuser</c>) + its N:N
/// <c>sprk_practicearea_ref</c> names, maps the <c>sprk_primaryrole</c> value→LABEL per the task-001
/// contract, and soft-fails to null (ADR-032 P2 quiet no-op) on an absent row, an invalid id, or any
/// Dataverse read error. Pinned against a mocked <see cref="IDataverseService"/> module boundary.
/// </summary>
public sealed class StatedProfileReaderTests
{
    private const string SystemUserId = "9b0e6a1e-0000-4000-8000-00000000abcd";

    private static StatedProfileReader BuildReader(IDataverseService dataverse) =>
        new(dataverse, Mock.Of<ILogger<StatedProfileReader>>());

    private static Mock<IDataverseService> DataverseReturning(
        EntityCollection profile, EntityCollection practiceAreas)
    {
        var mock = new Mock<IDataverseService>();
        mock.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                "sprk_userprofile" => profile,
                "sprk_practicearea_ref" => practiceAreas,
                _ => new EntityCollection(),
            });
        return mock;
    }

    [Fact]
    public async Task ReadAsync_ProfiledUser_MapsRoleLabelAndOrdinalPracticeAreaNames()
    {
        var profileRow = new Entity("sprk_userprofile", Guid.NewGuid())
        {
            ["sprk_primaryrole"] = new OptionSetValue(100000001), // Partner (task-001 contract)
            ["sprk_focusareas"] = "M&A and joint ventures",
            ["sprk_officelocation"] = "New York",
            ["sprk_assistantpreferences"] = "Concise, cite sources",
        };
        // Deliberately out of order to pin the reader's ordinal sort.
        var paLitigation = new Entity("sprk_practicearea_ref") { ["sprk_practiceareaname"] = "Litigation" };
        var paCorporate = new Entity("sprk_practicearea_ref") { ["sprk_practiceareaname"] = "Corporate" };

        var dataverse = DataverseReturning(
            new EntityCollection(new List<Entity> { profileRow }),
            new EntityCollection(new List<Entity> { paLitigation, paCorporate }));

        var profile = await BuildReader(dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.RoleLabel.Should().Be("Partner");
        profile.PracticeAreaNames.Should().Equal("Corporate", "Litigation");
        profile.FocusAreas.Should().Be("M&A and joint ventures");
        profile.OfficeLocation.Should().Be("New York");
        profile.AssistantPreferences.Should().Be("Concise, cite sources");
    }

    [Fact]
    public async Task ReadAsync_UnknownRoleValue_MapsRoleLabelToNull()
    {
        var profileRow = new Entity("sprk_userprofile", Guid.NewGuid())
        {
            ["sprk_primaryrole"] = new OptionSetValue(999999), // not in the contract
        };
        var dataverse = DataverseReturning(
            new EntityCollection(new List<Entity> { profileRow }),
            new EntityCollection());

        var profile = await BuildReader(dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.RoleLabel.Should().BeNull("an unknown/future role value maps to null — never a guessed label");
        profile.HasAny.Should().BeFalse("no populated stated field remains, so the profile renders to nothing");
    }

    [Fact]
    public async Task ReadAsync_NoProfileRow_ReturnsNull()
    {
        var dataverse = DataverseReturning(new EntityCollection(), new EntityCollection());

        var profile = await BuildReader(dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        profile.Should().BeNull("an unprofiled user has no sprk_userprofile row — everyday default, absent fragment");
    }

    [Fact]
    public async Task ReadAsync_InvalidSystemUserId_ReturnsNullWithoutQuerying()
    {
        var dataverse = new Mock<IDataverseService>();

        var profile = await BuildReader(dataverse.Object).ReadAsync("not-a-guid", CancellationToken.None);

        profile.Should().BeNull();
        dataverse.Verify(
            d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unparseable systemuserid short-circuits before any Dataverse read");
    }

    [Fact]
    public async Task ReadAsync_DataverseThrows_SoftFailsToNull()
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse outage"));

        var profile = await BuildReader(dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        profile.Should().BeNull("any Dataverse read error soft-fails to null (ADR-032 P2 quiet no-op)");
    }
}
