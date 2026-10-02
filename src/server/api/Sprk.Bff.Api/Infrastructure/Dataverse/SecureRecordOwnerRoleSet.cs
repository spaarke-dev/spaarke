using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 145 (GitHub #1046) — the codified set of tables the <c>Secure Record Owner</c> role
/// must hold Read on, at User (<c>Basic</c>) depth, so Dataverse accepts the secure owner team as the OWNER of a row
/// in each of them. Parsed from <c>config/secure-record-owner-role.json</c>, the ONE list.
/// </summary>
/// <remarks>
/// <para><b>Why it is read here.</b> Dataverse refuses an owner whose roles lack Read on the row's table ("Read
/// Privilege Check For Owner failed … is missing prvReadsprk_Todo privilege"). A child of a secure record that a
/// writer assigns to the owner team therefore fails closed until the role covers that table. The NFR-05 census
/// (<see cref="SecureBuRoleDepthAssertion"/>, clause 5) reads THIS set to name every table the role lacks, so a
/// stripped or never-granted privilege shows up on the next run instead of as a refused save.</para>
///
/// <para><b>One file, not a copy.</b> The JSON lives at the repository root and is compiled into this assembly as an
/// embedded resource LINKED from that path (<c>Sprk.Bff.Api.csproj</c>), so the scheduled census job (deployed, no
/// repository on disk) and the manual live gate (test project) read the same bytes that
/// <c>scripts/Set-SecureRecordOwnerRolePrivileges.ps1</c> and the setup guide read. There is no second list.</para>
///
/// <para><b>Validation mirrors the script.</b> <c>schemaVersion</c> 1, <c>access</c> Read and <c>depth</c> Basic
/// only, at least one table, no duplicate table, and every entry carries a logical name, a privilege name, a reason
/// and its evidence. A set that asked for anything wider would let the census grade a widened role as "covered", so
/// it is refused rather than parsed.</para>
///
/// <para><b>Placement</b> (CLAUDE.md §10/§11): a static parser beside the assertion that consumes it — no DI
/// registration, no interface, no package. It is census input, read-only, and has no reason to change apart from the
/// file's schema.</para>
/// </remarks>
public sealed class SecureRecordOwnerRoleSet
{
    /// <summary>Manifest resource name of the embedded JSON (set by <c>LogicalName</c> in the csproj).</summary>
    public const string EmbeddedResourceName = "Sprk.Bff.Api.SecureRecordOwnerRole.json";

    /// <summary>The only access type the set may name (setup guide §5.1).</summary>
    public const string RequiredAccess = "Read";

    /// <summary>The only depth the set may name (setup guide §5.1).</summary>
    public const string RequiredDepth = "Basic";

    private static readonly Lazy<SecureRecordOwnerRoleSet> EmbeddedSet = new(LoadEmbedded);

    private SecureRecordOwnerRoleSet(string roleName, string businessUnitName, IReadOnlyList<SecureRecordOwnerRoleTable> tables)
    {
        RoleName = roleName;
        BusinessUnitName = businessUnitName;
        Tables = tables;
    }

    /// <summary>The role the set is for (<c>Secure Record Owner</c>).</summary>
    public string RoleName { get; }

    /// <summary>The business unit the role lives in, as the file names it (the script's lookup key).</summary>
    public string BusinessUnitName { get; }

    /// <summary>Every table the role must cover, in file order.</summary>
    public IReadOnlyList<SecureRecordOwnerRoleTable> Tables { get; }

    /// <summary>The set compiled into this assembly from <c>config/secure-record-owner-role.json</c>.</summary>
    /// <exception cref="InvalidOperationException">The resource is missing or fails validation.</exception>
    public static SecureRecordOwnerRoleSet Embedded => EmbeddedSet.Value;

    /// <summary>Parses and validates the codified set. Throws on anything the script would refuse.</summary>
    public static SecureRecordOwnerRoleSet Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var schemaVersion = root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number
            ? version.GetInt32()
            : 0;
        if (schemaVersion != 1)
        {
            throw Invalid($"unsupported schemaVersion '{schemaVersion}'; this reader understands 1.");
        }

        var access = Text(root, "access");
        var depth = Text(root, "depth");
        if (!string.Equals(access, RequiredAccess, StringComparison.Ordinal)
            || !string.Equals(depth, RequiredDepth, StringComparison.Ordinal))
        {
            throw Invalid(
                $"it asks for access '{access}' at depth '{depth}'. Only {RequiredAccess} at {RequiredDepth} is "
                + "supported: the role's safety argument rests on it (setup guide §5.1).");
        }

        var roleName = Text(root, "roleName");
        var businessUnitName = Text(root, "businessUnitName");
        if (string.IsNullOrWhiteSpace(roleName) || string.IsNullOrWhiteSpace(businessUnitName))
        {
            throw Invalid("it names no roleName or no businessUnitName.");
        }

        if (!root.TryGetProperty("tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("it has no 'tables' array.");
        }

        var tables = new List<SecureRecordOwnerRoleTable>();
        foreach (var entry in tablesElement.EnumerateArray())
        {
            var logicalName = Text(entry, "logicalName");
            foreach (var field in new[] { "logicalName", "privilegeName", "reason", "evidence" })
            {
                if (string.IsNullOrWhiteSpace(Text(entry, field)))
                {
                    throw Invalid(
                        $"entry '{logicalName ?? "(no logicalName)"}' has no '{field}'. Every table needs its reason "
                        + "and the recorded refusal that forced it (the file's howToExtend rule).");
                }
            }

            tables.Add(new SecureRecordOwnerRoleTable(logicalName!, Text(entry, "privilegeName")!, Text(entry, "kind") ?? string.Empty));
        }

        if (tables.Count == 0)
        {
            throw Invalid("it lists no tables. An empty set would grade every role as covering it.");
        }

        var duplicates = tables
            .GroupBy(t => t.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw Invalid($"it lists a table twice: {string.Join(", ", duplicates)}.");
        }

        return new SecureRecordOwnerRoleSet(roleName, businessUnitName, tables);
    }

    private static SecureRecordOwnerRoleSet LoadEmbedded()
    {
        var assembly = typeof(SecureRecordOwnerRoleSet).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedResourceName}' is missing from {assembly.GetName().Name}. It is "
                + "linked from config/secure-record-owner-role.json in Sprk.Bff.Api.csproj; without it the census "
                + "cannot say which tables the Secure Record Owner role must cover.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static InvalidOperationException Invalid(string reason) =>
        new($"config/secure-record-owner-role.json is not a valid codified set: {reason}");

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>One table of the codified set.</summary>
/// <param name="LogicalName">The table (e.g. <c>sprk_todo</c>).</param>
/// <param name="PrivilegeName">Its Read privilege's exact name from metadata (casing follows the schema name).</param>
/// <param name="Kind"><c>root</c> (carries <c>sprk_issecure</c>) or <c>child</c> (assigned under write-path I-6).</param>
public sealed record SecureRecordOwnerRoleTable(string LogicalName, string PrivilegeName, string Kind);
