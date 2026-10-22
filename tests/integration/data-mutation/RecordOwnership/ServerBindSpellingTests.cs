using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 018b-2, #2238 (UAC-r2's 018a review, K-c): step 9 of the unified create writes
/// three server-owned values — the owner bind, the creator-person bind and the secure flag. A SYSTEM principal's item is not
/// checked by step 2, so it may already carry one of them under another spelling; the body's keys are case-sensitive, so the
/// server's canonical key is written only after any case-variant spelling is removed (the K3 rule for the search-index bind,
/// <see cref="SearchIndexDefaultTests"/>). The create body never holds two keys that differ only in case.
/// </summary>
/// <remarks>
/// The secure flag is never written beside a variant: every table whose step 9 writes it (a project or work assignment
/// created into isolation) refuses ANY spelling of the flag in its item at the step-6 plan, before step 9 — pinned here.
/// </remarks>
[Trait("status", "new")]
public sealed class ServerBindSpellingTests
{
    private const string OwnerBind = "ownerid@odata.bind";
    private const string CreatorBind = "sprk_CreatedByPerson@odata.bind";

    private readonly CreateWorld _world = new();

    [Theory]
    [InlineData("OwnerId@odata.bind", "a case-variant spelling of the owner bind")]
    [InlineData(OwnerBind, "the canonical spelling of the owner bind")]
    public async Task ASystemItemCarryingTheOwnerBind_IsCreatedWithOneOwnerKey_TheServersCanonicalOne(string spelled, string because)
    {
        var fields = await SystemCreatedFieldsAsync(EventUnderTheOrdinaryMatter(spelled, $"/teams({Guid.NewGuid():D})"));

        fields.Keys.Where(k => string.Equals(k, OwnerBind, StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle($"{because}: the body never carries two keys that differ only in case")
            .Which.Should().Be(OwnerBind, "the server's owner is written under the canonical key");
        CreateWorld.Owner(fields).Should().Be(Directory.ChildTeam, "the owner is the server's (record-first), never the item's");
    }

    [Theory]
    [InlineData("sprk_createdbyperson@odata.bind", "a case-variant spelling of the creator-person bind")]
    [InlineData(CreatorBind, "the canonical spelling of the creator-person bind")]
    public async Task ASystemItemCarryingTheCreatorPersonBind_IsCreatedWithOneCreatorKey_TheServersCanonicalOne(string spelled, string because)
    {
        var fields = await SystemCreatedFieldsAsync(
            EventUnderTheOrdinaryMatter(spelled, $"/systemusers({Guid.NewGuid():D})"),
            RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid));

        fields.Keys.Where(k => string.Equals(k, CreatorBind, StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle($"{because}: the body never carries two keys that differ only in case")
            .Which.Should().Be(CreatorBind, "the server's creator person is written under the canonical key");
        fields[CreatorBind].Should().Be($"/systemusers({Directory.CallerUserId:D})", "the requester, never the item's value");
    }

    [Theory]
    [InlineData("sprk_IsSecure", "a case-variant spelling of the secure flag")]
    [InlineData("sprk_issecure", "the canonical spelling of the secure flag")]
    public async Task ASystemItemCarryingTheSecureFlag_OnATableWhoseStep9WritesIt_IsRefusedBeforeStep9_AndNothingIsCreated(
        string spelled, string because)
    {
        var outcome = await _world.CreateAsync(
            CreateWorld.Mapped("sprk_workassignment", new Dictionary<string, object?> { ["sprk_name"] = "Review", [spelled] = false }),
            system: true, requestedBy: RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid));

        outcome.Succeeded.Should().BeFalse(because);
        outcome.Refusal!.Code.Should().Be(AccessFollowsParent.SecureFlagReasonCode, "a create never carries the flag, in any spelling");
        _world.Creates.Should().BeEmpty("refused at the step-6 plan, so step 9 never writes the flag beside it");
    }

    /// <summary>A system writer's event filed under the ordinary matter, also carrying <paramref name="key"/> = <paramref name="value"/>.</summary>
    private static RecordCreationRequest EventUnderTheOrdinaryMatter(string key, string value) => CreateWorld.Mapped("sprk_event",
        new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})",
            [key] = value,
        },
        new DataverseWriteItemMapper.MappedLookup("sprk_regardingmatter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));

    private async Task<IReadOnlyDictionary<string, object?>> SystemCreatedFieldsAsync(
        RecordCreationRequest request, RecordRequester? requestedBy = null)
    {
        var outcome = await _world.CreateAsync(request, system: true, requestedBy: requestedBy);
        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        return _world.Creates.Should().ContainSingle().Subject.Fields;
    }
}
