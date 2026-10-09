// -----------------------------------------------------------------------------
// SecureRecordOwnerRoleSet.cs
//
// T256 (H7b) — L2's reader of config/secure-record-owner-role.json, the ONE list of tables the "Secure Record Owner"
// role reads at User (Basic) depth (unified-access-control-r2 task 145; INCOMING-145 §2.1).
//
// WHY A SECOND READER: the file is linked into this assembly as an embedded resource (never copied), but L2 has no
// project reference to the BFF, whose SecureRecordOwnerRoleSet parses the same file for the NFR-05 census. This type
// applies the SAME validation rules (schemaVersion 1; access Read and depth Basic only; at least one table; no
// duplicate table; every entry carries logicalName, privilegeName, reason and evidence). Spaarke.ArchTests
// SecureRecordOwnerRoleSetParityTests runs BOTH readers over the live file and the same seeded bad documents, so the
// two cannot drift apart: H7b grants exactly the set the census grades.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>
/// The codified set of tables the <c>Secure Record Owner</c> role holds Read on, at <c>Basic</c> depth, parsed from the
/// embedded <c>config/secure-record-owner-role.json</c>.
/// </summary>
public sealed class SecureRecordOwnerRoleSet
{
    /// <summary>Manifest resource name of the embedded JSON (set by <c>LogicalName</c> in the csproj).</summary>
    public const string EmbeddedResourceName = "Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup.SecureRecordOwnerRole.json";

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

    /// <summary>The business unit the role lives in (<c>Secure Record</c>) — the BFF's fail-closed lookup key.</summary>
    public string BusinessUnitName { get; }

    /// <summary>Every table the role must cover, in file order.</summary>
    public IReadOnlyList<SecureRecordOwnerRoleTable> Tables { get; }

    /// <summary>The set compiled into this assembly. Throws <see cref="InvalidOperationException"/> when it is missing or invalid.</summary>
    public static SecureRecordOwnerRoleSet Embedded => EmbeddedSet.Value;

    /// <summary>
    /// SHA-256 (lower-case hex) of the set's privilege names, sorted ordinally and joined by a line feed — INCOMING-145
    /// §2.3's <c>setHash</c>. Extending the set changes it, so the next run re-applies.
    /// </summary>
    public string SetHash()
    {
        var names = Tables.Select(t => t.PrivilegeName).Order(StringComparer.Ordinal);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', names)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Parses and validates the codified set. Throws <see cref="InvalidOperationException"/> on anything the script would refuse.</summary>
    public static SecureRecordOwnerRoleSet Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw Invalid($"it is not JSON ({ex.Message}).");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("its root is not an object.");
            }

            var schemaVersion = root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var parsedVersion)
                ? parsedVersion
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
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("a 'tables' entry is not an object.");
                }

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
    }

    private static SecureRecordOwnerRoleSet LoadEmbedded()
    {
        var assembly = typeof(SecureRecordOwnerRoleSet).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedResourceName}' is missing from {assembly.GetName().Name}. It is linked "
                + "from config/secure-record-owner-role.json in Sprk.Provisioning.ControlPlane.Core.csproj; without it H7b "
                + "cannot say which tables the Secure Record Owner role must read.");
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
/// <param name="Kind"><c>root</c> (carries <c>sprk_issecure</c>) or <c>child</c>.</param>
public sealed record SecureRecordOwnerRoleTable(string LogicalName, string PrivilegeName, string Kind);
