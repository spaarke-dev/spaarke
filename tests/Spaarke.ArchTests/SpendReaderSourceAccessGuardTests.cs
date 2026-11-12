using System.Xml.Linq;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 165b-4 (ontology-r1 O3, O4): the spend readers decide only from server-written sources, so the sources they read must be
/// readable by the identity that reads them, or the answer goes wrong without an error.
/// </summary>
/// <remarks>
/// <para><b>O3 (Decision Records).</b> Counted spend keeps an invoice only when its approve Decision Record is found. Below Global read depth the
/// record of an invoice in another business unit is not returned, so the invoice is silently left out and spend is undercounted (a missing
/// privilege throws, which is safe). This pins <c>prvReadsprk_DecisionRecord</c> at Global in the evaluator's reading role in the solution
/// source. The identity that reads at run time is checked live on dev (<c>SpendReaderSourceAccessLiveTests</c>, tests/integration/seam/Signals), because a role file alone cannot
/// show which roles an environment's identity holds.</para>
/// <para><b>O4 (hold sources).</b> <see cref="InvoiceBlockedHold.HoldSourceColumns"/> are field-secured, so they are kept apart from the
/// derived base columns (which must be unsecured). Each must be secured in its <c>Entity.xml</c>, and the "Spaarke BFF-Managed Field Writers"
/// profile must grant Read (CanRead 4) on it. Membership of the reading identity in that profile is the live half.</para>
/// <para>Enforced by: this ArchTest (offline, CI) plus the live dev tests named above (CLAUDE.md §16 ladder: no type can express a role or
/// profile grant).</para>
/// </remarks>
public sealed class SpendReaderSourceAccessGuardTests
{
    private const string ReadingRoleFile = "Spaarke Ontology Service.xml";
    private const string DecisionRecordRead = "prvReadsprk_DecisionRecord";
    private const string HoldReadProfile = "Spaarke BFF-Managed Field Writers";

    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");

    // ── O3 ──

    [Fact(DisplayName = "165b-4 O3: the evaluator's reading role holds Decision Record read at Global (spend finds every approve record)")]
    public void ReadingRole_HoldsDecisionRecordRead_AtGlobal()
    {
        var role = File.ReadAllText(Path.Combine(SolutionRoot, "Roles", ReadingRoleFile));

        Assert.Null(DecisionRecordReadProblem(role));
    }

    [Theory(DisplayName = "165b-4 O3 control: a lower depth or a missing grant fires")]
    [InlineData("Deep")]
    [InlineData("Local")]
    [InlineData("Basic")]
    [InlineData(null)]
    public void DecisionRecordReadCheck_Fires_BelowGlobal_OrWhenMissing(string? level)
    {
        var doc = XDocument.Parse(File.ReadAllText(Path.Combine(SolutionRoot, "Roles", ReadingRoleFile)));
        var grant = doc.Descendants().Single(e => e.Name.LocalName == "RolePrivilege"
            && string.Equals((string?)e.Attribute("name"), DecisionRecordRead, StringComparison.OrdinalIgnoreCase));
        if (level is null) grant.Remove();
        else grant.SetAttributeValue("level", level);

        Assert.NotNull(DecisionRecordReadProblem(doc.ToString()));
    }

    [Fact(DisplayName = "165b-4 O3 control: a look-alike privilege at Global does not satisfy the check; the exact one does")]
    public void DecisionRecordReadCheck_MatchesTheExactPrivilege()
    {
        const string lookAlike = """<Role><RolePrivileges><RolePrivilege name="prvReadsprk_DecisionRecordType" level="Global" /><RolePrivilege name="prvWritesprk_DecisionRecord" level="Global" /></RolePrivileges></Role>""";
        const string exact = """<Role><RolePrivileges><RolePrivilege name="prvreadsprk_decisionrecord" level="Global" /></RolePrivileges></Role>""";

        Assert.NotNull(DecisionRecordReadProblem(lookAlike));
        Assert.Null(DecisionRecordReadProblem(exact));
    }

