using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.ChildRecords;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 fix round 2 (F-2): a SYSTEM principal's mapped item that names
/// <c>sprk_issecure</c> or <c>sprk_accessinheritance</c> as a JSON <c>null</c> — a value <c>OwnedChildWrite.WritesOf</c> leaves
/// out (it lists a null only for a cleared lookup), so the secure-root plan never saw it — is refused by the unified
/// <see cref="RecordCreationService"/> exactly as a non-null value is (the plan's code, words and 403), and nothing is written.
/// Unfiled project and work assignment: the plan runs, but before the fix it read only <c>WritesOf</c>.
/// </summary>
[Trait("status", "new")]
public sealed class SecureRootServerColumnRefusalTests
{
    private readonly CreateWorld _world = new();

    public static TheoryData<string, string, string, object?> InheritingRootsAndServerColumns => new()
    {
        // The JSON-null rows (F-2): created before the fix.
        { "sprk_project", "sprk_projectname", SecurableColumns.SecureFlag, null },
        { "sprk_workassignment", "sprk_name", SecurableColumns.SecureFlag, null },
        { "sprk_project", "sprk_projectname", SecurableColumns.AccessRecord, null },
        { "sprk_workassignment", "sprk_name", SecurableColumns.AccessRecord, null },
        // Controls: a non-null value, already refused by the plan; the answer must be the same.
        { "sprk_project", "sprk_projectname", SecurableColumns.SecureFlag, false },
        { "sprk_workassignment", "sprk_name", SecurableColumns.SecureFlag, false },
        { "sprk_project", "sprk_projectname", SecurableColumns.AccessRecord, "{}" },
        { "sprk_workassignment", "sprk_name", SecurableColumns.AccessRecord, "{}" },
    };

    [Theory]
    [MemberData(nameof(InheritingRootsAndServerColumns))]
    public async Task ASystemCreate_OfAnUnfiledInheritingRoot_NamingAServerColumn_IsRefused403_AndNothingIsCreated(
        string table, string nameColumn, string column, object? value)
    {
        var outcome = await SystemCreateAsync(table, new Dictionary<string, object?> { [nameColumn] = "x", [column] = value });

        outcome.Succeeded.Should().BeFalse("naming the column at all is refused, a JSON null included");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Forbidden);
        var noun = RecordCreationService.NounOf(table);
        if (column == SecurableColumns.SecureFlag)
        {
            outcome.Refusal.Code.Should().Be(AccessFollowsParent.SecureFlagReasonCode);
            outcome.Refusal.Detail.Should().Be($"The {noun} was not created: {SecureRootInheritance.SecureFlagRefusalText}.");
        }
        else
        {
            outcome.Refusal.Code.Should().Be(AccessInheritance.ServerOnlyReasonCode);
            // Round 3 (UAC-r2 review note 1): the plan's own words, with its noun.
            outcome.Refusal.Detail.Should().Be(
                $"The {noun} was not created: {SecurableColumns.AccessRecord} is written only by Spaarke, so the {noun} was not created.");
        }

        _world.Creates.Should().BeEmpty();
    }

    /// <summary>
    /// Round 3 (review #8): a hand-built system item whose column list LEAVES OUT the JSON-null key its body still carries —
    /// neither <c>WritesOf</c> nor the columns name it, only the body does — is refused all the same, nothing written.
    /// </summary>
    [Theory]
    [InlineData("sprk_project", "sprk_projectname", SecurableColumns.SecureFlag, AccessFollowsParent.SecureFlagReasonCode)]
    [InlineData("sprk_workassignment", "sprk_name", SecurableColumns.AccessRecord, AccessInheritance.ServerOnlyReasonCode)]
    [InlineData("sprk_matter", "sprk_mattername", SecurableColumns.SecureFlag, AccessFollowsParent.SecureFlagReasonCode)]
    public async Task ASystemItem_WhoseColumnsLeaveOutABodyNull_IsStillRefused(string table, string nameColumn, string column, string code)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> { [nameColumn] = "x", [column] = null });
        var item = new DataverseWriteItemMapper.MappedItem(json, [nameColumn]);

        var outcome = await _world.CreateAsync(new RecordCreationRequest(table, new RecordCreationPayload.Mapped(item)),
            system: true, requestedBy: RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid));

        outcome.Succeeded.Should().BeFalse("the body names the column, whatever the column list says");
        outcome.Refusal!.Code.Should().Be(code);
        _world.Creates.Should().BeEmpty();
    }

    /// <summary>Must-not-fire: the same system create without either column is created, for all three secure roots.</summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_mattername")]
    [InlineData("sprk_project", "sprk_projectname")]
    [InlineData("sprk_workassignment", "sprk_name")]
    public async Task ASystemCreate_OfAnUnfiledSecureRoot_WithoutTheServerColumns_IsCreated(string table, string nameColumn)
    {
        var outcome = await SystemCreateAsync(table, new Dictionary<string, object?> { [nameColumn] = "x" });

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _world.Creates.Should().ContainSingle().Which.Fields.Keys.Should()
            .NotContain(k => k == SecurableColumns.SecureFlag || k == SecurableColumns.AccessRecord);
    }

    private Task<RecordCreationOutcome> SystemCreateAsync(string table, Dictionary<string, object?> body) =>
        _world.CreateAsync(CreateWorld.Mapped(table, body), system: true,
            requestedBy: RecordRequester.Of(Directory.CallerUserId, Directory.CallerOid));

    private static class SecurableColumns
    {
        public const string SecureFlag = "sprk_issecure";
        public const string AccessRecord = "sprk_accessinheritance";
    }
}
