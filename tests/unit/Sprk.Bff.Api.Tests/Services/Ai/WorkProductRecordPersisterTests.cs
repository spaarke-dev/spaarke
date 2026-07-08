using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="TopicRegistryWorkProductPersister"/> — the FR-P3-08
/// work_product disposition persistence seam (spaarke-ai-architecture-redesign-r1
/// task 047).
///
/// <para>
/// Module boundary per ADR-038: the only test double is <see cref="IDataverseUserClient"/> —
/// the SAME user-OBO Web API boundary every <c>dataverse.*</c> handler test mocks.
/// Everything else (topic resolution, registry-mapping selection, envelope derivation,
/// PATCH construction) is the real persister.
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors a Binding-declared-persistence
/// contract the platform's composition story depends on — the topic-registry declaration
/// join (Binding capability code → <c>sprk_topicname</c>), the envelope-derives-from-ledger
/// rule (ADR-040), single-field If-Match PATCH idempotency (repeated routing never
/// duplicates), and the loud failure modes that keep a misdeclared catalog row from
/// silently dropping a user's work product.
/// </para>
/// </summary>
public class WorkProductRecordPersisterTests
{
    private static readonly Guid BindingId = Guid.Parse("3a1c30d1-cccc-f111-ab0e-70a8a590c51c");
    private static readonly Guid HostRecordId = Guid.Parse("7d9c30d1-bbbb-f111-ab0e-70a8a590c51c");

    private readonly Mock<IDataverseUserClient> _dataverse = new(MockBehavior.Strict);

    private TopicRegistryWorkProductPersister CreateSut() =>
        new(_dataverse.Object, Mock.Of<ILogger<TopicRegistryWorkProductPersister>>());

    // ─── Happy path: registry-declared target mapping → single-field PATCH ────────────────────

