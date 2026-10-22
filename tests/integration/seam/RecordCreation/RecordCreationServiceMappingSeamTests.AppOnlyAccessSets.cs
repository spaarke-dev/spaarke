using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// #2253 fix round 3 — create-time field mapping asks <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> as an
/// app-only writer (<see cref="InheritedAccessPermission.WriteIdentity.AppOnly"/>, passed explicitly), so the sets #2253 added
/// reach it too: a rule aimed at a secure root's container or access columns (<see cref="InheritedAccessPermission.SecureRootColumns"/>)
/// or a communication's internal-only flag (<see cref="InheritedAccessPermission.AccessColumns"/>) is dropped with the
/// refusal's own code (must fire). The D7 exemption stays exactly a document's Access Permission (must not fire).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    public static TheoryData<string> SecureRootColumnsOnAMatter() => new()
    {
        RecordContainerResolver.ContainerColumn,
        SecurableEntityRegistry.SecureFlagAttribute,
        InheritedAccessPermission.Column,
    };

    [Theory]
    [MemberData(nameof(SecureRootColumnsOnAMatter))]
    public async Task CreateAsync_AMatterRuleTargetingASecureRootColumn_IsDroppedWithTheSecureRootCode_AndAnOrdinaryRuleIsFilled(
        string column)
    {
        DeclareCreatableColumns("sprk_matter");
        _user.Columns["sprk_matter"][RecordContainerResolver.ContainerColumn] = ("String", true);
        Profile("sprk_project", "sprk_matter",
            Rule(null, column, Text, Default, "100000002"),
            Rule("sprk_projectdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(RootCreate("sprk_matter"), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => k.StartsWith(column, StringComparison.OrdinalIgnoreCase),
            $"{column} is the BFF's own on a secure root (must fire)");
        StringOf(fields, "sprk_description").Should().Be("From the project", "an ordinary column is still mapped (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Column == column
                && w.Detail.Contains(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode));
    }

    [Fact]
    public async Task CreateAsync_ACommunicationRuleTargetingItsInternalOnlyFlag_IsDroppedWithTheAccessColumnCode()
    {
        var column = CommunicationInternalOnlyFloor.InternalOnlyField;
        _user.Columns["sprk_communication"] = new()
        {
            [column] = ("Boolean", true),
            ["sprk_subject"] = ("String", true),
            ["sprk_body"] = ("Memo", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
        };
        Profile("sprk_matter", "sprk_communication",
            Rule(null, column, CreateTimeFieldMapping.FieldTypeBoolean, Default, "false"),
            Rule("sprk_matterdescription", "sprk_body", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_communication", new Dictionary<string, object?>
                {
                    ["sprk_subject"] = "Status",
                    ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                },
                mapping: true, ("sprk_matter", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(column, "a communication's internal-only flag is the server's (must fire)");
        StringOf(fields, "sprk_body").Should().Be("From the matter", "an ordinary column is still mapped (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Column == column
                && w.Detail.Contains(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode));
    }

    [Fact]
    public async Task CreateAsync_ADocumentsAccessPermissionRule_IsFilledWithNoWarning_TheExemptionKeepsItsShape()
    {
        Profile("sprk_matter", "sprk_document", Rule(null, InheritedAccessPermission.Column, OptionSet, Default, "100000002"));

        var outcome = await Service().CreateAsync(DocumentUnderMatter(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ((JsonElement)fields[InheritedAccessPermission.Column]!).GetInt32().Should().Be(100000002,
            "D7: a document's Access Permission stays fill-only at create (must not fire)");
        outcome.MappingWarnings.Should().BeEmpty();
    }
}
