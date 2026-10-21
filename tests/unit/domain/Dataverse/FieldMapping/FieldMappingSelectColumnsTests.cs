using System.Text.RegularExpressions;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 004 (spec FR-05a) — the columns the field-mapping read path SELECTs are columns
/// the solution (and so the deployed table) declares. A wrong spelling is a Dataverse 400 at runtime and nothing else
/// would observe it: <c>Mock&lt;HttpMessageHandler&gt;</c> is banned (ADR-038 B1), so the extracted
/// <see cref="FieldMappingColumns"/> constants are the observation point (ADR-038 Amendment A2).
/// </summary>
/// <remarks>
/// The profile and rule tables carry DIFFERENT mode columns, both deployed under exactly these names (Dataverse
/// <c>describe</c> on spaarkedev1, 2026-10-21): <c>sprk_fieldmappingprofile.sprk_capabilitymode</c> and
/// <c>sprk_fieldmappingrule.sprk_compatibilitymode</c>. KEEP path: tests/unit/domain/** (ADR-038 §2).
/// </remarks>
public class FieldMappingSelectColumnsTests
{
    private static readonly Regex LogicalName = new("<LogicalName>([^<]+)</LogicalName>", RegexOptions.Compiled);

    [Fact]
    public void ProfileSelect_EveryColumnIsDeclaredOnTheProfileEntity()
    {
        var declared = DeclaredColumns("sprk_FieldMappingProfile");

        ColumnsOf(FieldMappingColumns.ProfileSelect).Should().BeSubsetOf(declared,
            "a select of an undeclared column is a Dataverse 400 on every profile read");
    }

    [Fact]
    public void RuleSelect_EveryColumnIsDeclaredOnTheRuleEntity()
    {
        var declared = DeclaredColumns("sprk_FieldMappingRule");

        ColumnsOf(FieldMappingColumns.RuleSelect).Should().BeSubsetOf(declared,
            "a select of an undeclared column is a Dataverse 400 on every rule read");
    }

    [Fact]
    public void ModeColumns_AreTheDeployedNamePerTable_NotSwapped()
    {
        ColumnsOf(FieldMappingColumns.ProfileSelect).Should().Contain("sprk_capabilitymode")
            .And.NotContain("sprk_compatibilitymode", "the profile table has no sprk_compatibilitymode");
        ColumnsOf(FieldMappingColumns.RuleSelect).Should().Contain("sprk_compatibilitymode")
            .And.NotContain("sprk_capabilitymode", "the rule table has no sprk_capabilitymode");
    }

    /// <summary>Logical column names in a $select list; a lookup's <c>_x_value</c> read form maps to its <c>x</c> attribute.</summary>
    private static string[] ColumnsOf(string select) => select.Split(',')
        .Select(c => c.StartsWith('_') && c.EndsWith("_value", StringComparison.Ordinal) ? c[1..^"_value".Length] : c)
        .ToArray();

    private static HashSet<string> DeclaredColumns(string entityFolder)
    {
        var path = LocateEntityXml(entityFolder);
        return LogicalName.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string LocateEntityXml(string entityFolder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", entityFolder, "Entity.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {entityFolder}/Entity.xml by walking up from {AppContext.BaseDirectory}.");
    }
}
