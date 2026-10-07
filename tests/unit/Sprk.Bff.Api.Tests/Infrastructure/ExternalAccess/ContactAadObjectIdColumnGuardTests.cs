using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 141 — no query against the CONTACT entity may name
/// <c>azureactivedirectoryobjectid</c> anywhere in <c>src/server</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> <c>contact.azureactivedirectoryobjectid</c> does not exist in spaarkedev1. Three
/// readers queried it anyway (the workforce contact branch, the systemuser fallback, "assign it to me"); every
/// query threw, every throw was swallowed as "no contact", and every Type-2 employee got 403 while every test
/// passed against doubles that modelled a column the platform does not have. A behavioural test cannot catch the
/// next one for the same reason. The binding key is <c>contact.sprk_externalobjectid</c>; the Entra oid column
/// belongs to <c>systemuser</c>, where it is legitimately read in dozens of places.</para>
///
/// <para><b>How it classifies.</b> Comments are stripped. Each code occurrence of the column (or of a constant
/// whose value is the column) is attributed to the NEAREST preceding entity marker — <c>"contact"</c> /
/// <c>contacts(</c> / <c>contacts?</c> / <c>ContactEntitySet</c> versus <c>"systemuser"</c> / <c>systemusers</c>
/// / <c>SystemUserEntitySet</c> — within a bounded window. A contact attribution fails; an occurrence with no
/// marker at all also fails, because "cannot tell" is not "safe". The classifier is itself tested on seeded
/// snippets below, so a regression in the guard reddens too.</para>
/// </remarks>
[Trait("Category", "Security")]
public class ContactAadObjectIdColumnGuardTests
{
    private const string Column = "azureactivedirectoryobjectid";
    private const int Window = 40;

