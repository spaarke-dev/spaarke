using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// No Spaarke-defined security role may grant audit-history read (<c>prvReadRecordAuditHistory</c>, <c>prvReadAuditSummary</c>).
/// Ontology task 145 redacts a Signal's stored evidence lines (<c>sprk_signal.sprk_evidencerefs</c>) once their witness
/// communication leaves the Signal's secure family (D-123). Column audit keeps the value from before the redaction, so a role that
/// can read audit history would read the secure witness's subject and sender back. In spaarkedev1 (2026-10-14) only admin-class
/// platform roles hold these privileges. This guard keeps every Spaarke role that way until the owner decides on column audit
/// (DI-81).
/// </summary>
/// <remarks>
/// Scans every role in <c>src/dataverse/solutions/SpaarkeMaster/Roles</c>, the only source-controlled role definitions
/// (12 on 2026-10-14). <c>config/secure-record-owner-role.json</c> lists tables, not privileges, so it cannot grant audit read.
/// Any depth counts, since audit read at Basic depth still reads the caller's own Signals' history. No baseline: no role grants
/// either privilege today.
/// </remarks>
public class SpaarkeRoleAuditReadGuardTests
{
    internal static readonly string[] AuditReadPrivileges = ["prvReadRecordAuditHistory", "prvReadAuditSummary"];

    [Fact(DisplayName = "Spaarke roles: none grants audit-history read (prvReadRecordAuditHistory / prvReadAuditSummary; task 145, DI-81)")]
    public void NoSpaarkeRoleGrantsAuditHistoryRead()
    {
        var rolesRoot = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
        var roles = Directory.EnumerateFiles(rolesRoot, "*.xml", SearchOption.TopDirectoryOnly).ToList();
        Assert.True(roles.Count >= 12,
            $"Scanner found only {roles.Count} role definitions under {rolesRoot} (expected >= 12 on 2026-10-14). Repo root resolved to '{SourceScan.RepoRoot}'.");

        var violations = roles
            .SelectMany(path => AuditReadGrants(File.ReadAllText(path))
                .Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, path)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "A Spaarke role grants audit-history read. Column audit on sprk_signal.sprk_evidencerefs keeps evidence text that task 145 "
            + "redacted for D-123, so this role would read a secure witness's subject and sender back. Remove the privilege, or settle "
            + "DI-81 (turn off that column's audit) with the owner first:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Role audit-read guard control: either audit-read privilege is reported, at any depth")]
    public void GuardFiresOnAuditReadPrivileges()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000001}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_signal" level="Basic" />
                <RolePrivilege name="prvReadRecordAuditHistory" level="Basic" />
                <RolePrivilege name="prvReadAuditSummary" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        var found = AuditReadGrants(role);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Contains("prvReadRecordAuditHistory", StringComparison.Ordinal) && f.Contains("Basic", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvReadAuditSummary", StringComparison.Ordinal) && f.Contains("Global", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Role audit-read guard control: other audit privileges, comments and look-alike names are not reported")]
    public void GuardIgnoresOtherPrivilegesAndComments()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000002}" name="Spaarke Example">
              <!-- prvReadRecordAuditHistory is deliberately absent -->
              <Description>Does not hold prvReadAuditSummary.</Description>
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_signal" level="Basic" />
                <RolePrivilege name="prvAudit" level="Global" />
                <RolePrivilege name="prvReadRecordAuditHistoryExtra" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Empty(AuditReadGrants(role));
    }

    /// <summary>Every <c>RolePrivilege</c> naming an audit-read privilege exactly, as "name (level)".</summary>
    internal static List<string> AuditReadGrants(string roleXml) =>
        XDocument.Parse(roleXml)
            .Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?"))
            .Where(p => AuditReadPrivileges.Contains(p.Name, StringComparer.Ordinal))
            .Select(p => $"{p.Name} ({p.Level})")
            .ToList();
}
