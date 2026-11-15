using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// field-mapping-server-write-path-r1 task 022c (unified-access-control-r2, 2026-11-12): an access column is never a mapping
/// target, on any table and from any source (<see cref="RecordCreationService.AccessColumnsNeverMapped"/>). The access columns
/// change only through the access endpoints and the stamp / reconcile path. A push rule aimed at one is skipped, with the code
/// <see cref="FieldMappingEndpoints.AccessColumnNotMappedReasonCode"/>, and the rest of the push applies; the preview does not
/// offer it (<see cref="FieldMappingEndpoints.PushMayWrite"/>). Must not fire: an ordinary column on the same tables, and the
/// regarding pair (a push still re-files, FR-23).
/// </summary>
public class AccessColumnPushTargetTests
{
    private static readonly Guid Child = Guid.Parse("022c0000-0000-0000-0000-0000000000c1");
    private static readonly Guid Caller = Guid.Parse("022c0000-0000-0000-0000-0000000000ca");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    public static TheoryData<string, string> EveryAccessColumnOnEveryTable()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in new[] { "sprk_event", "sprk_todo", "sprk_communication", "sprk_matter", "sprk_project", "sprk_invoice" })
        {
            foreach (var column in RecordCreationService.AccessColumnsNeverMapped)
                data.Add(table, column);
        }

        return data;
    }

    // ── must fire ──

    [Theory]
    [InlineData("sprk_event", "sprk_accesspermission")]
    [InlineData("sprk_todo", "sprk_accesspermission")]
    [InlineData("sprk_communication", "sprk_accesspermission")]
    [InlineData("sprk_event", " SPRK_AccessPermission ")]
    public async Task ACopyRule_TargetingTheAccessPermission_IsSkippedWithItsCode_AndTheOtherColumnsArePushed(string table, string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            table,
            Rule("sprk_name", "sprk_description", "Text"),
            Rule("sprk_accesspermission", target, "OptionSet"));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            table, Child,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Should().Match<FieldMappingResultDto>(r =>
                r.Status == FieldMappingStatus.Skipped
                && r.ErrorMessage == FieldMappingEndpoints.AccessColumnNotMappedMessage
                && r.ErrorMessage.Contains("(" + FieldMappingEndpoints.AccessColumnNotMappedReasonCode + ")"));
        results.Should().ContainSingle(r => r.TargetField == "sprk_description")
            .Which.Status.Should().Be(FieldMappingStatus.Mapped);
    }

    [Theory]
    [MemberData(nameof(EveryAccessColumnOnEveryTable))]
    public async Task EveryAccessColumn_IsSkippedByThePush_AndNothingIsWritten(string table, string column)
    {
        var (updated, failed, skipped, _, results) = await Push(table, Rule(column, column, "Text"));

        (updated, failed, skipped).Should().Be((0, 0, 1));
        _writes.Invocations.Should().BeEmpty($"{column} is never a mapping target");
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Skipped);
    }

    /// <summary>The pin: the push's refusal set (shared by the apply and the preview) holds every column of the one list.</summary>
    [Theory]
    [MemberData(nameof(EveryAccessColumnOnEveryTable))]
    public void EveryAccessColumn_IsRefusedByTheColumnsAPushMayWrite(string table, string column)
    {
        FieldMappingEndpoints.PushMayWrite(table, column).Should().BeFalse();
        FieldMappingEndpoints.PushMayWrite(table, column.ToUpperInvariant()).Should().BeFalse();
    }

    [Fact]
    public void TheList_IsExactlyTheAccessColumns()
        => RecordCreationService.AccessColumnsNeverMapped.Should().BeEquivalentTo(new[]
        {
            "sprk_issecure", "sprk_accesspermission", "sprk_accessinheritance", "sprk_securitybu", "sprk_containerid",
        });

    [Fact]
    public void TheCode_IsStable()
        => FieldMappingEndpoints.AccessColumnNotMappedReasonCode.Should().Be("field_mapping.push.access_column_not_mapped");

    /// <summary>Every access column carries the one code, a document's included (the access-column check is asked before the
    /// document's own refusal).</summary>
    [Theory]
    [InlineData("sprk_accesspermission")]
    [InlineData("sprk_accessinheritance")]
    public async Task ADocumentsAccessColumn_CarriesTheOneAccessColumnCode(string column)
    {
        var (_, _, _, _, results) = await Push("sprk_document", Rule(column, column, "Text"));

        results.Should().ContainSingle().Which.ErrorMessage.Should().Be(FieldMappingEndpoints.AccessColumnNotMappedMessage);
    }

    /// <summary>
    /// The payload safety net: a bind plan that names an access column's key under a rule whose target is an alias reaches the
    /// payload past the per-rule check; it is removed before the write, and the other column is still written.
    /// </summary>
    [Fact]
    public async Task ABindKeyForAnAccessColumn_UnderAnAliasedRule_IsRemovedBeforeTheWrite()
    {
        var record = Guid.Parse("022c0000-0000-0000-0000-0000000000a1");
        var aliased = new FieldMappingRuleDto
        {
            SourceField = "sprk_inheritancesource",
            TargetField = "sprk_inheritancealias",
            SourceFieldType = "Lookup",
            TargetFieldType = "Lookup",
        };
        var binds = new Dictionary<FieldMappingRuleDto, LookupBindPlan>
        {
            [aliased] = new LookupBindPlan("sprk_AccessInheritance@odata.bind", $"/sprk_accessinheritances({record:D})", null),
        };

        await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            [Rule("sprk_name", "sprk_description", "Text"), aliased],
            new Dictionary<string, object?>
            {
                ["sprk_name"] = "Matter 022c",
                ["sprk_inheritancesource"] = new SourceLookupValue(record, "sprk_accessinheritance", "Inherit"),
            },
            "sprk_matter",
            new[] { Child },
            Caller,
            NullLogger.Instance,
            CancellationToken.None,
            lookupBinds: binds);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_matter", Child,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ThePayloadSafetyNet_RemovesEveryKeyForm_AndKeepsTheOtherColumns()
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_AccessInheritance@odata.bind"] = "/sprk_accessinheritances(00000000-0000-0000-0000-000000000001)",
            ["_sprk_accesspermission_value"] = 1,
            ["SPRK_IsSecure"] = true,
            ["sprk_description"] = "kept",
            [SecureRootInheritance.PairIdColumn] = "kept",
        };

        FieldMappingEndpoints.DropAccessColumns("sprk_matter", Child, payload, NullLogger.Instance).Should().Be(3);

        payload.Keys.Should().BeEquivalentTo(new[] { "sprk_description", SecureRootInheritance.PairIdColumn });
    }

    // ── must not fire ──

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_communication")]
    public async Task AnOrdinaryColumn_OnTheSameTables_IsPushed(string table)
    {
        var (updated, failed, _, _, results) = await Push(table, Rule("sprk_name", "sprk_description", "Text"));

        (updated, failed).Should().Be((1, 0));
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Mapped);
        FieldMappingEndpoints.PushMayWrite(table, "sprk_description").Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_communication")]
    public void TheRegardingPair_IsNotAnAccessColumn_AndAPushStillRefilesThroughIt(string table)
    {
        foreach (var column in new[] { SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn })
        {
            FieldMappingEndpoints.IsAccessColumnNeverMapped(column).Should().BeFalse();
            FieldMappingEndpoints.PushMayWrite(table, column).Should().BeTrue("a push still re-files (FR-23)");
        }
    }

    private static FieldMappingRuleDto Rule(string source, string target, string type) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = type, TargetFieldType = type };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?>
            {
                ["sprk_name"] = "Matter 022c",
                ["sprk_accesspermission"] = InheritedAccessPermission.Restricted,
                ["sprk_issecure"] = true,
                ["sprk_accessinheritance"] = "inherit",
                ["sprk_securitybu"] = "bu",
                ["sprk_containerid"] = "container",
            },
            table,
            new[] { Child },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);
}