    /// <summary>Null when the role grants Decision Record read at Global; otherwise why not.</summary>
    internal static string? DecisionRecordReadProblem(string roleXml)
    {
        var grants = XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege" && string.Equals((string?)e.Attribute("name"), DecisionRecordRead, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (grants.Count == 0) return $"{DecisionRecordRead} is not granted";
        return grants.Any(g => string.Equals((string?)g.Attribute("level"), "Global", StringComparison.Ordinal))
            ? null
            : $"{DecisionRecordRead} is granted at {string.Join(", ", grants.Select(g => (string?)g.Attribute("level")))}, not Global";
    }

    // ── O4 ──

    [Fact(DisplayName = "165b-4 O4: each hold source column is field-secured and readable through the BFF-managed writers profile")]
    public void HoldSourceColumns_AreSecured_AndTheProfileGrantsRead()
    {
        Assert.NotEmpty(InvoiceBlockedHold.HoldSourceColumns);
        var profiles = File.ReadAllText(Path.Combine(SolutionRoot, "Other", "FieldSecurityProfiles.xml"));
        foreach (var (entity, column) in InvoiceBlockedHold.HoldSourceColumns)
        {
            Assert.True(IsSecured(EntityXml(entity), column), $"{entity}.{column} is a hold source and must be field-secured (it is listed apart from the derived base columns)");
            Assert.True(ProfileGrantsRead(profiles, HoldReadProfile, entity, column), $"{HoldReadProfile} must grant Read (CanRead 4) on {entity}.{column}");
        }
    }

    [Fact(DisplayName = "165b-4 O4 control: the checks fire on an unsecured column and on a profile without the read grant")]
    public void HoldSourceChecks_Fire_AndStayQuiet()
    {
        const string entity = """<Entity><attributes><attribute><LogicalName>sprk_a</LogicalName><IsSecured>1</IsSecured></attribute><attribute><LogicalName>sprk_b</LogicalName><IsSecured>0</IsSecured></attribute></attributes></Entity>""";
        Assert.True(IsSecured(entity, "sprk_a"));
        Assert.False(IsSecured(entity, "sprk_b"));
        Assert.False(IsSecured(entity, "sprk_absent"));

        static string Profiles(string name, int canRead) => $"""
            <FieldSecurityProfiles><FieldSecurityProfile name="{name}"><FieldPermissions><FieldPermission>
              <EntityName>sprk_t</EntityName><AttributeName>sprk_a</AttributeName><CanRead>{canRead}</CanRead></FieldPermission></FieldPermissions>
            </FieldSecurityProfile></FieldSecurityProfiles>
            """;
        Assert.True(ProfileGrantsRead(Profiles(HoldReadProfile, 4), HoldReadProfile, "sprk_t", "sprk_a"));
        Assert.False(ProfileGrantsRead(Profiles(HoldReadProfile, 0), HoldReadProfile, "sprk_t", "sprk_a"));
        Assert.False(ProfileGrantsRead(Profiles("Another Profile", 4), HoldReadProfile, "sprk_t", "sprk_a"));
        Assert.False(ProfileGrantsRead(Profiles(HoldReadProfile, 4), HoldReadProfile, "sprk_t", "sprk_b"));
    }

    private static string EntityXml(string entity)
    {
        var folder = Directory.GetDirectories(Path.Combine(SolutionRoot, "Entities"))
            .Single(d => string.Equals(Path.GetFileName(d), entity, StringComparison.OrdinalIgnoreCase));
        return File.ReadAllText(Path.Combine(folder, "Entity.xml"));
    }

    internal static bool IsSecured(string entityXml, string column) =>
        XDocument.Parse(entityXml).Descendants("attribute")
            .Any(a => (string?)a.Element("LogicalName") == column && (string?)a.Element("IsSecured") == "1");

    internal static bool ProfileGrantsRead(string profilesXml, string profile, string entity, string column) =>
        XDocument.Parse(profilesXml).Descendants("FieldSecurityProfile")
            .Where(p => (string?)p.Attribute("name") == profile)
            .SelectMany(p => p.Descendants("FieldPermission"))
            .Any(f => string.Equals((string?)f.Element("EntityName"), entity, StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)f.Element("AttributeName"), column, StringComparison.OrdinalIgnoreCase)
                && (string?)f.Element("CanRead") == "4");
}
