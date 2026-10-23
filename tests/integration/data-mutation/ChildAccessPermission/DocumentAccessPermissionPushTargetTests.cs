using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 189, security pass 2: the field-mapping push never writes a document's
/// <c>sprk_accesspermission</c> or its own-narrowing record <c>sprk_accessinheritance</c>. Its rules are a configuration
/// applied to every child the push reaches, so one push from a Standard matter would otherwise clear the own narrowing of
/// each child document (D7: a child narrows, never widens). Such a rule is skipped with its reason
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>), and the rest of the push applies. Must not fire: the same
/// column on a to-do or an event, and the document's other columns.
/// </summary>
public class DocumentAccessPermissionPushTargetTests
{
    private static readonly Guid Child = Guid.Parse("18900000-0000-0000-0000-0000000000c1");
    private static readonly Guid Caller = Guid.Parse("18900000-0000-0000-0000-0000000000ca");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    [Theory]
    [InlineData("sprk_accesspermission")]
    [InlineData("sprk_accessinheritance")]
    [InlineData(" SPRK_AccessPermission ")]
    public async Task APushRule_TargetingADocumentsAccessColumn_IsSkipped_AndTheOtherColumnsArePushed(string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            "sprk_document",
            Rule("sprk_name", "sprk_documentdescription", "Text"),
            Rule("sprk_accesspermission", target, "OptionSet"));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Child,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_documentdescription")),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Should().Match<FieldMappingResultDto>(r =>
                r.Status == FieldMappingStatus.Skipped && r.ErrorMessage!.Contains("set by Spaarke"));
        results.Should().ContainSingle(r => r.TargetField == "sprk_documentdescription")
            .Which.Status.Should().Be(FieldMappingStatus.Mapped);
    }

    [Fact]
    public async Task APushOfOnlyADocumentsAccessPermission_WritesNothing_AndCountsTheChildSkipped()
    {
        var (updated, failed, skipped, _, results) = await Push(
            "sprk_document", Rule("sprk_accesspermission", "sprk_accesspermission", "OptionSet"));

        (updated, failed, skipped).Should().Be((0, 0, 1));
        _writes.Invocations.Should().BeEmpty("the document's Access Permission is set by Spaarke");
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Skipped);
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    public async Task APushRule_TargetingTheSameColumnOnAToDoOrAnEvent_IsPushed(string table)
    {
        var (updated, failed, _, _, results) = await Push(
            table, Rule("sprk_accesspermission", "sprk_accesspermission", "OptionSet"));

        (updated, failed).Should().Be((1, 0));
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Child, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_accesspermission")),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Mapped);
    }

    private static FieldMappingRuleDto Rule(string source, string target, string type) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = type, TargetFieldType = type };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?>
            {
                ["sprk_name"] = "Matter 189",
                ["sprk_accesspermission"] = InheritedAccessPermission.Standard,
            },
            table,
            new[] { Child },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);
}
