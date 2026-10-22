using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// #2220 (field-mapping-server-write-path-r1 DI-005) — through the unified <see cref="RecordCreationService"/>, a
/// field-mapping rule never writes a secure root's flag or access columns: <c>sprk_issecure</c>,
/// <c>sprk_accesspermission</c>, <c>sprk_accessinheritance</c> and <c>sprk_securitybu</c>. A rule targeting one is skipped
/// with <see cref="RecordCreationWarning.MappingRuleSkippedCode"/> and the column is not in the create. An ordinary column
/// of the same profile is still filled.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly (string Column, int FieldType, string Value)[] SecureRootAccessColumnRules =
    [
        (SecurableEntityRegistry.SecureFlagAttribute, CreateTimeFieldMapping.FieldTypeBoolean, "true"),
        (InheritedAccessPermission.Column, OptionSet, "100000002"),
        (AccessInheritance.Column, Memo, "{\"parents\":[]}"),
        (SecurableEntityRegistry.SecurityBusinessUnitAttribute, Lookup, "a0140000-0000-4000-8000-0000000000bb"),
    ];

    public static TheoryData<string, string> EverySecureRootAndAccessColumn()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in new[] { "sprk_matter", "sprk_project" })
            foreach (var (column, _, _) in SecureRootAccessColumnRules)
                data.Add(table, column);
        return data;
    }

    /// <summary>A root create asking for field mapping from a named source record (matter from a project, project from a matter).</summary>
    private RecordCreationRequest RootCreate(string table) => table == "sprk_matter"
        ? Request("sprk_matter", new Dictionary<string, object?> { ["sprk_mattername"] = "Smith v Jones" },
            mapping: true, ("sprk_project", SourceProject))
        : Request("sprk_project", new Dictionary<string, object?> { ["sprk_projectname"] = "Phase 1" },
            mapping: true, ("sprk_matter", OrdinaryMatter));

    /// <summary>
    /// The target table's metadata declares the four columns valid for create, so a rule that reached the write would be
    /// written: the only thing that keeps one out is the protected-column skip (not a "no such column" drop).
    /// </summary>
    private void DeclareCreatableColumns(string table)
    {
        var columns = _user.Columns.TryGetValue(table, out var declared) ? declared : _user.Columns[table] = new();
        columns["sprk_description"] = ("Memo", true);
        columns[SecurableEntityRegistry.SecureFlagAttribute] = ("Boolean", true);
        columns[InheritedAccessPermission.Column] = ("Picklist", true);
        columns[AccessInheritance.Column] = ("Memo", true);
        columns[SecurableEntityRegistry.SecurityBusinessUnitAttribute] = ("Lookup", true);
    }

    private (string Source, string SourceField) SourceOf(string table) =>
        table == "sprk_matter" ? ("sprk_project", "sprk_projectdescription") : ("sprk_matter", "sprk_matterdescription");

    [Theory]
    [MemberData(nameof(EverySecureRootAndAccessColumn))]
    public async Task CreateAsync_ARuleTargetingASecureRootsFlagOrAccessColumn_IsSkippedWithTheWarning_AndTheColumnIsNotWritten(
        string table, string column)
    {
        var (source, _) = SourceOf(table);
        var (_, fieldType, value) = SecureRootAccessColumnRules.Single(r => r.Column == column);
        DeclareCreatableColumns(table);
        Profile(source, table, Rule(null, $" {column.ToUpperInvariant()} ", fieldType, Default, value));

        var outcome = await Service().CreateAsync(RootCreate(table), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => k.StartsWith(column, StringComparison.OrdinalIgnoreCase),
            $"{column} is the BFF's own on a {table} (must fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains(column)
                                                                        && w.Detail.Contains("managed by the server"));
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public async Task CreateAsync_AnOrdinaryColumnRule_IsStillFilled_BesideASkippedSecureFlagRule(string table)
    {
        var (source, sourceField) = SourceOf(table);
        DeclareCreatableColumns(table);
        Profile(source, table,
            Rule(sourceField, "sprk_description", Memo),
            Rule(null, SecurableEntityRegistry.SecureFlagAttribute, CreateTimeFieldMapping.FieldTypeBoolean, Default, "true"));

        var outcome = await Service().CreateAsync(RootCreate(table), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be(table == "sprk_matter" ? "From the project" : "From the matter",
            "an ordinary column is still mapped (must not fire)");
        fields.Should().NotContainKey(SecurableEntityRegistry.SecureFlagAttribute);
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Code.Should().Be(RecordCreationWarning.MappingRuleSkippedCode);
    }
}
