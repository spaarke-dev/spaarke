// R4 Task 028 — customData schema-conformance xUnit fixture (retargeted at NotificationActionCore, D-100 / task 131).
//
// Spec FR-6 AC-6a/b/c/d; FR-10 AC-10.
//
// This file originally drove the CreateNotification node executor for the seven notification playbooks. Those playbooks
// and the executor were removed (D-100). The FR-6 payload is still produced by NotificationActionCore.BuildNotificationEntity,
// which IActionSeam.CreateNotificationAsync (comms-RI, OutputRouter) uses, so the same invariants are asserted against
// the core directly: enriched fields, backward-compatible legacy shape, the 10 KB payload cap and the sprk_category
// dual-write. The seven fixture rows are kept as seven representative channel shapes.

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Schema-conformance fixture for the FR-6 enriched customData payload produced by
/// <c>NotificationActionCore.BuildNotificationEntity</c>.
///
///   AC-6a: enriched fields present (regardingName/regardingEntityType/regardingId,
///          source.{entityType,id,modifiedOn,owningUser}, viaMatter.{id,name,memberships[]})
///   AC-6b: backward compat, the pre-enrichment shape still produces valid notifications
///   AC-6c: payload under 10KB (UTF-8 bytes) across all representative fixtures
///   AC-6d: sprk_category column dual-write across all representative fixtures
/// </summary>
[Trait("ac", "FR-6/FR-10")]
[Trait("rigor", "STANDARD")]
public class CustomDataSchemaConformanceTests
{
    // Columns: fixtureCode, category, sourceEntityType, regardingType, expectDueDate, channelLabel
    public static IEnumerable<object[]> ChannelFixtures => new[]
    {
        new object[] { "CH-1", "new-documents",    "sprk_document",       "sprk_matter", false, "New Documents" },
        new object[] { "CH-2", "matter-activity",  "sprk_event",          "sprk_matter", false, "Matter Activity" },
        new object[] { "CH-3", "new-emails",       "sprk_communication",  "sprk_matter", false, "New Emails" },
        new object[] { "CH-4", "new-events",       "sprk_event",          "sprk_matter", false, "New Events" },
        new object[] { "CH-5", "tasks-due-soon",   "sprk_event",          "sprk_matter", true,  "Tasks Due Soon" },
        new object[] { "CH-6", "tasks-overdue",    "sprk_event",          "sprk_matter", true,  "Tasks Overdue" },
        new object[] { "CH-7", "work-assignments", "sprk_workassignment", "sprk_matter", false, "Work Assignments" },
    };

    [Theory]
    [MemberData(nameof(ChannelFixtures))]
    public async Task AllChannelFixtures_EmitFR6Schema(
        string code, string category, string sourceEntityType, string regardingType, bool expectDueDate, string channelLabel)
    {
        var matterId = Guid.NewGuid();
        var sourceRecordId = Guid.NewGuid();
        var input = EnrichedInput(category, sourceEntityType, regardingType, channelLabel, matterId, sourceRecordId, expectDueDate, ["owner"]);

        var captured = await CreateAsync(input);

        var customData = ExtractCustomData(captured);
        customData.GetProperty("regardingName").GetString().Should().NotBeNullOrEmpty($"AC-6a/{code}: regardingName");
        customData.GetProperty("regardingEntityType").GetString().Should().Be(regardingType, $"AC-6a/{code}");
        customData.GetProperty("regardingId").GetString().Should().NotBeNullOrEmpty($"AC-6a/{code}");

        var source = customData.GetProperty("source");
        source.GetProperty("entityType").GetString().Should().Be(sourceEntityType, $"AC-6a/{code}");
        source.GetProperty("id").GetString().Should().Be(sourceRecordId.ToString(), $"AC-6a/{code}");
        source.TryGetProperty("modifiedOn", out _).Should().BeTrue($"AC-6a/{code}: source.modifiedOn");
        source.TryGetProperty("owningUser", out _).Should().BeTrue($"AC-6a/{code}: source.owningUser");

        var viaMatter = customData.GetProperty("viaMatter");
        viaMatter.GetProperty("id").GetString().Should().Be(matterId.ToString(), $"AC-6a/{code}");
        viaMatter.TryGetProperty("name", out _).Should().BeTrue($"AC-6a/{code}: viaMatter.name");
        viaMatter.GetProperty("memberships").GetArrayLength().Should().BeGreaterThan(0, $"AC-6a/{code}: memberships[]");

        if (expectDueDate)
        {
            customData.GetProperty("dueDate").GetString().Should().NotBeNullOrEmpty($"AC-6a/{code}: dueDate");
        }

        customData.GetProperty("actionUrl").GetString().Should().NotBeNullOrEmpty($"AC-6a/{code}: actionUrl");
    }

    [Theory]
    [MemberData(nameof(ChannelFixtures))]
    public async Task AllChannelFixtures_HaveSprkCategoryDualWrite(
        string code, string category, string sourceEntityType, string regardingType, bool expectDueDate, string channelLabel)
    {
        var input = EnrichedInput(category, sourceEntityType, regardingType, channelLabel, Guid.NewGuid(), Guid.NewGuid(), expectDueDate, ["owner"]);

        var captured = await CreateAsync(input);

        captured.Contains("sprk_category").Should().BeTrue($"AC-6d/{code}: sprk_category column MUST be populated so the category $filter works");
        captured["sprk_category"].Should().Be(category, $"AC-6d/{code}: the column mirrors the category exactly");
    }

