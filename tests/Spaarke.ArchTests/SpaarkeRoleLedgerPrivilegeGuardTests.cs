using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Source-side half of the append-only and single-closer guarantees for Signals and Decision Records
/// (ontology task 049; D-17, D-62, D-147; uac-r2 K-A). The live half is the union-of-roles check
/// (<c>scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -Verify</c>, NFR-11).
/// <list type="bullet">
/// <item>Only <c>Spaarke Ontology Service</c> (the writer identity) may Create a Decision Record; no Spaarke role may Write or Delete one.
/// The BFF commit route is the one creator, so a user who could create their own record could forge one the stored-record replay accepts
/// (the 043 review hash is a plain SHA-256).</item>
/// <item><c>Spaarke Console User</c> and <c>Spaarke Ontology Administrator</c> may not Write <c>sprk_signal</c>: only the BFF commit
/// route closes a Signal, so FR-18 ("no human-resolved Signal has a null record") is enforced by privilege, not by the browser's good
/// behaviour. The administrator's retire, publish and re-evaluation write Signals through the writer identity (owner Q6, 2026-10-17).</item>
/// </list>
/// </summary>
/// <remarks>
/// Scans every role in <c>src/dataverse/solutions/SpaarkeMaster/Roles</c>, the only source-controlled role definitions. Any depth
/// counts. Other roles' Signal Write (platform and service roles) is the live union check's concern, not this guard's.
/// </remarks>
public class SpaarkeRoleLedgerPrivilegeGuardTests
{
    private const string WriterRole = "Spaarke Ontology Service";
    private static readonly string[] SignalWriteForbiddenRoles = ["Spaarke Console User", "Spaarke Ontology Administrator"];

    private static readonly string[] DecisionRecordCreate = ["prvCreatesprk_DecisionRecord"];
    private static readonly string[] DecisionRecordWriteOrDelete = ["prvWritesprk_DecisionRecord", "prvDeletesprk_DecisionRecord"];
    private static readonly string[] SignalWrite = ["prvWritesprk_Signal"];

