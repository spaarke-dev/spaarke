// -----------------------------------------------------------------------------
// CustomerOwnerRoleSet.cs
//
// #2429 (H7c) — L2's reader of the two declared customer owner roles, config/restricted-content-owner-role.json and
// config/customer-configuration-owner-role.json: the files scripts/Set-CustomerOwnerTeams.ps1 reads ($DeclaredRoleFiles,
// unified-access-control-r2 task 194b). Both are linked into this assembly as embedded resources (never copied or
// re-typed), and this type applies the script's Read-Config rules VERBATIM: schemaVersion 1; access Read at depth Basic
// only; businessUnit 'customer' only; a teamName and a roleName; at least one table; no logicalName or privilegeName
// twice; every entry carries logicalName, privilegeName, reason and evidence. Across the two files: no team, role or
// table declared twice. CustomerOwnerRoleSetParityTests pins the embedded set to what the script reads.
//
// The Secure Record Owner role is a different file and a different reader (SecureRecordOwnerRoleSet, H7b).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;

/// <summary>
/// The two declared customer owner roles (<see cref="RestrictedContent"/> and <see cref="CustomerConfiguration"/>), parsed
/// from the embedded config files.
/// </summary>
public sealed class CustomerOwnerRoleSet
{
    /// <summary>File name of the restricted-content owner role (the script's first <c>$DeclaredRoleFiles</c> entry).</summary>
    public const string RestrictedContentFile = "restricted-content-owner-role.json";

    /// <summary>File name of the customer-configuration owner role (the script's second <c>$DeclaredRoleFiles</c> entry).</summary>
    public const string CustomerConfigurationFile = "customer-configuration-owner-role.json";

    /// <summary>Manifest resource name of <see cref="RestrictedContentFile"/> (set by <c>LogicalName</c> in the csproj).</summary>
    public const string RestrictedContentResourceName = "Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams.RestrictedContentOwnerRole.json";

    /// <summary>Manifest resource name of <see cref="CustomerConfigurationFile"/> (set by <c>LogicalName</c> in the csproj).</summary>
    public const string CustomerConfigurationResourceName = "Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams.CustomerConfigurationOwnerRole.json";

    /// <summary>The only access type a file may name (the script refuses any other).</summary>
    public const string RequiredAccess = "Read";

    /// <summary>The only depth a file may name (the script refuses any other).</summary>
    public const string RequiredDepth = "Basic";

    /// <summary>The only business unit a file may name: each customer's top unit (owner round 136).</summary>
    public const string RequiredBusinessUnit = "customer";

    private static readonly Lazy<CustomerOwnerRoleSet> EmbeddedSet = new(LoadEmbedded);

    private CustomerOwnerRoleSet(CustomerOwnerRoleDefinition restrictedContent, CustomerOwnerRoleDefinition customerConfiguration)
    {
        RestrictedContent = restrictedContent;
        CustomerConfiguration = customerConfiguration;
        Definitions = [restrictedContent, customerConfiguration];
    }

    /// <summary>"Spaarke Restricted Content Owners" / "Restricted Content Owner".</summary>
    public CustomerOwnerRoleDefinition RestrictedContent { get; }

    /// <summary>"Spaarke Customer Configuration Owners" / "Customer Configuration Owner".</summary>
    public CustomerOwnerRoleDefinition CustomerConfiguration { get; }

    /// <summary>Both definitions, in the script's <c>$DeclaredRoleFiles</c> order.</summary>
    public IReadOnlyList<CustomerOwnerRoleDefinition> Definitions { get; }

    /// <summary>The set compiled into this assembly. Throws <see cref="InvalidOperationException"/> when a file is missing or invalid.</summary>
    public static CustomerOwnerRoleSet Embedded => EmbeddedSet.Value;

