using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Regression for #2919. The Dataverse ServiceClient returns a row's version on <see cref="Entity.RowVersion"/> for a
/// <c>ColumnSet(true)</c> read, not as the <c>versionnumber</c> attribute. The Direct owner step read only the attribute, so on a live
/// environment every Direct message kept its creator as owner (error 5415, "it could not be read before its Direct owner write").
/// These rows carry the version the way the ServiceClient returns it. They run on the <see cref="DirectMessageOwnerStepTests"/> rig.
/// </summary>
public partial class DirectMessageOwnerStepTests
{
    [Fact]
    public async Task Issue2919_VersionOnlyOnRowVersion_TheMessageAndItsContentRowsMoveToTheDirectTeam_AtThatVersion()
    {
        string? messageVersion = null;
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync("sprk_communication", MessageId, It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, Dictionary<string, object>, CancellationToken>((_, _, v, _, _) => messageVersion = v)
            .Returns((string table, Guid id, string v, Dictionary<string, object> fields, CancellationToken _) => Write(table, id, v, fields));

        var step = Step(Resolved());
        var join = await step.GrantAtJoinAsync(MessageId, Direct(), CancellationToken.None);
        WriteContentAfterJoin();
        VersionsOnRowVersionOnly();
        await step.OwnDirectAfterContentAsync(join!, CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        messageVersion.Should().Be("101", "the owner write is conditional on the version the read returned");
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(DirectTeam);
        OwningTeam("sprk_communicationparticipant", Participant).Should().Be(DirectTeam);
        OwningTeam("sprk_emailreviewlog", ReviewLog).Should().Be(DirectTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(DirectTeam);
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Issue2919_NoVersionOnEitherSource_TheMessageKeepsItsOwner_WithError5415_AndNothingIsWritten()
    {
        var message = _rows[("sprk_communication", MessageId)];
        message.Attributes.Remove("versionnumber");
        message.RowVersion = null;

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        Owner("sprk_communication", MessageId).Should().Be(AppUser);
        OwningTeam("sprk_communicationattachment", Attachment).Should().BeNull();
        _dataverse.Verify(d => d.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5415);
    }

    [Theory]
    [InlineData("101", 101L)]
    [InlineData("", null)]
    [InlineData("-5", null)]
    [InlineData("1.5", null)]
    public void Issue2919_VersionOf_ReadsRowVersionWhenTheAttributeIsAbsent(string rowVersion, long? expected)
    {
        var row = new Entity("sprk_communication", MessageId) { RowVersion = rowVersion };

        DirectMessageOwnerStep.VersionOf(row).Should().Be(expected);
    }

    [Fact]
    public void Issue2919_VersionOf_TheAttributeWinsOverRowVersion()
    {
        var row = new Entity("sprk_communication", MessageId) { ["versionnumber"] = 7L, RowVersion = "9" };

        DirectMessageOwnerStep.VersionOf(row).Should().Be(7L);
    }

    /// <summary>Moves every stored row's version from the <c>versionnumber</c> attribute to <see cref="Entity.RowVersion"/>, as a
    /// ServiceClient <c>ColumnSet(true)</c> read returns it.</summary>
    private void VersionsOnRowVersionOnly()
    {
        foreach (var row in _rows.Values)
        {
            if (row.GetAttributeValue<long?>("versionnumber") is not { } version)
                continue;
            row.Attributes.Remove("versionnumber");
            row.RowVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