    [Fact(DisplayName = "Spaarke roles: only the writer creates Decision Records, none writes or deletes them, Console User and Ontology Administrator cannot write a Signal (task 049)")]
    public void SourceRolesKeepTheLedgerGuarantees()
    {
        var rolesRoot = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
        var roles = Directory.EnumerateFiles(rolesRoot, "*.xml", SearchOption.TopDirectoryOnly).ToList();
        Assert.True(roles.Count >= 12,
            $"Scanner found only {roles.Count} role definitions under {rolesRoot} (expected >= 12). Repo root resolved to '{SourceScan.RepoRoot}'.");

        // The named roles must be among the scanned files. If a role file's name attribute changed, the role rules below would
        // silently stop applying to it and this guard would pass green.
        var scannedNames = roles
            .Select(path => (string?)XDocument.Parse(File.ReadAllText(path)).Root?.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal);
        var missingRoles = SignalWriteForbiddenRoles.Append(WriterRole)
            .Where(r => !scannedNames.Contains(r))
            .ToList();
        Assert.True(missingRoles.Count == 0,
            $"The role guard names roles that no scanned file under {rolesRoot} defines: [{string.Join(", ", missingRoles)}]. "
            + "A role file was renamed or removed, so the Signal Write and Decision Record Create rules would no longer apply to it. "
            + "Update WriterRole / SignalWriteForbiddenRoles with the rename.");

        var violations = roles
            .SelectMany(path => Violations(File.ReadAllText(path))
                .Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, path)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "A Spaarke role breaks the Signal / Decision Record privilege rules (D-17, D-62, D-147, uac-r2 K-A). Only the BFF commit route "
            + "(writer identity) creates a Decision Record or closes a Signal:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Role ledger-privilege guard control: Create/Write/Delete on a Decision Record and Console User / Ontology Administrator Write on a Signal are reported")]
    public void GuardFiresOnTheForbiddenGrants()
    {
        const string humanRole = """
            <Role id="{00000000-0000-0000-0000-000000000001}" name="Spaarke Console User">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_DecisionRecord" level="Deep" />
                <RolePrivilege name="prvCreatesprk_DecisionRecord" level="Deep" />
                <RolePrivilege name="prvWritesprk_DecisionRecord" level="Basic" />
                <RolePrivilege name="prvDeletesprk_DecisionRecord" level="Basic" />
                <RolePrivilege name="prvWritesprk_Signal" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        var found = Violations(humanRole);

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.Contains("prvCreatesprk_DecisionRecord", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvWritesprk_DecisionRecord", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvDeletesprk_DecisionRecord", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvWritesprk_Signal", StringComparison.Ordinal));

        const string adminRole = """
            <Role id="{00000000-0000-0000-0000-000000000004}" name="Spaarke Ontology Administrator">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_DecisionRecord" level="Deep" />
                <RolePrivilege name="prvWritesprk_Signal" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        var adminFound = Assert.Single(Violations(adminRole));
        Assert.Contains("prvWritesprk_Signal", adminFound, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Role ledger-privilege guard control: the writer may Create, Reads, comments, look-alike names and other roles' Signal Write are not reported")]
    public void GuardIgnoresAllowedGrants()
    {
        const string writerRole = """
            <Role id="{00000000-0000-0000-0000-000000000002}" name="Spaarke Ontology Service">
              <!-- prvWritesprk_DecisionRecord is deliberately absent -->
              <RolePrivileges>
                <RolePrivilege name="prvCreatesprk_DecisionRecord" level="Global" />
                <RolePrivilege name="prvWritesprk_Signal" level="Global" />
                <RolePrivilege name="prvReadsprk_DecisionRecord" level="Global" />
              </RolePrivileges>
            </Role>
            """;
        // Console User is the role the Signal Write rule fires on, so the look-alike names must sit HERE to prove the match is
        // exact: a StartsWith (SignalExtra), EndsWith (xprvWrite...) or Contains matcher would report them, an Ordinal equality does not.
        const string consoleUserRole = """
            <Role id="{00000000-0000-0000-0000-000000000003}" name="Spaarke Console User">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_DecisionRecord" level="Deep" />
                <RolePrivilege name="prvWritesprk_SignalExtra" level="Deep" />
                <RolePrivilege name="xprvWritesprk_Signal" level="Deep" />
                <RolePrivilege name="prvAppendTosprk_Signal" level="Deep" />
                <RolePrivilege name="prvReadsprk_Signal" level="Deep" />
              </RolePrivileges>
            </Role>
            """;
        // Not a named role, so its Signal Write is the live union check's concern, not this guard's.
        const string otherRole = """
            <Role id="{00000000-0000-0000-0000-000000000004}" name="Spaarke Matter Contributor">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_DecisionRecord" level="Deep" />
                <RolePrivilege name="prvWritesprk_Signal" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Empty(Violations(consoleUserRole));
        Assert.Empty(Violations(writerRole));
        Assert.Empty(Violations(otherRole));
    }

    /// <summary>The forbidden grants in one role definition, as "role: name (level)".</summary>
    internal static List<string> Violations(string roleXml)
    {
        var doc = XDocument.Parse(roleXml);
        var roleName = (string?)doc.Root?.Attribute("name") ?? string.Empty;
        var isWriter = string.Equals(roleName, WriterRole, StringComparison.Ordinal);
        var isSignalWriteForbidden = SignalWriteForbiddenRoles.Contains(roleName, StringComparer.Ordinal);

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?"))
            .Where(p =>
                DecisionRecordWriteOrDelete.Contains(p.Name, StringComparer.Ordinal)
                || (!isWriter && DecisionRecordCreate.Contains(p.Name, StringComparer.Ordinal))
                || (isSignalWriteForbidden && SignalWrite.Contains(p.Name, StringComparer.Ordinal)))
            .Select(p => $"{roleName}: {p.Name} ({p.Level})")
            .ToList();
    }
}
