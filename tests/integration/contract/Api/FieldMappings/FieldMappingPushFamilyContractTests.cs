using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using PushHost = Sprk.Bff.Api.Tests.Api.FieldMappings.FieldMappingPushAuthorizationContractTests.PushHost;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// <c>POST /api/v1/field-mappings/push</c> — the FR-34 request members (field-mapping-server-write-path-r1 task 040): a
/// preview that writes nothing, the confirmed children the server re-derives as the caller, the Assigned-To rules that need
/// Write on the source, the Filing-only kind, and the sync-mode selection of FR-23.
/// </summary>
/// <remarks>
/// The same REAL mapper and handler as <see cref="FieldMappingPushAuthorizationContractTests"/>, over the same host; what is
/// substituted is the caller-scoped query seam (a scripted Dataverse that answers the family walk and the current values) and
/// the caller-rights probe. No HTTP handler is mocked (ADR-038).
/// </remarks>
public class FieldMappingPushFamilyContractTests
{
    private static readonly Guid MatterId = Guid.Parse("16616616-1111-4000-8000-000000000166");
    private static readonly Guid CallerSystemUserId = Guid.Parse("16616616-2222-4000-8000-000000000166");
    private static readonly Guid ChildA = Guid.Parse("04000000-aaaa-4000-8000-000000000001");
    private static readonly Guid ChildB = Guid.Parse("04000000-bbbb-4000-8000-000000000002");
    private static readonly Guid ChildC = Guid.Parse("04000000-cccc-4000-8000-000000000003");
    private static readonly Guid Stranger = Guid.Parse("04000000-dddd-4000-8000-000000000004");

    private const string Column = "sprk_clientreference";

    /// <summary>An Assigned-To column of sprk_event in the canonical registry.</summary>
    private const string EventAssignedTo = "sprk_assignedattorney1";

    // ───────────────────────── preview ─────────────────────────

