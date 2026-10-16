using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ontology;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 100: the Rules service shows the rule's lane, severity, subject type and work type as words, so it carries
/// the option-set labels itself, and it writes <c>statuscode</c> Retired as a number. A label table that drifted from the solution would
/// show the wrong word for a stored value with every behavioural test green (the fake Dataverse echoes whatever the table says), so this
/// reads the exported <c>sprk_policy</c> solution source (<c>SpaarkeMaster/Entities/sprk_Policy/Entity.xml</c>) and compares. The
/// <c>sprk_domain</c> value (Ontology = 100000001) is not in the exported source; it was read live on spaarkedev1 and is pinned here
/// as a constant with the swap note in the service (ebilling-r1 task 070, #1911).
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path); pure source read, no network.</remarks>
public class OntologyAdminRulesSchemaAgreementTests
{
    [Theory]
    [InlineData("sprk_lane")]
    [InlineData("sprk_severity")]
    [InlineData("sprk_subjecttype")]
    [InlineData("sprk_worktype")]
    public void TheOptionLabels_AgreeWithTheSolutionSource(string column)
    {
        var fromSolution = Options($"sprk_policy_{column}");
        var inService = column switch
        {
            "sprk_lane" => OntologyAdminRulesService.LaneLabels,
            "sprk_severity" => OntologyAdminRulesService.SeverityLabels,
            "sprk_subjecttype" => OntologyAdminRulesService.SubjectTypeLabels,
            _ => OntologyAdminRulesService.WorkTypeLabels,
        };

        fromSolution.Should().NotBeEmpty();
        inService.OrderBy(p => p.Key).Should().Equal(fromSolution.OrderBy(p => p.Key), "the service shows the words the solution defines");
    }

    [Fact]
    public void Retired_IsTheStatusReasonThatBelongsToTheInactiveState()
    {
        var status = StatusOptions().Single(s => s.Label == "Retired");

        status.Value.Should().Be(OntologyAdminRulesService.StatusRetired);
        status.State.Should().Be(1, "Retire writes statecode 1 together with the Retired status reason");
        StatusOptions().Single(s => s.Label == "Active").State.Should().Be(OntologyAdminRulesService.StateActive,
            "the evaluator reads statecode 0, so a Retired rule is not evaluated");
    }

    [Fact]
    public void TheOntologyDomain_IsThePinnedLiveValue()
    {
        OntologyAdminRulesService.OntologyDomain.Should().Be(100000001, "read live on spaarkedev1: Billing 100000000, Ontology 100000001, Docketing 100000002");
    }

    // ── the solution source ─────────────────────────────────────────────────────────────────────────────────────────

    private static XDocument Entity() => XDocument.Load(Path.Combine(
        RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_Policy", "Entity.xml"));

    private static List<KeyValuePair<int, string>> Options(string optionSetName) =>
        Entity().Descendants("optionset").Where(o => (string?)o.Attribute("Name") == optionSetName)
            .SelectMany(o => o.Descendants("option"))
            .Select(o => new KeyValuePair<int, string>(
                int.Parse((string)o.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture),
                (string)o.Descendants("label").First().Attribute("description")!))
            .ToList();

    private static List<(int Value, int State, string Label)> StatusOptions() =>
        Entity().Descendants("optionset").Where(o => (string?)o.Attribute("Name") == "sprk_policy_statuscode")
            .SelectMany(o => o.Descendants("status"))
            .Select(s => (
                int.Parse((string)s.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse((string)s.Attribute("state")!, System.Globalization.CultureInfo.InvariantCulture),
                (string)s.Descendants("label").First().Attribute("description")!))
            .ToList();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