    /// <summary>
    /// SHA-256 (lower-case hex) over each definition's file, team, role and sorted privilege names — extending a role,
    /// renaming a team or a role changes it, so the next run re-applies.
    /// </summary>
    public string SetHash()
    {
        var lines = Definitions.SelectMany(d => new[] { d.File, d.TeamName, d.RoleName }
            .Concat(d.Tables.Select(t => t.PrivilegeName).Order(StringComparer.Ordinal)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Parses and validates both files. Throws <see cref="InvalidOperationException"/> on anything the script refuses.</summary>
    public static CustomerOwnerRoleSet Parse(string restrictedContentJson, string customerConfigurationJson)
    {
        var restricted = ParseOne(RestrictedContentFile, restrictedContentJson);
        var configuration = ParseOne(CustomerConfigurationFile, customerConfigurationJson);

        // The script's cross-file rules (Group-Object: case-insensitive): each team and role is declared once, and no table
        // is declared by two roles.
        if (string.Equals(restricted.TeamName, configuration.TeamName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Two config files declare the same teamName '{restricted.TeamName}'. Each team and role is declared once.");
        }
        if (string.Equals(restricted.RoleName, configuration.RoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Two config files declare the same roleName '{restricted.RoleName}'. Each team and role is declared once.");
        }
        var shared = restricted.Tables.Select(t => t.LogicalName)
            .Intersect(configuration.Tables.Select(t => t.LogicalName), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (shared.Length > 0)
        {
            throw new InvalidOperationException(
                $"A table is declared by two roles: {string.Join("; ", shared.Select(t => $"{t} ({RestrictedContentFile}, {CustomerConfigurationFile})"))}.");
        }

        return new CustomerOwnerRoleSet(restricted, configuration);
    }

    private static CustomerOwnerRoleDefinition ParseOne(string file, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw Invalid(file, $"it is not JSON ({ex.Message}).");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(file, "its root is not an object.");
            }

            var schemaVersion = root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var parsedVersion)
                ? parsedVersion
                : 0;
            if (schemaVersion != 1)
            {
                throw Invalid(file, $"unsupported schemaVersion '{schemaVersion}'; this reader understands 1.");
            }

            var access = Text(root, "access");
            var depth = Text(root, "depth");
            if (!string.Equals(access, RequiredAccess, StringComparison.Ordinal)
                || !string.Equals(depth, RequiredDepth, StringComparison.Ordinal))
            {
                throw Invalid(file, $"it asks for access '{access}' at depth '{depth}'. Only {RequiredAccess} at {RequiredDepth} is supported, by design.");
            }

            var businessUnit = Text(root, "businessUnit");
            if (!string.Equals(businessUnit, RequiredBusinessUnit, StringComparison.Ordinal))
            {
                throw Invalid(file, $"it names business unit '{businessUnit}'. Only '{RequiredBusinessUnit}' (each customer's top " +
                    "business unit) is supported, by design (owner round 136).");
            }

            var teamName = Text(root, "teamName");
            var roleName = Text(root, "roleName");
            if (string.IsNullOrWhiteSpace(teamName))
            {
                throw Invalid(file, "it has no 'teamName'.");
            }
            if (string.IsNullOrWhiteSpace(roleName))
            {
                throw Invalid(file, "it has no 'roleName'.");
            }

            if (!root.TryGetProperty("tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(file, "it lists no tables.");
            }

            var tables = new List<CustomerOwnerRoleTable>();
            foreach (var entry in tablesElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(file, "a 'tables' entry is not an object.");
                }
                var logicalName = Text(entry, "logicalName");
                foreach (var field in new[] { "logicalName", "privilegeName", "reason", "evidence" })
                {
                    if (string.IsNullOrWhiteSpace(Text(entry, field)))
                    {
                        throw Invalid(file, $"entry '{logicalName ?? "(no logicalName)"}' has no '{field}'.");
                    }
                }
                tables.Add(new CustomerOwnerRoleTable(logicalName!, Text(entry, "privilegeName")!));
            }

            if (tables.Count == 0)
            {
                throw Invalid(file, "it lists no tables.");
            }

            foreach (var (key, select) in new (string, Func<CustomerOwnerRoleTable, string>)[]
                     {
                         ("logicalName", t => t.LogicalName), ("privilegeName", t => t.PrivilegeName),
                     })
            {
                var duplicates = tables.GroupBy(select, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                    .Select(g => g.Key).ToArray();
                if (duplicates.Length > 0)
                {
                    throw Invalid(file, $"it lists a {key} twice: {string.Join(", ", duplicates)}.");
                }
            }

            return new CustomerOwnerRoleDefinition(file, teamName, roleName, tables);
        }
    }

    private static CustomerOwnerRoleSet LoadEmbedded()
        => Parse(ReadResource(RestrictedContentResourceName, RestrictedContentFile),
            ReadResource(CustomerConfigurationResourceName, CustomerConfigurationFile));

    private static string ReadResource(string resourceName, string file)
    {
        var assembly = typeof(CustomerOwnerRoleSet).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{resourceName}' is missing from {assembly.GetName().Name}. It is linked from " +
                $"config/{file} in Sprk.Provisioning.ControlPlane.Core.csproj; without it H7c cannot say which tables the " +
                "customer owner roles must read.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static InvalidOperationException Invalid(string file, string reason) =>
        new($"config/{file} is not a valid declared owner role: {reason}");

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>One declared owner role: its file, its memberless team, its role and the tables it reads at Basic.</summary>
/// <param name="File">The config file name (e.g. <c>restricted-content-owner-role.json</c>).</param>
/// <param name="TeamName">The memberless Owner team in each customer top unit.</param>
/// <param name="RoleName">The role, created once in the root unit; each customer unit's copy goes to that unit's team.</param>
/// <param name="Tables">Every table the role reads at Basic, in file order.</param>
public sealed record CustomerOwnerRoleDefinition(
    string File, string TeamName, string RoleName, IReadOnlyList<CustomerOwnerRoleTable> Tables);

/// <summary>One table of a declared owner role.</summary>
/// <param name="LogicalName">The table (e.g. <c>sprk_policy</c>).</param>
/// <param name="PrivilegeName">Its Read privilege's exact name from metadata (casing follows the schema name).</param>
public sealed record CustomerOwnerRoleTable(string LogicalName, string PrivilegeName);