    [Theory]
    [MemberData(nameof(ChannelFixtures))]
    public async Task AllChannelFixtures_PayloadSizeUnder10KB(
        string code, string category, string sourceEntityType, string regardingType, bool expectDueDate, string channelLabel)
    {
        var input = EnrichedInput(category, sourceEntityType, regardingType, channelLabel, Guid.NewGuid(), Guid.NewGuid(), expectDueDate,
            ["owner", "assignedAttorney", "assignedParalegal"]);

        var captured = await CreateAsync(input);

        var sizeBytes = Encoding.UTF8.GetByteCount((string)captured["data"]);
        sizeBytes.Should().BeLessThan(10_000, $"AC-6c/{code}: appnotification.data MUST stay under 10KB (actual {sizeBytes} bytes, 3 roles)");
    }

    [Fact]
    public async Task BackwardCompat_OldShapeStillValid()
    {
        var input = Input() with { Category = "general", ActionUrl = "/main.aspx?id=123", DueDate = "2026-07-01T00:00:00Z" };

        var captured = await CreateAsync(input);

        var customData = ExtractCustomData(captured);
        customData.GetProperty("actionUrl").GetString().Should().Be("/main.aspx?id=123", "AC-6b: legacy actionUrl survives");
        customData.GetProperty("dueDate").GetString().Should().Be("2026-07-01T00:00:00Z", "AC-6b: legacy dueDate survives");
        customData.TryGetProperty("regardingName", out _).Should().BeFalse("AC-6b: enriched fields absent when not supplied");
        customData.TryGetProperty("viaMatter", out _).Should().BeFalse("AC-6b: viaMatter absent (not null) without matter linkage");
        customData.TryGetProperty("source", out _).Should().BeFalse("AC-6b: source absent without source-record info");
    }

    [Fact]
    public async Task MissingMatterLinkage_ViaMatterFieldOmitted()
    {
        var input = Input() with
        {
            Category = "general",
            ActionUrl = "/somewhere",
            RegardingName = "Standalone record",
            SourceEntityType = "sprk_event",
            SourceId = Guid.NewGuid().ToString(),
            SourceModifiedOn = "2026-06-25T12:00:00Z",
        };

        var captured = await CreateAsync(input);

        var customData = ExtractCustomData(captured);
        customData.TryGetProperty("viaMatter", out _).Should().BeFalse("AC-6: viaMatter ABSENT (not present-as-null) when no matter linkage");
        customData.GetProperty("regardingName").GetString().Should().Be("Standalone record");
        customData.GetProperty("source").GetProperty("entityType").GetString().Should().Be("sprk_event");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static NotificationActionInput Input() => new(
        Title: "Title", Body: "Body", Category: null, Priority: 200_000_000, ToastType: 200_000_000, ActionUrl: null,
        RecipientId: Guid.NewGuid(), RegardingId: null, RegardingType: null, DueDate: null, RegardingName: null,
        SourceEntityType: null, SourceId: null, SourceModifiedOn: null, SourceOwningUser: null, ViaMatterId: null,
        ViaMatterName: null, ViaMatterMemberships: null, Source: "system", CorrelationId: string.Empty);

    private static NotificationActionInput EnrichedInput(
        string category, string sourceEntityType, string regardingType, string channelLabel, Guid matterId, Guid sourceRecordId,
        bool withDueDate, string[] roles) =>
        Input() with
        {
            Title = $"{channelLabel}: {sourceEntityType} update",
            Body = $"Activity on {channelLabel} channel",
            Category = category,
            ActionUrl = $"/main.aspx?pagetype=entityrecord&etn={sourceEntityType}&id={sourceRecordId}",
            RegardingId = matterId,
            RegardingType = regardingType,
            DueDate = withDueDate ? "2026-07-01T17:00:00Z" : null,
            RegardingName = "Acme Corp v. Smith Industries",
            SourceEntityType = sourceEntityType,
            SourceId = sourceRecordId.ToString(),
            SourceModifiedOn = "2026-06-25T12:00:00Z",
            SourceOwningUser = Guid.NewGuid().ToString(),
            ViaMatterId = matterId.ToString(),
            ViaMatterName = "Acme Corp v. Smith Industries",
            ViaMatterMemberships = roles.Select(r => (object)new { role = r, matterId = matterId.ToString() }).ToList(),
        };

    private static async Task<Entity> CreateAsync(NotificationActionInput input)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        Entity? captured = null;
        entities.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(Guid.NewGuid());

        var result = await new NotificationActionCore(entities.Object, NullLogger.Instance).CreateAsync(input, CancellationToken.None);

        result.Skipped.Should().BeFalse();
        captured.Should().NotBeNull("BuildNotificationEntity MUST run and produce an Entity");
        return captured!;
    }

    private static JsonElement ExtractCustomData(Entity entity)
    {
        entity.Contains("data").Should().BeTrue("BuildNotificationEntity MUST populate entity['data'] when any customData field is set");
        using var doc = JsonDocument.Parse((string)entity["data"]);
        return doc.RootElement.GetProperty("customData").Clone();
    }
}
