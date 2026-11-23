using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.ChildRecords;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 fix round 1 (F-1; UAC-r2 rule, 2026-11-03): a Matter create that NAMES
/// <c>sprk_issecure</c> — true, false or null alike (<see cref="AccessFollowsParent.NamesSecureFlag"/>) — is refused by the
/// unified <see cref="RecordCreationService"/> with <see cref="AccessFollowsParent.SecureFlagReasonCode"/> (403, the project's
/// refusal text), and nothing is written. Only Make Secure / Remove Secure set the flag. Pinned on the service itself, so every
/// path that creates a matter through it is covered: a caller's Web API body (the child-records route and the Office
/// quick-create both hand the service this shape) and a system principal's mapped item.
/// </summary>
/// <remarks>Driven over the cross-surface harness (<see cref="CreateWorld"/>): the real service, doubled only at the module
/// boundaries; no field security is scripted, so the refusal under test is the service's own, not Dataverse's.</remarks>
[Trait("status", "new")]
public sealed class MatterSecureFlagServiceRefusalTests
{
    private readonly CreateWorld _world = new();

    public static TheoryData<bool, bool?> PrincipalsAndValues => new()
    {
        { false, true }, { false, false }, { false, null },
        { true, true }, { true, false }, { true, null },
    };

    [Theory]
    [MemberData(nameof(PrincipalsAndValues))]
    public async Task AMatterCreate_NamingTheSecureFlag_IsRefusedSecureFlagTransitionOnly_403_AndNothingIsCreated(
        bool system, bool? value)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_mattername"] = "Acme v. Widget",
            [SecurableEntityRegistry.SecureFlagAttribute] = value,
        };

        var outcome = await _world.CreateAsync(Request(body, system), system,
            system ? RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid) : null);

        outcome.Succeeded.Should().BeFalse("naming the secure flag at all is refused, whatever its value");
        outcome.Refusal!.Code.Should().Be(AccessFollowsParent.SecureFlagReasonCode);
        outcome.Refusal.Kind.Should().Be(RecordCreationRefusalKind.Forbidden, "the project's HTTP mapping: 403");
        outcome.Refusal.Detail.Should().Be($"The matter was not created: {SecureRootInheritance.SecureFlagRefusalText}.");
        _world.Creates.Should().BeEmpty();
    }

    /// <summary>Must-not-fire: the same matter create without the column is created.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMatterCreate_WithoutTheSecureFlag_IsCreated(bool system)
    {
        var body = new Dictionary<string, object?> { ["sprk_mattername"] = "Acme v. Widget" };

        var outcome = await _world.CreateAsync(Request(body, system), system,
            system ? RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid) : null);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _world.Creates.Should().ContainSingle().Which.Fields.Should().NotContainKey(SecurableEntityRegistry.SecureFlagAttribute);
    }

    /// <summary>A caller's Web API body (route, Office), or a system principal's hand-built mapped item.</summary>
    private static RecordCreationRequest Request(Dictionary<string, object?> body, bool system) => system
        ? CreateWorld.Mapped("sprk_matter", body)
        : new RecordCreationRequest("sprk_matter", new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(body)));
}