    /// <summary>A contact query: the logical name, the entity set, or a constant naming either.</summary>
    private static readonly Regex ContactMarker = new(
        @"""contact""|\bcontacts\s*[\(\?/'""$]|\bContactEntity(Set|Name)?\b|name=""""contact""""",
        RegexOptions.Compiled);

    /// <summary>
    /// A systemuser query — deliberately broad (<c>systemuser</c> anywhere in an identifier or literal:
    /// <c>"systemuser"</c>, <c>systemusers</c>, <c>systemuserid</c>, <c>SystemUserEntity</c>, a method named for
    /// systemusers). Breadth is safe here because attribution is by NEAREST marker: a contact query inside a
    /// systemuser-named method is still nearer its own <c>"contact"</c>.
    /// </summary>
    private static readonly Regex SystemUserMarker = new(@"systemuser", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>How far AFTER an occurrence a marker may be (a filter built before the URL that names its set).</summary>
    private const int ForwardWindow = 10;

    private static readonly Regex ConstantAlias = new(
        @"const\s+string\s+(?<name>\w+)\s*=\s*""azureactivedirectoryobjectid""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NoQueryAgainstContact_NamesTheAadObjectIdColumn()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles())
        {
            violations.AddRange(Classify(File.ReadAllText(file))
                .Select(v => $"{Path.GetRelativePath(RepoRoot(), file)}:{v}"));
        }

        violations.Should().BeEmpty(
            "contact.azureactivedirectoryobjectid does not exist in dev and is not the binding key — query "
            + "contact.sprk_externalobjectid (ContactBindingDecision.ContactsBoundToQuery / the identity store). Found:\n"
            + string.Join("\n", violations));
    }

    // ── The guard bites: the classifier on seeded snippets ───────────────────────────────────────────

    [Theory]
    [InlineData("var q = new QueryExpression(\"contact\") { ColumnSet = new ColumnSet(\"contactid\") };\nq.Criteria.AddCondition(\"azureactivedirectoryobjectid\", ConditionOperator.Equal, oid);")]
    [InlineData("var url = $\"contacts?$filter=azureactivedirectoryobjectid eq '{oid}'\";")]
    [InlineData("await client.QueryAsync<Row>(ContactEntitySet,\n    filter: $\"azureactivedirectoryobjectid eq {oid}\");")]
    [InlineData("private const string Col = \"azureactivedirectoryobjectid\";\nvar q = new QueryExpression(\"contact\");\nq.Criteria.AddCondition(Col, ConditionOperator.Equal, oid);")]
    [InlineData("var orphan = \"azureactivedirectoryobjectid\";")]
    public void TheClassifier_FlagsAContactQuery_OrAnUnattributableOccurrence(string seeded)
        => Classify(seeded).Should().NotBeEmpty();

    [Theory]
    [InlineData("var q = new QueryExpression(\"systemuser\");\nq.Criteria.AddCondition(\"azureactivedirectoryobjectid\", ConditionOperator.Equal, oid);")]
    [InlineData("var url = $\"systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'\";")]
    [InlineData("// contact.azureactivedirectoryobjectid is retired — a comment is not a query\nvar x = 1;")]
    [InlineData("var c = new QueryExpression(\"contact\");\nvar s = new QueryExpression(\"systemuser\");\ns.Criteria.AddCondition(\"azureactivedirectoryobjectid\", ConditionOperator.Equal, oid);")]
    public void TheClassifier_PassesSystemUserQueriesAndComments(string seeded)
        => Classify(seeded).Should().BeEmpty();

    // ── Classifier ───────────────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<string> Classify(string source)
    {
        var lines = StripComments(source);
        var aliases = lines.SelectMany(l => ConstantAlias.Matches(l).Select(m => m.Groups["name"].Value)).ToHashSet();
        var violations = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (ConstantAlias.IsMatch(line))
            {
                continue; // the declaration; its USES are classified
            }

            var positions = Occurrences(line, aliases).ToList();
            foreach (var pos in positions)
            {
                var marker = NearestMarker(lines, i, pos);
                if (marker != "systemuser")
                {
                    violations.Add($"{i + 1}: [{marker ?? "no entity marker"}] {line.Trim()}");
                }
            }
        }

        return violations;
    }

    private static IEnumerable<int> Occurrences(string line, HashSet<string> aliases)
    {
        var idx = line.IndexOf(Column, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            yield return idx;
            idx = line.IndexOf(Column, idx + Column.Length, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var alias in aliases)
        {
            foreach (Match m in Regex.Matches(line, $@"\b{Regex.Escape(alias)}\b"))
            {
                yield return m.Index;
            }
        }
    }

    private static string? NearestMarker(IReadOnlyList<string> lines, int lineIndex, int column)
    {
        // Backward first (the usual shape: the entity is named, then the column).
        string? backward = null;
        var backwardDistance = int.MaxValue;
        for (var i = lineIndex; i >= Math.Max(0, lineIndex - Window); i--)
        {
            var text = i == lineIndex ? lines[i][..column] : lines[i];
            var contact = LastIndex(ContactMarker, text);
            var systemUser = LastIndex(SystemUserMarker, text);
            if (contact < 0 && systemUser < 0)
            {
                continue;
            }

            backward = contact > systemUser ? "contact" : "systemuser";
            backwardDistance = lineIndex - i;
            break;
        }

        // Then forward, a short way (a filter string built before the URL that names its entity set).
        string? forward = null;
        var forwardDistance = int.MaxValue;
        for (var i = lineIndex; i <= Math.Min(lines.Count - 1, lineIndex + ForwardWindow); i++)
        {
            var text = i == lineIndex ? lines[i][column..] : lines[i];
            var contact = FirstIndex(ContactMarker, text);
            var systemUser = FirstIndex(SystemUserMarker, text);
            if (contact < 0 && systemUser < 0)
            {
                continue;
            }

            forward = systemUser < 0 || (contact >= 0 && contact < systemUser) ? "contact" : "systemuser";
            forwardDistance = i - lineIndex;
            break;
        }

        return forwardDistance < backwardDistance ? forward : backward ?? forward;
    }

    private static int FirstIndex(Regex regex, string text)
    {
        var m = regex.Match(text);
        return m.Success ? m.Index : -1;
    }

    private static int LastIndex(Regex regex, string text)
    {
        var last = -1;
        foreach (Match m in regex.Matches(text))
        {
            last = m.Index;
        }

        return last;
    }

    private static List<string> StripComments(string source)
    {
        var result = new List<string>();
        var inBlock = false;
        foreach (var raw in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw;
            if (inBlock)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Add(string.Empty);
                    continue;
                }

                line = line[(end + 2)..];
                inBlock = false;
            }

            var start = line.IndexOf("/*", StringComparison.Ordinal);
            if (start >= 0 && !line[..start].Contains('"'))
            {
                inBlock = !line[start..].Contains("*/");
                line = line[..start];
            }

            var trimmed = line.TrimStart();
            result.Add(trimmed.StartsWith("//", StringComparison.Ordinal) ? string.Empty : line);
        }

        return result;
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "server"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // ⚠️ `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
