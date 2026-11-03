using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 094 (UAC review round 5, K3-a): stored files are written only by the BFF (the submission and file services stamp the
/// owner, source markers and access links in one place), so no source-controlled Spaarke role may hold Create, Write, Append or AppendTo on
/// <c>sprk_storedfile</c> unless a UAC review has put that role on <see cref="UacReviewedRoles"/>. The list is empty today. Read, Delete,
/// Assign and Share are not covered here: Read is how people see their own filings, and the others are handled by the access model.
/// </summary>
/// <remarks>
/// Modelled on <see cref="ProcedureRolePrivilegeGuardTests"/> and reuses its role-file selector, so a role added to any solution's
/// <c>Roles</c> folder (not only SpaarkeMaster) is covered. Matching is exact on <c>prv{Action}sprk_storedfile</c>, case-insensitive,
/// at any privilege depth.
/// </remarks>
public class StoredFileRolePrivilegeGuardTests
{
    /// <summary>Role names a UAC review has agreed may hold the guarded privileges. Empty today; add a name only with a UAC sign-off.</summary>
    private static readonly HashSet<string> UacReviewedRoles = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex GuardedStoredFilePrivilege = new(
        "^prv(Create|Write|Append|AppendTo)sprk_storedfile$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact(DisplayName = "094: no Spaarke role grants Create/Write/Append/AppendTo on sprk_storedfile unless a UAC review listed it")]
    public void SourceRolesGrantNoWriteOnStoredFiles()
    {
        var solutionsRoot = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");
        var roles = ProcedureRolePrivilegeGuardTests.RoleFiles(solutionsRoot);
        Assert.True(roles.Count >= 12,
            $"Scanner found only {roles.Count} role definitions under {solutionsRoot}/**/Roles (expected >= 12). Repo root resolved to '{SourceScan.RepoRoot}'.");
        Assert.Contains(roles, r => r.Contains(Path.Combine("SpaarkeMaster", "Roles"), StringComparison.OrdinalIgnoreCase)); // non-vacuity
        Assert.True(roles.Sum(r => XDocument.Parse(File.ReadAllText(r)).Descendants().Count(e => e.Name.LocalName == "RolePrivilege")) >= 100,
            "The role definitions carry fewer than 100 RolePrivilege elements in all; the scanner is not reading what it should.");

        var violations = roles
            .SelectMany(path => Violations(File.ReadAllText(path)).Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, path)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "A Spaarke role grants Create, Write, Append or AppendTo on sprk_storedfile. Stored files are written only by the BFF; a role grant "
            + "would let a person create or re-link a file row outside the submission and file services. Remove the grant, or have the role "
            + "reviewed by UAC and listed in UacReviewedRoles:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "094 must-fire: Create, Write, Append and AppendTo on sprk_storedfile are reported, in any case and at any depth")]
    public void GuardFiresOnTheGuardedGrants()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000011}" name="Spaarke Test Role">
              <RolePrivileges>
                <RolePrivilege name="prvCreatesprk_storedfile" level="Basic" />
                <RolePrivilege name="prvWritesprk_StoredFile" level="Global" />
                <RolePrivilege name="prvAppendsprk_storedfile" level="Deep" />
                <RolePrivilege name="prvAppendTosprk_storedfile" level="Local" />
              </RolePrivileges>
            </Role>
            """;

        var found = Violations(role);

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.Contains("prvCreatesprk_storedfile", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvWritesprk_StoredFile", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvAppendsprk_storedfile", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvAppendTosprk_storedfile", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "094 must-not-fire: Read-only, Delete/Assign/Share, other tables and look-alike names are not reported")]
    public void GuardIgnoresOtherGrants()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000012}" name="Spaarke Test Role Two">
              <!-- prvWritesprk_storedfile is deliberately absent -->
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_storedfile" level="Global" />
                <RolePrivilege name="prvDeletesprk_storedfile" level="Basic" />
                <RolePrivilege name="prvAssignsprk_storedfile" level="Basic" />
                <RolePrivilege name="prvSharesprk_storedfile" level="Basic" />
                <RolePrivilege name="prvCreatesprk_document" level="Deep" />
                <RolePrivilege name="prvWritesprk_storedfilex" level="Deep" />
                <RolePrivilege name="xprvWritesprk_storedfile" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Empty(Violations(role));
    }

    [Fact(DisplayName = "094 control: a role on the UAC-reviewed list is exempt, an unlisted role with the same grant is not")]
    public void ReviewedRoleIsExemptAndOthersAreNot()
    {
        const string template = """
            <Role id="{00000000-0000-0000-0000-000000000013}" name="{NAME}">
              <RolePrivileges>
                <RolePrivilege name="prvWritesprk_storedfile" level="Basic" />
              </RolePrivileges>
            </Role>
            """;
        var reviewed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Spaarke Reviewed Role" };

        Assert.Empty(Violations(template.Replace("{NAME}", "Spaarke Reviewed Role"), reviewed));
        Assert.Single(Violations(template.Replace("{NAME}", "Spaarke Unreviewed Role"), reviewed));
        Assert.Empty(UacReviewedRoles); // empty today; a new entry needs UAC sign-off and a new reason here
    }

    private static List<string> Violations(string roleXml) => Violations(roleXml, UacReviewedRoles);

    /// <summary>The guarded grants in one role definition, as "role: name (level)"; empty when the role is on <paramref name="reviewed"/>.</summary>
    private static List<string> Violations(string roleXml, HashSet<string> reviewed)
    {
        var doc = XDocument.Parse(roleXml);
        var roleName = (string?)doc.Root?.Attribute("name") ?? string.Empty;
        if (reviewed.Contains(roleName))
        {
            return new List<string>();
        }

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?"))
            .Where(p => GuardedStoredFilePrivilege.IsMatch(p.Name))
            .Select(p => $"{roleName}: {p.Name} ({p.Level})")
            .ToList();
    }
}