    [Fact]
    public async Task Preview_ListsOnlyTheValuesThatWouldChange_ForTheChildrenTheCallerMayWrite_AndWritesNothing()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB]);
        world.Dataverse.Current[ChildA] = "OLD";
        world.Dataverse.Current[ChildB] = "REF-166"; // already the source's value
        // ChildC is readable but not writable by the caller.

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview = true });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["preview"]!.GetValue<bool>().Should().BeTrue();
        json["updatedCount"]!.GetValue<int>().Should().Be(0);
        json["skippedNoWriteCount"]!.GetValue<int>().Should().Be(1, "the caller cannot write ChildC");
        json["truncated"]!.GetValue<bool>().Should().BeFalse();
        json["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
        var change = json["changes"]!.AsArray().Should().ContainSingle().Which!;
        change["recordId"]!.GetValue<string>().Should().Be(ChildA.ToString("D"));
        change["name"]!.GetValue<string>().Should().Be("Event A");
        change["column"]!.GetValue<string>().Should().Be(Column);
        change["from"]!.GetValue<string>().Should().Be("OLD");
        change["to"]!.GetValue<string>().Should().Be("REF-166");
        change["flags"]!.AsArray().Should().BeEmpty("an ordinary column meets no Assigned-To policy");

        world.Host.Writes.Should().BeEmpty("a preview writes nothing");
        world.Host.FieldMappings.Verify(f => f.UpdateExistingRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Preview_WithConfirmedChildIds_IsLimitedToThoseTheServerDerives()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            preview = true,
            childIds = new[] { ChildB, Stranger },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["changes"]!.AsArray().Select(c => c!["recordId"]!.GetValue<string>())
            .Should().Equal([ChildB.ToString("D")], "a child the server did not derive is never previewed");
    }

    [Fact]
    public async Task Preview_ReturnsTheConfirmedIdsItRefused_SoThePersonIsToldTheyWillNotBeUpdated()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            preview = true,
            childIds = new[] { ChildB, Stranger },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonNode.Parse(body)!["refusedChildIds"]!.AsArray().Select(id => id!.GetValue<string>())
            .Should().Equal([Stranger.ToString("D")], "a ticked id the server will not update is reported, not dropped");
        world.Host.Writes.Should().BeEmpty();
    }

    // ───────────────────────── confirmed children ─────────────────────────

    [Fact]
    public async Task Apply_WithConfirmedChildIds_WritesOnlyTheChildrenTheServerDerives_AndRefusesTheRestPerIdWithAFixedString()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA, Stranger },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["updatedCount"]!.GetValue<int>().Should().Be(1);
        json["failedCount"]!.GetValue<int>().Should().Be(1);
        var error = json["errors"]!.AsArray().Should().ContainSingle().Which!;
        error["recordId"]!.GetValue<string>().Should().Be(Stranger.ToString("D"));
        error["error"]!.GetValue<string>().Should().Be(FieldMappingPushFamily.ChildNotInPushMessage);
        world.Host.Writes.Should().ContainSingle().Which.Id.Should().Be(ChildA);
        world.Host.UpdateOnlyWrites.Should().ContainSingle().Which.ImpersonatedAs.Should().Be(CallerSystemUserId);
    }

    [Fact]
    public async Task Apply_WithoutChildIds_KeepsTheOrdinaryLookupDiscovery()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["updatedCount"]!.GetValue<int>().Should().Be(3);
        world.Dataverse.Queries.Should().ContainSingle().Which.Should().Contain("$select=sprk_eventid&$top=501", "the ordinary request asks as it always has");
    }

    // ───────────────────────── edge, Assigned To ─────────────────────────

    [Fact]
    public async Task APreviewToATableNoFilingEdgeJoins_Is409_AndReadsNoChild()
    {
        await using var world = await World.StartAsync(writable: [ChildA], target: "sprk_fileversion", targetSet: "sprk_fileversions");

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_fileversion", preview = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>()
            .Should().Be(FieldMappingEndpoints.ParentLookupMissingReasonCode);
        world.Dataverse.Queries.Should().BeEmpty();
    }

    /// <summary>Task 076c: a readable source but no profile for the pair is a 404 that carries the stable code, so the Family page
    /// can report that one table and still preview the others.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATargetWithNoProfile_Is404WithTheProfileMissingReasonCode_ForAPreviewAndAnApply(bool preview)
    {
        await using var world = await World.StartAsync(writable: [ChildA]);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_noprofile", preview });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingEndpoints.ProfileMissingReasonCode);
        json["title"]!.GetValue<string>().Should().Be("Profile Not Found");
        world.Dataverse.Queries.Should().BeEmpty();
        world.Host.Writes.Should().BeEmpty();
    }

    /// <summary>The source-not-found 404 stays uniform and carries NO reason code: it must not be mistaken for a missing profile
    /// (the page treats a code-less 404 as a whole failure).</summary>
    [Fact]
    public async Task ASourceTheCallerCannotRead_Is404WithNoReasonCode()
    {
        await using var world = await World.StartAsync(writable: [ChildA]);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = Guid.NewGuid(), targetEntity = "sprk_noprofile", preview = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject().ContainsKey("reasonCode")
            .Should().BeFalse("the uniform source-not-found answer has no code, so it cannot be told from a missing profile by its absence of one");
    }

    [Fact]
    public async Task IncludeAssignedTo_WithoutWriteOnTheSource_Is403_AndNothingIsReadOrWritten()
    {
        await using var world = await World.StartAsync(writable: [ChildA], sourceRights: AccessRights.Read);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            includeAssignedTo = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>()
            .Should().Be(FieldMappingEndpoints.AssignedToNeedsSourceWriteReasonCode);
        world.Host.FieldMappings.Verify(f => f.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        world.Dataverse.Queries.Should().BeEmpty();
        world.Host.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// The opt-in is what writes an Assigned-To column: the rule is left at One-time, so nothing but <c>includeAssignedTo</c>
    /// (through the real classifier) can select it. If the opt-in did nothing, nothing would be written.
    /// </summary>
    [Fact]
    public async Task IncludeAssignedTo_WithWriteOnTheSource_WritesTheAssignedToColumn()
    {
        var assigned = PushHost.Rule(Column, 0, EventAssignedTo, 0);
        assigned.SyncMode = SyncMode.OneTime;
        await using var world = await World.StartAsync(
            writable: [ChildA, ChildB], sourceRights: AccessRights.Read | AccessRights.Write, rules: [assigned]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            includeAssignedTo = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        world.Host.Writes.Should().NotBeEmpty().And.OnlyContain(w => w.Payload.ContainsKey(EventAssignedTo));
    }

    // ───────────────────── the one Assigned-To classifier (the bound registry, any table) ─────────────────────

    /// <summary>An event's Assigned-To column (the registry lists sprk_event): not written by an ordinary push, whatever the
    /// rule's sync mode; written when the request opts in.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEventAssignedToRule_IsWrittenOnlyWithIncludeAssignedTo(bool includeAssignedTo)
    {
        var assigned = PushHost.Rule(Column, 0, EventAssignedTo, 0); // Manual Refresh: only the class keeps it out
        var ordinary = PushHost.Rule(Column, 0, Column, 0);
        await using var world = await World.StartAsync(writable: [ChildA, ChildB], rules: [assigned, ordinary]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            includeAssignedTo,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        world.Host.Writes.Should().NotBeEmpty().And.OnlyContain(w => w.Payload.ContainsKey(Column));
        if (includeAssignedTo)
            world.Host.Writes.Should().OnlyContain(w => w.Payload.ContainsKey(EventAssignedTo));
        else
            world.Host.Writes.Should().NotContain(w => w.Payload.ContainsKey(EventAssignedTo), "an ordinary push leaves Assigned To alone");
    }

    /// <summary>A root's Assigned-To column (a project under the matter): the same rule as any other table.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARootAssignedToRule_IsWrittenOnlyWithIncludeAssignedTo(bool includeAssignedTo)
    {
        var assigned = PushHost.Rule(Column, 0, "sprk_assignedattorney1", 0);
        var ordinary = PushHost.Rule(Column, 0, Column, 0);
        await using var world = await World.StartAsync(
            writable: [ChildA, ChildB], target: "sprk_project", targetSet: "sprk_projects", rules: [assigned, ordinary]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_project",
            includeAssignedTo,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        world.Host.Writes.Should().NotBeEmpty().And.OnlyContain(w => w.Payload.ContainsKey(Column));
        if (includeAssignedTo)
            world.Host.Writes.Should().OnlyContain(w => w.Payload.ContainsKey("sprk_assignedattorney1"));
        else
            world.Host.Writes.Should().NotContain(w => w.Payload.ContainsKey("sprk_assignedattorney1"));
    }

    [Fact]
    public async Task WithoutIncludeAssignedTo_TheSourceNeedsReadOnly()
    {
        await using var world = await World.StartAsync(writable: [ChildA], sourceRights: AccessRights.Read);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Write on the source is asked only for the Assigned-To rules");
    }

    // ───────────────────────── FR-23: the rules a push applies ─────────────────────────

    [Fact]
    public async Task ARuleThatIsNotCopyAtManualRefresh_IsCountedAndSkipped_NeverWritten_NeverAnError()
    {
        var oneTime = PushHost.Rule(Column, 0, Column, 0);
        oneTime.SyncMode = SyncMode.OneTime;
        var asDefault = PushHost.Rule("sprk_other", 0, "sprk_other", 0);
        asDefault.MappingType = 1;
        asDefault.DefaultValue = "x";
        await using var world = await World.StartAsync(writable: [ChildA], rules: [oneTime, asDefault]);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["skippedRuleCount"]!.GetValue<int>().Should().Be(2);
        json["updatedCount"]!.GetValue<int>().Should().Be(0);
        json["success"]!.GetValue<bool>().Should().BeTrue();
        world.Host.Writes.Should().BeEmpty();
        world.Dataverse.Queries.Should().BeEmpty("with no rule to apply the push reads no child");
    }

    [Fact]
    public async Task APreviewWithNoRuleToApply_SaysSoWithAnEmptyChangeList()
    {
        var oneTime = PushHost.Rule(Column, 0, Column, 0);
        oneTime.SyncMode = SyncMode.OneTime;
        await using var world = await World.StartAsync(writable: [ChildA], rules: [oneTime]);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview = true });

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["preview"]!.GetValue<bool>().Should().BeTrue();
        json["changes"]!.AsArray().Should().BeEmpty();
        json["skippedRuleCount"]!.GetValue<int>().Should().Be(1);
    }

    /// <summary>Task 076c: an active profile whose rules are all off returns no rules at all. A preview must still answer AS a
    /// preview (the Family page throws on a 200 that is not), and an apply keeps its ordinary answer.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProfileWithNoRules_AnswersAPreviewAsAPreview_AndAnApplyAsBefore(bool preview)
    {
        await using var world = await World.StartAsync(writable: [ChildA], rules: []);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!.AsObject();
        json["success"]!.GetValue<bool>().Should().BeTrue();
        json["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal("Profile has no mapping rules configured.");
        if (preview)
        {
            json["preview"]!.GetValue<bool>().Should().BeTrue();
            json["changes"]!.AsArray().Should().BeEmpty();
            json["skippedNoWriteCount"]!.GetValue<int>().Should().Be(0);
            json["truncated"]!.GetValue<bool>().Should().BeFalse();
        }
        else
        {
            (json["preview"]?.GetValue<bool>() ?? false).Should().BeFalse("an apply keeps today's non-preview answer");
            json["changes"].Should().BeNull();
        }

        world.Dataverse.Queries.Should().BeEmpty();
        world.Host.Writes.Should().BeEmpty();
    }

    // ───────────────────────── the Assigned-To policies on a previewed change ─────────────────────────

    [Fact]
    public async Task APreviewedAssignedToChange_CarriesThePoliciesTheTargetMeets()
    {
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.AddRange([ChildA, ChildB, ChildC]);
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_events");
        var probe = new FamilyProbe(writable: [ChildA, ChildB, ChildC]);
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_assignedname",
            SourceFieldType = "Text",
            TargetField = "sprk_assignedname",
            TargetFieldType = "Text",
            Priority = 1,
            MappingType = "Copy",
        };
        var members = new[] { ChildA, ChildB, ChildC }.Select(id => new RecordFamilyMember(
            "sprk_event", id, $"Event {id.ToString()[..4]}", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1)).ToList();
        dataverse.AttributeNames.Add("sprk_assignedname");

        var response = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, [rule], new Dictionary<string, object?> { ["sprk_assignedname"] = "Alex" },
                new Dictionary<FieldMappingRuleDto, LookupBindPlan>(), 0, (table, column) => column == "sprk_assignedname",
                Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild()),
            "token", CallerSystemUserId, probe, dataverse, entities.Object,
            (_, ids, _) => Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(new Dictionary<Guid, RootRecordFlags>
            {
                [ChildA] = new(IsSecure: true, IsRestricted: false),
                [ChildB] = new(IsSecure: false, IsRestricted: true, IsLimited: true),
                [ChildC] = new(IsSecure: false, IsRestricted: false),
            }),
            CancellationToken.None);

        var byChild = response.Changes!.ToDictionary(c => c.RecordId, c => c.Flags);
        byChild[ChildA].Should().Equal("secure");
        byChild[ChildB].Should().Equal("restricted", "limited");
        byChild[ChildC].Should().BeEmpty();
    }

    [Fact]
    public void AnUnreadableRecord_IsTreatedAsMeetingEveryPolicy()
    {
        FieldMappingPushFamily.FlagNames(RootRecordFlags.Unreadable).Should().Equal("secure", "restricted", "limited");
        FieldMappingPushFamily.FlagNames(new RootRecordFlags(false, false)).Should().BeEmpty();
    }

    // ───────────────────────── task 022 fix round 4: the mapping-source rule, through the route ─────────────────────────

    /// <summary>A secure source, children owned by a user (not its isolation owner): the route asks the rule on the apply.</summary>
    [Fact]
    public async Task Apply_FromASecureSource_DoesNotWriteAChildNotOwnedByItsIsolationOwner_AndGivesTheCode()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);
        world.Host.ArrangeSourceGateRead("sprk_matter", secureFlag: true);
        ArrangeChildOwner(world, new Microsoft.Xrm.Sdk.EntityReference("systemuser", Guid.NewGuid()));

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["updatedCount"]!.GetValue<int>().Should().Be(0);
        json["failedCount"]!.GetValue<int>().Should().Be(1);
        var error = json["errors"]!.AsArray().Should().ContainSingle().Which!;
        error["recordId"]!.GetValue<string>().Should().Be(ChildA.ToString("D"));
        error["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        error["error"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.NotIsolatedMessage);
        world.Host.Writes.Should().BeEmpty("nothing is written to a child the rule refuses");
    }

    /// <summary>The same source and children on a preview: no value is listed for the child; it comes back with its code.</summary>
    [Fact]
    public async Task Preview_FromASecureSource_ListsNoValueForAChildNotOwnedByItsIsolationOwner_AndReturnsItWithTheCode()
    {
        await using var world = await World.StartAsync(writable: [ChildA]);
        world.Dataverse.Current[ChildA] = "OLD";
        world.Host.ArrangeSourceGateRead("sprk_matter", secureFlag: true);
        ArrangeChildOwner(world, new Microsoft.Xrm.Sdk.EntityReference("systemuser", Guid.NewGuid()));

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview = true });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["preview"]!.GetValue<bool>().Should().BeTrue();
        json["changes"]!.AsArray().Should().BeEmpty("the apply would write nothing to the child");
        var skipped = json["errors"]!.AsArray().Should().ContainSingle().Which!;
        skipped["recordId"]!.GetValue<string>().Should().Be(ChildA.ToString("D"));
        skipped["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        world.Host.Writes.Should().BeEmpty();
    }

    /// <summary>An ordinary source through the route: unchanged, the child is written and no child owner is read.</summary>
    [Fact]
    public async Task Apply_FromAnOrdinarySource_IsUnchanged_AndReadsNoChildOwner()
    {
        await using var world = await World.StartAsync(writable: [ChildA]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonNode.Parse(body)!["updatedCount"]!.GetValue<int>().Should().Be(1);
        world.Host.Writes.Should().ContainSingle().Which.Id.Should().Be(ChildA);
        world.Host.Entities.Verify(e => e.RetrieveAsync(
            "sprk_event", It.IsAny<Guid>(), It.Is<string[]>(c => c.Length == 1 && c[0] == "ownerid"), It.IsAny<CancellationToken>()),
            Times.Never, "a source that is not secure is allowed for every child");
    }

    /// <summary>
    /// The preview over a secure source owned by the Secure Record owner team: the child that team owns is listed with its
    /// value; the child an ordinary team owns is not, and comes back with the code and the fixed text.
    /// </summary>
    [Fact]
    public async Task Preview_FromASecureSource_ListsTheChildItsIsolationOwnerOwns_AndReturnsTheOtherWithTheCode()
    {
        var secureTeam = Guid.NewGuid();
        var ordinaryTeam = Guid.NewGuid();
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.AddRange([ChildA, ChildB]);
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_events");
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_matter", MatterId)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_issecure"] = true,
            });
        entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Microsoft.Xrm.Sdk.Entity(table, id)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", id == ChildB ? secureTeam : ordinaryTeam),
            });
        var gate = new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", MatterId, _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((secureTeam, null)));
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = Column,
            SourceFieldType = "Text",
            TargetField = Column,
            TargetFieldType = "Text",
            Priority = 1,
            MappingType = "Copy",
        };
        var members = new[] { ChildA, ChildB }.Select(id => new RecordFamilyMember(
            "sprk_event", id, $"Event {id.ToString()[..4]}", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1)).ToList();

        var response = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, [rule], new Dictionary<string, object?> { [Column] = "SECURE-VALUE" },
                new Dictionary<FieldMappingRuleDto, LookupBindPlan>(), 0, (_, _) => false, SourceGate: gate),
            "token", CallerSystemUserId, new FamilyProbe(writable: [ChildA, ChildB]), dataverse, entities.Object, null,
            CancellationToken.None);

        response.Changes!.Select(c => c.RecordId).Should().Equal([ChildB], "the child the isolation owner owns is listed");
        var skipped = response.Errors.Should().ContainSingle().Which;
        skipped.RecordId.Should().Be(ChildA);
        skipped.ReasonCode.Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        skipped.Error.Should().Be(FieldMappingPushSourceGate.NotIsolatedMessage);
    }

    /// <summary>
    /// UAC-r2 (a) on the preview: a secure source's push that would change a child's filing lists no value for that child and
    /// returns it with the re-file code; a child already filed under the pushed record is listed (both are owned by the
    /// source's isolation owner).
    /// </summary>
    [Fact]
    public async Task Preview_FromASecureSource_ReturnsAChildItWouldRefileWithTheRefileCode_AndListsTheOneAlreadyFiledThere()
    {
        var secureTeam = Guid.NewGuid();
        var otherMatter = Guid.NewGuid();
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.AddRange([ChildA, ChildB]);
        dataverse.AttributeNames.Add("sprk_regardingmatter");
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_events");
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_matter", MatterId)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_issecure"] = true,
            });
        entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Microsoft.Xrm.Sdk.Entity(table, id)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_regardingmatter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", id == ChildB ? otherMatter : MatterId),
            });
        var gate = new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", MatterId, _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((secureTeam, null)));
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_lookupsource",
            SourceFieldType = "Lookup",
            TargetField = "sprk_regardingmatter",
            TargetFieldType = "Lookup",
            Priority = 1,
            MappingType = "Copy",
        };
        var members = new[] { ChildA, ChildB }.Select(id => new RecordFamilyMember(
            "sprk_event", id, $"Event {id.ToString()[..4]}", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1)).ToList();

        var response = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, [rule], new Dictionary<string, object?> { ["sprk_lookupsource"] = otherMatter },
                new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
                {
                    [rule] = new("sprk_RegardingMatter@odata.bind", $"/sprk_matters({otherMatter:D})", null),
                },
                0, (_, _) => false, SourceGate: gate),
            "token", CallerSystemUserId, new FamilyProbe(writable: [ChildA, ChildB]), dataverse, entities.Object, null,
            CancellationToken.None);

        response.Changes!.Select(c => c.RecordId).Should().Equal([ChildB], "the child already filed under the pushed record is listed");
        var skipped = response.Errors.Should().ContainSingle().Which;
        skipped.RecordId.Should().Be(ChildA);
        skipped.ReasonCode.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
        skipped.Error.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedMessage);
    }

    /// <summary>
    /// The same on the preview for a lookup the pair repeats (a party lookup, one of the pair's agreement columns): a secure
    /// source's push that would move it is returned with the re-file code; a child already holding the pushed account is listed.
    /// </summary>
    [Fact]
    public async Task Preview_FromASecureSource_ReturnsAChildWhosePairedPartyItWouldMove_WithTheRefileCode()
    {
        var secureTeam = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.AddRange([ChildA, ChildB]);
        dataverse.AttributeNames.Add("sprk_regardingaccount");
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_events");
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_matter", MatterId)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_issecure"] = true,
            });
        entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
            {
                var account = id == ChildB ? accountB : accountA;
                return new Microsoft.Xrm.Sdk.Entity(table, id)
                {
                    ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                    ["sprk_regardingaccount"] = new Microsoft.Xrm.Sdk.EntityReference("account", account),
                    [Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn] = account.ToString("D"),
                };
            });
        var gate = new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", MatterId, _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((secureTeam, null)));
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_client",
            SourceFieldType = "Lookup",
            TargetField = "sprk_regardingaccount",
            TargetFieldType = "Lookup",
            Priority = 1,
            MappingType = "Copy",
        };
        var members = new[] { ChildA, ChildB }.Select(id => new RecordFamilyMember(
            "sprk_event", id, $"Event {id.ToString()[..4]}", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1)).ToList();

        var response = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, [rule], new Dictionary<string, object?> { ["sprk_client"] = accountB },
                new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
                {
                    [rule] = new("sprk_RegardingAccount@odata.bind", $"/accounts({accountB:D})", null),
                },
                0, (_, _) => false, SourceGate: gate),
            "token", CallerSystemUserId, new FamilyProbe(writable: [ChildA, ChildB]), dataverse, entities.Object, null,
            CancellationToken.None);

        response.Changes!.Select(c => c.RecordId).Should().Equal([ChildB]);
        var skipped = response.Errors.Should().ContainSingle().Which;
        skipped.RecordId.Should().Be(ChildA);
        skipped.ReasonCode.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
    }

    /// <summary>
    /// Task 022 fix round 5 (review F1): a lookup rule whose planned bind writes a DIFFERENT filing lookup than the one it names
    /// is refused per rule on a reparentable child (#2091 r4), on the preview as on the apply. So the preview asks the source
    /// gate with the payload the apply writes — the other rules' values only — and lists the child the apply writes, instead
    /// of hiding it with the re-file code. The apply over the same child, rules and binds writes the same values (parity).
    /// </summary>
    [Fact]
    public async Task Preview_FromASecureSource_ARuleWhoseBindIsAnotherFilingLookup_IsRefusedPerRule_AndTheChildIsListedAsTheApplyWritesIt()
    {
        var secureTeam = Guid.NewGuid();
        var otherMatter = Guid.NewGuid();
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.Add(ChildA);
        dataverse.Current[ChildA] = "OLD-VALUE";
        var entities = SecureSourceWithChildFiledUnderIt(secureTeam);
        var gate = new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", MatterId, _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((secureTeam, null)));
        var (rules, sourceValues, binds) = ATextRuleAndALookupRuleBoundThroughTheMatterLookup(otherMatter);
        var members = new List<RecordFamilyMember>
        {
            new("sprk_event", ChildA, "Event A", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1),
        };

        var preview = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, rules, sourceValues, binds, 0, (_, _) => false, SourceGate: gate),
            "token", CallerSystemUserId, new FamilyProbe(writable: [ChildA]), dataverse, entities.Object, null,
            CancellationToken.None);

        preview.Errors.Should().BeEmpty("the refused rule is left out, so nothing in the payload changes the child's filing");
        var listed = preview.Changes.Should().ContainSingle().Which;
        listed.RecordId.Should().Be(ChildA);
        listed.Column.Should().Be(Column);
        listed.To.Should().Be("SECURE-VALUE");

        var writes = new List<Dictionary<string, object?>>();
        var writer = new Mock<IFieldMappingDataverseService>();
        writer.Setup(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, Dictionary<string, object?> payload, Guid? _, CancellationToken _) =>
                writes.Add(new Dictionary<string, object?>(payload)))
            .Returns(Task.CompletedTask);

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            gate,
            writer.Object,
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper,
            rules,
            sourceValues,
            "sprk_event",
            [ChildA],
            CallerSystemUserId,
            NullLogger.Instance,
            CancellationToken.None,
            lookupBinds: binds,
            appOnlyEntities: entities.Object);

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0), "the apply writes the child the preview listed");
        writes.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new Dictionary<string, object?> { [Column] = "SECURE-VALUE" }, "the same values the preview listed, and no filing bind");
    }

    /// <summary>
    /// Must not fire: a lookup rule whose bind writes the column it names keeps its bind on the preview (the confinement leaves
    /// it as it is), so a secure source's push that would re-file the child still returns it with the re-file code.
    /// </summary>
    [Fact]
    public async Task Preview_FromASecureSource_ALookupRuleWritingTheColumnItNames_KeepsItsBind_AndARefileIsStillReturnedWithTheCode()
    {
        var secureTeam = Guid.NewGuid();
        var otherMatter = Guid.NewGuid();
        var dataverse = new ScriptedDataverse { Inner = Mock.Of<IImpersonatedCommunicationQuery>() };
        dataverse.Children.Add(ChildA);
        dataverse.Current[ChildA] = "OLD-VALUE";
        dataverse.AttributeNames.Add("sprk_regardingmatter");
        var entities = SecureSourceWithChildFiledUnderIt(secureTeam);
        var gate = new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", MatterId, _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((secureTeam, null)));
        var (rules, sourceValues, binds) = ATextRuleAndALookupRuleBoundThroughTheMatterLookup(otherMatter, lookupTarget: "sprk_regardingmatter");
        var members = new List<RecordFamilyMember>
        {
            new("sprk_event", ChildA, "Event A", null, null, null, RecordFamilyEdges.FamilyKind.Filing, null, null, 1),
        };

        var preview = await FieldMappingPushFamily.PreviewAsync(
            new FieldMappingPushFamily.PreviewInput(
                "sprk_event", members, false, rules, sourceValues, binds, 0, (_, _) => false, SourceGate: gate),
            "token", CallerSystemUserId, new FamilyProbe(writable: [ChildA]), dataverse, entities.Object, null,
            CancellationToken.None);

        preview.Changes.Should().BeEmpty();
        var skipped = preview.Errors.Should().ContainSingle().Which;
        skipped.RecordId.Should().Be(ChildA);
        skipped.ReasonCode.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
    }

    /// <summary>A secure matter (flagged, owned by the Secure team) and an event the Secure team owns, filed under that matter.</summary>
    private static Mock<IGenericEntityService> SecureSourceWithChildFiledUnderIt(Guid secureTeam)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_event", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_events");
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_matter", MatterId)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_issecure"] = true,
            });
        entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Microsoft.Xrm.Sdk.Entity(table, id)
            {
                ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", secureTeam),
                ["sprk_regardingmatter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", MatterId),
            });
        return entities;
    }

    /// <summary>
    /// A text rule on <see cref="Column"/> and a lookup rule on <paramref name="lookupTarget"/> whose bind was planned through the
    /// event's matter lookup (<c>sprk_RegardingMatter</c>), naming another matter.
    /// </summary>
    private static (FieldMappingRuleDto[] Rules, Dictionary<string, object?> SourceValues, IReadOnlyDictionary<FieldMappingRuleDto, LookupBindPlan> Binds)
        ATextRuleAndALookupRuleBoundThroughTheMatterLookup(Guid otherMatter, string lookupTarget = "sprk_regardingproject")
    {
        var text = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = Column,
            SourceFieldType = "Text",
            TargetField = Column,
            TargetFieldType = "Text",
            Priority = 1,
            MappingType = "Copy",
        };
        var lookup = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_lookupsource",
            SourceFieldType = "Lookup",
            TargetField = lookupTarget,
            TargetFieldType = "Lookup",
            Priority = 2,
            MappingType = "Copy",
        };
        var binds = new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
        {
            [lookup] = new("sprk_RegardingMatter@odata.bind", $"/sprk_matters({otherMatter:D})", null),
        };
        return ([text, lookup], new Dictionary<string, object?> { [Column] = "SECURE-VALUE", ["sprk_lookupsource"] = otherMatter }, binds);
    }

    /// <summary>Each child's owner as the app-only read returns it (task 022 fix round 4).</summary>
    private static void ArrangeChildOwner(World world, Microsoft.Xrm.Sdk.EntityReference owner) =>
        world.Host.Entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                new Microsoft.Xrm.Sdk.Entity(table, id) { ["ownerid"] = owner });

    // ───────────────────────── fixtures ─────────────────────────

    /// <summary>The host, a scripted caller-scoped Dataverse and a probe that answers the rights the test sets.</summary>
    private sealed class World : IAsyncDisposable
    {
        public required PushHost Host { get; init; }

        public required ScriptedDataverse Dataverse { get; init; }

        public static async Task<World> StartAsync(
            Guid[] writable, AccessRights sourceRights = AccessRights.Read | AccessRights.Write,
            string target = "sprk_event", string targetSet = "sprk_events", FieldMappingRuleEntity[]? rules = null)
        {
            var dataverse = new ScriptedDataverse();
            dataverse.Children.AddRange([ChildA, ChildB, ChildC]);
            var probe = new FamilyProbe(writable) { SourceRights = sourceRights };
            var reader = new RecordFamilyReader(dataverse, new EphemeralDataProtectionProvider(), NullLogger<RecordFamilyReader>.Instance);
            var host = await PushHost.StartAsync(configure: services =>
            {
                services.AddSingleton<IImpersonatedCommunicationQuery>(dataverse);
                services.AddSingleton<CallerRecordAccessProbe>(probe);
                services.AddSingleton(reader);
            });
            dataverse.Inner = host.Impersonated.Object;
            host.ArrangeHappyPathData(targetEntity: target, targetSet: targetSet, rules: rules);
            return new World { Host = host, Dataverse = dataverse };
        }

        public async Task<HttpResponseMessage> PostAsync(object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/field-mappings/push") { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(PushAuthzTestAuthHandler.CallerHeader, "present");
            return await Host.SendAsync(request);
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    /// <summary>The caller's rights, as the test sets them: the source's, and which children they may write.</summary>
    private sealed class FamilyProbe : CallerRecordAccessProbe
    {
        private readonly HashSet<Guid> _writable;

        public FamilyProbe(IEnumerable<Guid> writable)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _writable = [.. writable];

        public AccessRights SourceRights { get; set; } = AccessRights.Read | AccessRights.Write;

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && recordId == MatterId ? SourceRights : AccessRights.None);

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
            => Task.FromResult<Guid?>(string.IsNullOrEmpty(callerBearerToken) ? null : CallerSystemUserId);

        public override Task<IReadOnlyList<AccessRights>> GetCallerRightsForRecordsAsync(
            string? callerBearerToken, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccessRights>>(
                targets.Select(t => _writable.Contains(t.RecordId) ? AccessRights.Read | AccessRights.Write : AccessRights.Read).ToList());
    }

    /// <summary>
    /// A caller-scoped Dataverse for the event table: the family walk's rows (each child filed under the matter), the target's
    /// attribute catalogue and each child's current value. Anything else goes to the host's own scripted seam.
    /// </summary>
    private sealed class ScriptedDataverse : IImpersonatedCommunicationQuery
    {
        public IImpersonatedCommunicationQuery Inner { get; set; } = null!;

        public List<Guid> Children { get; } = [];

        public Dictionary<Guid, string?> Current { get; } = [];

        public HashSet<string> AttributeNames { get; } = [Column];

        /// <summary>Every query that reached the event table (the walk's, the ordinary discovery's, the preview's).</summary>
        public List<string?> Queries { get; } = [];

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            if (entitySetName == "EntityDefinitions(LogicalName='sprk_event')/Attributes")
            {
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(AttributeNames
                    .Select(name => new Dictionary<string, JsonElement>
                    {
                        ["LogicalName"] = JsonSerializer.SerializeToElement(name),
                        ["AttributeType"] = JsonSerializer.SerializeToElement("String"),
                        ["IsValidODataAttribute"] = JsonSerializer.SerializeToElement(true),
                    }).ToList());
            }

            if (entitySetName != "sprk_events")
                return Inner.QueryAsync(entitySetName, odataQuery, callerSystemUserId, ct);

            Queries.Add(odataQuery);
            var nameColumn = RecordFamilyReader.NameColumnOf("sprk_event");
            var rows = Children.Select((id, i) =>
            {
                var row = new Dictionary<string, JsonElement>
                {
                    ["sprk_eventid"] = JsonSerializer.SerializeToElement(id.ToString("D")),
                    ["_sprk_regardingmatter_value"] = JsonSerializer.SerializeToElement(MatterId.ToString("D")),
                    [Column] = JsonSerializer.SerializeToElement(Current.GetValueOrDefault(id)),
                };
                if (nameColumn is not null)
                    row[nameColumn] = JsonSerializer.SerializeToElement($"Event {(char)('A' + i)}");
                return row;
            }).ToList();
            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(rows);
        }
    }
}
