using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.ChildRecords;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 fix round 1 (F-1): the chat create (<c>dataverse.create_record</c>) of a
/// matter that names <c>sprk_issecure</c> reaches the unified service, which refuses it with
/// <see cref="AccessFollowsParent.SecureFlagReasonCode"/> after the confirmation (as a project's is), and nothing is created.
/// Driven through the REAL handler over the scripted caller's Dataverse and the real service (the
/// <c>ChatPreSuspendServiceRefusalTests</c> harness, copied).
/// </summary>
[Trait("status", "new")]
public sealed class MatterSecureFlagChatRefusalTests : TypedToolHandlerTestFixture
{
    private static readonly Guid Caller = Guid.Parse("a2460000-0000-4000-8000-0000000020f1");
    private static readonly Guid CallerOid = Guid.Parse("a2460000-0000-4000-8000-0000000020f2");

    private readonly Directory _world = Directory.Standard().WithUser(Caller, CallerOid, Directory.ChildBu);
    private readonly SecureChildOwnershipAiToolTests.ScriptedUserClient _user = new(Caller);
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);
    private readonly List<string> _appCreates = new();

    public MatterSecureFlagChatRefusalTests()
    {
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, _, _, _, _) => _appCreates.Add(t))
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public async Task ChatCreate_OfAMatterNamingTheSecureFlag_IsRefusedSecureFlagTransitionOnly_AndNothingIsCreated(string value)
    {
        var args = JsonSerializer.Serialize(new
        {
            tablename = "sprk_matter",
            item = new Dictionary<string, JsonElement>
            {
                ["sprk_mattername"] = JsonSerializer.SerializeToElement("Acme v. Widget"),
                ["sprk_issecure"] = JsonDocument.Parse(value).RootElement.Clone(),
            },
        });

        var result = await Handler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: args) with { UserId = CallerOid.ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(AccessFollowsParent.SecureFlagReasonCode, result.ErrorMessage);
        _appCreates.Should().BeEmpty();
    }

    private DataverseCreateRecordHandler Handler() =>
        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            _world.Resolver(), IdentityNormalizationFixtures.NoLinkedContact(),
            RouteRecordCreation.Default(_world.Resolver(), _appOnly.Object, SecureRootFilingGateFixtures.NothingSecure()));
}