    [Fact]
    public async Task PersistAsync_RegisteredTopic_PatchesEnvelopeToRegistryDeclaredField()
    {
        SetupRegistry(topicFilterValue: "matter-summary");
        var patches = CapturePatches();

        var entry = BuildEntry();
        var receipt = await CreateSut().PersistAsync(entry, BuildBinding(), BuildHostContext());

        // The write is ONE PATCH against the registry-declared entity set + target field.
        var patch = patches.Should().ContainSingle().Subject;
        patch.Path.Should().Be($"sprk_matters({HostRecordId:D})");
        using var body = JsonDocument.Parse(patch.Body);
        body.RootElement.EnumerateObject().Should().ContainSingle(
            "the PATCH touches ONLY the registry-declared target column");

        // The envelope derives VERBATIM from the stored ledger entry (ADR-040).
        using var envelope = JsonDocument.Parse(body.RootElement.GetProperty("sprk_mattersummary").GetString()!);
        var root = envelope.RootElement;
        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        root.GetProperty("ledgerKey").GetString().Should().Be(entry.Key);
        root.GetProperty("bindingId").GetString().Should().Be(BindingId.ToString());
        root.GetProperty("ucId").GetString().Should().Be("UC-A-1");
        root.GetProperty("turn").GetInt32().Should().Be(entry.Turn);
        root.GetProperty("disposition").GetString().Should().Be("work_product");
        root.GetProperty("payload").GetProperty("summary").GetString().Should().Be("matter work product");
        root.GetProperty("sourceRefs").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "file-1" });

        receipt.EntityLogicalName.Should().Be("sprk_matter");
        receipt.RecordId.Should().Be(HostRecordId);
        receipt.TargetField.Should().Be("sprk_mattersummary");
        receipt.LedgerKey.Should().Be(entry.Key);
    }

    [Fact]
    public async Task PersistAsync_RepeatedForSameEntry_IsIdempotent_IdenticalPatchNoCreates()
    {
        SetupRegistry(topicFilterValue: "matter-summary");
        var patches = CapturePatches();

        var entry = BuildEntry();
        var sut = CreateSut();
        await sut.PersistAsync(entry, BuildBinding(), BuildHostContext());
        await sut.PersistAsync(entry, BuildBinding(), BuildHostContext());

        // Re-routing the same stored entry re-issues a BYTE-IDENTICAL single-field
        // overwrite of the same record — nothing is created, nothing duplicates.
        patches.Should().HaveCount(2);
        patches[1].Path.Should().Be(patches[0].Path);
        patches[1].Body.Should().Be(patches[0].Body);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "work-product persistence NEVER creates records (update-only If-Match PATCH)");
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Declaration join: the Binding's capability code selects the registry topic ───────────

    [Fact]
    public async Task PersistAsync_DefaultConsumerCode_ResolvesTopicFromConsumerType()
    {
        // A single-row capability (code 'default') keys the registry by its consumer TYPE.
        SetupRegistry(topicFilterValue: "chat-summarize");
        CapturePatches();

        var binding = BuildBinding() with { ConsumerCode = "default" };
        await CreateSut().PersistAsync(BuildEntry(), binding, BuildHostContext());

        _dataverse.Verify(d => d.GetAsync(
            It.Is<string>(p => p.Contains("sprk_topicname eq 'chat-summarize'")),
            It.IsAny<CancellationToken>()));
    }

    // ─── Loud failure modes: misdeclared catalog data never silently drops output ─────────────

    [Fact]
    public async Task PersistAsync_TopicNotRegistered_ThrowsLoudWithAuthoringGuidance()
    {
        SetupEntitySet(TopicRegistryWorkProductPersister.RegistryLogicalName, "sprk_aitopicregistries");
        SetupRegistryRows("matter-summary" /* no rows */);

        var act = () => CreateSut().PersistAsync(BuildEntry(), BuildBinding(), BuildHostContext());

        (await act.Should().ThrowAsync<InvalidOperationException>(
            "a Binding declaring work_product WITHOUT a registry target mapping is a catalog authoring error"))
            .Which.Message.Should().Contain("sprk_aitopicregistry").And.Contain("matter-summary");
    }

    [Fact]
    public async Task PersistAsync_HostEntityMismatch_ThrowsLoudNamingBothEntities()
    {
        // Registry targets sprk_project, but the session is hosted on a matter.
        SetupEntitySet(TopicRegistryWorkProductPersister.RegistryLogicalName, "sprk_aitopicregistries");
        SetupRegistryRows("matter-summary",
            ("sprk_project", "sprk_projectsummaryfield"));

        var act = () => CreateSut().PersistAsync(BuildEntry(), BuildBinding(), BuildHostContext());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("matter").And.Contain("sprk_project");
    }

    [Fact]
    public async Task PersistAsync_MalformedTargetField_ThrowsLoudBeforeAnyWrite()
    {
        // The registry is maker-editable NVARCHAR data — a non-logical-name value must be
        // rejected before it reaches an OData path or PATCH body.
        SetupEntitySet(TopicRegistryWorkProductPersister.RegistryLogicalName, "sprk_aitopicregistries");
        SetupRegistryRows("matter-summary",
            ("sprk_matter", "sprk_field);DROP"));

        var act = () => CreateSut().PersistAsync(BuildEntry(), BuildBinding(), BuildHostContext());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("malformed");
        _dataverse.Verify(
            d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PersistAsync_InvalidHostRecordId_ThrowsLoud()
    {
        var hostContext = new ChatHostContext("matter", "not-a-guid");

        var act = () => CreateSut().PersistAsync(BuildEntry(), BuildBinding(), hostContext);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("record id");
    }

    [Fact]
    public async Task PersistAsync_DataversePatchFails_ThrowsLoudCarryingUsersOwnError()
    {
        SetupRegistry(topicFilterValue: "matter-summary");
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, "DATAVERSE_ACCESS_DENIED",
                "Principal user is missing prvWritesprk_matter privilege."));

        var act = () => CreateSut().PersistAsync(BuildEntry(), BuildBinding(), BuildHostContext());

        // User-OBO: the USER's own access error surfaces; the ledger entry stays addressable.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("403").And.Contain("WAS stored");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static Binding BuildBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = "chat-summarize",
        ConsumerCode = "matter-summary",
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.WorkProduct,
    };

    private static ChatHostContext BuildHostContext() =>
        new("matter", HostRecordId.ToString("D"));

    private static SessionOutput BuildEntry() => new()
    {
        Key = SessionLedger.BuildOutputKey(BindingId.ToString(), 3),
        BindingId = BindingId.ToString(),
        UcId = "UC-A-1",
        Turn = 3,
        Disposition = "work_product",
        Payload = ParseJson("""{"summary":"matter work product"}"""),
        SourceRefs = new[] { "file-1" },
        CreatedAt = DateTimeOffset.Parse("2026-07-06T12:00:00Z"),
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Full happy-path Dataverse boundary: registry entity set + one matching registry row + host entity set.</summary>
    private void SetupRegistry(string topicFilterValue)
    {
        SetupEntitySet(TopicRegistryWorkProductPersister.RegistryLogicalName, "sprk_aitopicregistries");
        SetupRegistryRows(topicFilterValue, ("sprk_matter", "sprk_mattersummary"));
        SetupEntitySet("sprk_matter", "sprk_matters");
    }

    private void SetupEntitySet(string logicalName, string entitySetName) =>
        _dataverse
            .Setup(d => d.GetAsync(
                $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""{"EntitySetName":"{{entitySetName}}"}""")));

    private void SetupRegistryRows(string topic, params (string HostEntity, string TargetField)[] rows)
    {
        var rowsJson = string.Join(",", rows.Select(r =>
            $$"""{"sprk_topicname":"{{topic}}","sprk_mode":"single","sprk_hostentity":"{{r.HostEntity}}","sprk_targetfield":"{{r.TargetField}}"}"""));
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("sprk_aitopicregistries?")
                                   && p.Contains($"sprk_topicname eq '{topic}'")
                                   && p.Contains("sprk_enabled eq true")
                                   && p.Contains("statecode eq 0")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""{"value":[{{rowsJson}}]}""")));
    }

    private List<(string Path, string Body)> CapturePatches()
    {
        var patches = new List<(string Path, string Body)>();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, body, _) => patches.Add((path, body)))
            .ReturnsAsync(DataverseUserResponse.Ok(204, null));
        return patches;
    }
}
