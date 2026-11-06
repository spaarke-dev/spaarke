using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Sync guard (ebilling-r1 task 070 fix-round-1, K2): the closed <see cref="RuleType"/> enum and the Dataverse choice
/// <c>sprk_policyversion_sprk_ruletype</c> in solution source must list the same members with the same values. The enum is the
/// code half of the closed set (D-27); the choice is what an admin picks in the Spaarke Platform app. A member added to one and
/// not the other is either unselectable (enum only) or a value the BFF refuses at every consumer (choice only).
/// </summary>
/// <remarks>
/// Reads the solution source, not the live environment: dev is changed by <c>scripts/ebilling/Deploy-EbillingRuleTypeOptions.ps1</c>
/// and the source is edited to match, so a missing source edit is exactly what this guards. Maintain-class (ADR-038,
/// <c>tests/unit/domain/**</c>): pure logic over one checked-in file.
/// </remarks>
public class RuleTypeOptionSetSyncTests
{
    private const string OptionSetName = "sprk_policyversion_sprk_ruletype";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>The option set's value -> English label, read from the solution source.</summary>
    private static Dictionary<int, string> SourceOptions()
    {
        var path = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_PolicyVersion", "Entity.xml");
        var optionSet = XDocument.Load(path).Descendants("optionset")
            .Single(e => (string?)e.Attribute("Name") == OptionSetName);
        return optionSet.Descendants("option").ToDictionary(
            o => int.Parse((string)o.Attribute("value")!, CultureInfo.InvariantCulture),
            o => (string)o.Descendants("label").Single(l => (string?)l.Attribute("languagecode") == "1033").Attribute("description")!);
    }

    /// <summary>Human-readable differences between the enum and an option set; empty when they agree, names and values.</summary>
    internal static List<string> Differences(IReadOnlyDictionary<int, string> options)
    {
        var enumMembers = Enum.GetValues<RuleType>().ToDictionary(t => (int)t, t => t.ToString());
        var differences = new List<string>();
        foreach (var (value, name) in enumMembers)
        {
            if (!options.TryGetValue(value, out var label)) differences.Add($"enum {name}={value} is missing from the option set");
            else if (!string.Equals(label, name, StringComparison.Ordinal)) differences.Add($"value {value}: enum '{name}' vs option '{label}'");
        }

        foreach (var (value, label) in options)
        {
            if (!enumMembers.ContainsKey(value)) differences.Add($"option '{label}'={value} has no RuleType member");
        }

        return differences;
    }

    [Fact]
    public void RuleTypeEnum_EqualsTheSolutionSourceOptionSet_NamesAndValues()
    {
        Differences(SourceOptions()).Should().BeEmpty(
            "the RuleType enum and sprk_policyversion_sprk_ruletype in SpaarkeMaster Entity.xml must list the same members and values");
        SourceOptions().Should().HaveCount(Enum.GetValues<RuleType>().Length);
    }

    // Must-fire controls: the comparison reports a missing option, an extra option, a renamed option and a renumbered one.
    [Fact]
    public void Differences_ReportsAMissingAnExtraARenamedAndARenumberedOption()
    {
        var source = SourceOptions();

        var missing = new Dictionary<int, string>(source);
        missing.Remove((int)RuleType.AiObservation);
        Differences(missing).Should().ContainSingle().Which.Should().Contain("AiObservation").And.Contain("missing");

        var extra = new Dictionary<int, string>(source) { [100000099] = "Phantom" };
        Differences(extra).Should().ContainSingle().Which.Should().Contain("Phantom");

        var renamed = new Dictionary<int, string>(source) { [(int)RuleType.Duplicate] = "Duplicates" };
        Differences(renamed).Should().ContainSingle().Which.Should().Contain("Duplicate");

        var renumbered = new Dictionary<int, string>(source);
        renumbered.Remove((int)RuleType.InvoiceAge);
        renumbered[100000098] = "InvoiceAge";
        Differences(renumbered).Should().HaveCount(2);
    }
}
