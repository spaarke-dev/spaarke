using System.Reflection;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// customer-provisioning-orchestration-r1 T256 (H7b; unified-access-control-r2 INCOMING-145 §2.1): the provisioning
/// handler that CREATES the Secure Record setup and the BFF that RELIES on it must agree on every name and list they share.
/// They are separate deployables (L2 has no reference to the BFF), so each has its own reader; this test runs both.
/// <list type="bullet">
/// <item>Both readers of <c>config/secure-record-owner-role.json</c> accept the live file with the same tables, and refuse
/// the same seeded bad documents — H7b grants exactly the set the BFF's NFR-05 census grades.</item>
/// <item>H7b's owner-team name is the BFF's compiled default, and the file's business unit is the BFF's compiled default:
/// both are fail-closed lookup keys (<c>sdap.provision.secure_owner_team_not_found</c>).</item>
/// <item>Stamps run the BFF on those compiled defaults: the canonical manifest sets no <c>SecureRecord__</c> key. If one is
/// ever added, H7b must take the same value from the same run parameter (INCOMING-145 §2.1) — this test says so.</item>
/// <item>H7b probes <c>sprk_noaccessentry</c> with exactly the BFF deny-list reader's <c>$select</c>, so "present" means
/// the BFF can read it.</item>
/// </list>
/// L2 is loaded from its build output (the BuildL2ForCosmosGuard target builds it), as CosmosProvisioningSecretGuardTests does.
/// </summary>
public class SecureRecordOwnerRoleSetParityTests
{
    private const string L2CoreAssembly = "Sprk.Provisioning.ControlPlane.Core";
    private const string L2RoleSetType = "Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup.SecureRecordOwnerRoleSet";
    private const string L2ProcedureType = "Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup.SecureRecordSetupProcedure";
    private const string L2WebApiType = "Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup.SecureRecordSetupWebApi";

    private static readonly Lazy<Assembly> L2Core = new(LoadL2Core);

    [Fact]
    public void LiveFile_BothReadersAccept_WithTheSameRoleUnitAndTables()
    {
        var json = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json"));

        var bff = SecureRecordOwnerRoleSet.Parse(json);
        var l2 = L2Parse(json);

        Assert.Equal(bff.RoleName, (string)Property(l2, "RoleName")!);
        Assert.Equal(bff.BusinessUnitName, (string)Property(l2, "BusinessUnitName")!);
        var l2Tables = ((System.Collections.IEnumerable)Property(l2, "Tables")!).Cast<object>()
            .Select(t => ((string)Property(t, "LogicalName")!, (string)Property(t, "PrivilegeName")!))
            .ToArray();
        Assert.Equal(bff.Tables.Select(t => (t.LogicalName, t.PrivilegeName)).ToArray(), l2Tables);
    }

    public static TheoryData<string, string> BadDocuments() => new()
    {
        { "schemaVersion 2", Doc(schemaVersion: "2") },
        { "access Write", Doc(access: "Write") },
        { "depth Deep", Doc(depth: "Deep") },
        { "no businessUnitName", Doc(businessUnitName: "") },
        { "no tables", Doc(tables: "") },
        { "duplicate table (case-insensitive)", Doc(tables: Entry("sprk_a") + "," + Entry("SPRK_A")) },
        { "entry without evidence", Doc(tables: "{\"logicalName\":\"sprk_a\",\"privilegeName\":\"p\",\"reason\":\"r\"}") },
        { "entry without reason", Doc(tables: "{\"logicalName\":\"sprk_a\",\"privilegeName\":\"p\",\"evidence\":\"e\"}") },
        { "tables is not an array", Doc().Replace("\"tables\":[", "\"tables\":{\"x\":[", StringComparison.Ordinal).Replace("]}", "]}}", StringComparison.Ordinal) },
    };

    [Theory]
    [MemberData(nameof(BadDocuments))]
    public void SeededBadDocument_BothReadersRefuse(string because, string json)
    {
        Assert.ThrowsAny<Exception>(() => SecureRecordOwnerRoleSet.Parse(json));
        var l2 = Record.Exception(() => L2Parse(json));
        Assert.True(l2 is not null, $"L2's reader accepted a document the BFF refuses ({because}).");
    }

    [Fact]
    public void GoodDocument_BothReadersAccept_PositiveControl()
    {
        Assert.Single(SecureRecordOwnerRoleSet.Parse(Doc()).Tables);
        Assert.NotNull(L2Parse(Doc()));
    }

    [Fact]
    public void OwnerTeamAndBusinessUnitNames_AreTheBffsCompiledDefaults()
    {
        var procedure = L2Core.Value.GetType(L2ProcedureType, throwOnError: true)!;
        Assert.Equal(SecureRecordOwnerTeam.DefaultOwnerTeamName, Const(procedure, "OwnerTeamName"));

        var json = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json"));
        Assert.Equal(SecureRecordOwnerTeam.DefaultBusinessUnitName, SecureRecordOwnerRoleSet.Parse(json).BusinessUnitName);
    }

    [Fact]
    public void Stamps_RunTheBffOnTheCompiledDefaults_NoSecureRecordOverrideInTheManifest()
    {
        var manifest = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "canonical-secret-catalog", "manifest.yaml"));
        Assert.False(manifest.Contains("SecureRecord__", StringComparison.Ordinal),
            "scripts/canonical-secret-catalog/manifest.yaml sets a SecureRecord__ key. H7b creates the business unit and team " +
            "under the BFF's COMPILED default names; a non-default value needs ONE run parameter that feeds both H4b and H7b " +
            "(INCOMING-145 §2.1).");
    }

    [Fact]
    public void NoAccessEntryProbe_SelectsExactlyTheBffReadersColumns()
    {
        var webApi = L2Core.Value.GetType(L2WebApiType, throwOnError: true)!;
        Assert.Equal(NoAccessListReader.RowSelect, Const(webApi, "NoAccessEntryRowSelect"));
    }

    // ------------------------------------------------------------ helpers

    private static object L2Parse(string json)
    {
        var type = L2Core.Value.GetType(L2RoleSetType, throwOnError: true)!;
        var parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)])!;
        try
        {
            return parse.Invoke(null, [json])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object? Property(object instance, string name)
        => instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance);

    private static string Const(Type type, string name)
        => (string)type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    private static string Entry(string table)
        => $"{{\"logicalName\":\"{table}\",\"privilegeName\":\"prvRead{table}\",\"reason\":\"r\",\"evidence\":\"e\"}}";

    private static string Doc(
        string schemaVersion = "1", string access = "Read", string depth = "Basic", string businessUnitName = "Secure Record",
        string? tables = null)
        => $"{{\"schemaVersion\":{schemaVersion},\"roleName\":\"Secure Record Owner\",\"businessUnitName\":\"{businessUnitName}\"," +
           $"\"access\":\"{access}\",\"depth\":\"{depth}\",\"tables\":[{tables ?? Entry("sprk_a")}]}}";

    private static Assembly LoadL2Core()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == L2CoreAssembly);
        if (loaded is not null)
        {
            return loaded;
        }

        var bin = Path.Combine(SourceScan.RepoRoot, "src", "server", "services", L2CoreAssembly, "bin");
        var candidate = Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, $"{L2CoreAssembly}.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        return candidate is not null
            ? Assembly.LoadFrom(candidate)
            : throw new FileNotFoundException(
                $"{L2CoreAssembly}.dll is not built under '{bin}'. The BuildL2ForCosmosGuard target builds it; run " +
                $"dotnet build src/server/services/{L2CoreAssembly}/ if this test runs outside that target.");
    }
}
