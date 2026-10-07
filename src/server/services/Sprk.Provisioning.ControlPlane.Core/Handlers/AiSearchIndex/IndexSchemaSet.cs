// -----------------------------------------------------------------------------
// IndexSchemaSet.cs
//
// The AI Search index schemas H2b applies: the embedded IndexSchemas/*.json
// resources (one per canonical index), how each is loaded (comment keys
// stripped — the exact body PUT to the service), and — task 245b — the
// content version of a set of them, which is the `indexVer` in H2b's
// idempotency key aisearch-{customerId}-{indexVer}.
//
// Before T245b H2b read `indexVer` from a run parameter nothing wrote, so every
// real run failed H2b. The version is now computed from the bodies H2b would
// PUT: the same schemas give the same version (a resume is a no-op); an edited
// schema gives a new one (the handler re-applies). Comment keys are stripped
// before hashing because they are stripped before the PUT — a comment-only edit
// changes nothing the service sees.
//
// Moved here from SearchIndexClientProvisioner (task 245b) so the handler and
// the provisioner read the SAME schema bodies through one owner.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>The embedded AI Search index schemas, their PUT bodies, and their content version (task 245b).</summary>
public static partial class IndexSchemaSet
{
    private const string SchemaResourcePrefix =
        "Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex.IndexSchemas.";

    /// <summary>
    /// Canonical index name -&gt; embedded resource file name. MUST stay in sync with
    /// infrastructure/ai-search/*.json (task 002 audit § 1 catalog authority) and with
    /// <see cref="CanonicalIndexCatalog.CanonicalIndexNames"/>.
    /// </summary>
    internal static readonly ImmutableDictionary<string, string> SchemaResourceNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["spaarke-files-index"] = "spaarke-files-index.json",
            ["spaarke-discovery-index"] = "spaarke-discovery-index.json",
            ["spaarke-records-index"] = "spaarke-records-index.json",
            ["spaarke-rag-references"] = "spaarke-rag-references.json",
            ["spaarke-insights-index"] = "spaarke-insights-index.json",
            ["spaarke-session-files"] = "spaarke-session-files.json",
            ["spaarke-invoices-index"] = "spaarke-invoices-index.json",
        }.ToImmutableDictionary();

    // Ports Deploy-AllIndexes.ps1's Remove-JsonCommentKeys regexes verbatim
    // (two comment-key conventions: `"// rationale": "..."` on most schemas;
    // `"_comment_": "..."` on spaarke-insights-index.json).
    [GeneratedRegex("\"//[^\"]*\"\\s*:\\s*\"[^\"]*\"\\s*,?\\s*")]
    private static partial Regex SlashCommentKeyPattern();

    [GeneratedRegex("\"_comment_\"\\s*:\\s*\"[^\"]*\"\\s*,?\\s*")]
    private static partial Regex UnderscoreCommentKeyPattern();

    /// <summary>
    /// The PUT body for <paramref name="indexName"/>, or <c>false</c> when no schema is embedded for
    /// that name.
    /// </summary>
    public static bool TryGetSchemaBody(string indexName, out string body)
    {
        body = string.Empty;
        if (!SchemaResourceNames.TryGetValue(indexName, out var resourceFileName))
        {
            return false;
        }
        body = LoadAndStripSchema(resourceFileName);
        return true;
    }

    /// <summary>
    /// The content version of the schema set for <paramref name="indexNames"/> — the <c>indexVer</c>
    /// of <c>aisearch-{customerId}-{indexVer}</c>. Returns <c>false</c> (with the first name that has
    /// no embedded schema) when any requested index is unknown.
    /// </summary>
    public static bool TryComputeVersion(IEnumerable<string> indexNames, out string version, out string? unknownIndexName)
    {
        ArgumentNullException.ThrowIfNull(indexNames);
        version = string.Empty;
        unknownIndexName = null;
        var bodies = new List<(string Name, string Body)>();
        foreach (var name in indexNames)
        {
            if (!TryGetSchemaBody(name, out var body))
            {
                unknownIndexName = name;
                return false;
            }
            bodies.Add((name, body));
        }
        version = ComputeVersion(bodies);
        return true;
    }

    /// <summary>
    /// Version of a set of (index name, PUT body) pairs — order-independent (sorted by name, ordinal),
    /// and each body is length-prefixed so no two different sets serialise to the same bytes.
    /// </summary>
    internal static string ComputeVersion(IEnumerable<(string Name, string Body)> schemas)
    {
        var canonical = new StringBuilder();
        foreach (var (name, body) in schemas.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            canonical.Append(name).Append('\n').Append(body.Length).Append('\n').Append(body).Append('\n');
        }
        return ArtifactVersion.Of(canonical.ToString());
    }

    /// <summary>
    /// Reads the embedded schema resource + strips comment keys. Throws
    /// <see cref="InvalidOperationException"/> when the resource is missing (a csproj drift).
    /// </summary>
    internal static string LoadAndStripSchema(string resourceFileName)
    {
        var resourceName = SchemaResourcePrefix + resourceFileName;
        var assembly = typeof(IndexSchemaSet).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Verify Sprk.Provisioning.ControlPlane.Core.csproj's " +
                "<EmbeddedResource> glob covers Handlers/AiSearchIndex/IndexSchemas/*.json with the matching <LogicalName>.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = reader.ReadToEnd();
        var stripped = SlashCommentKeyPattern().Replace(raw, string.Empty);
        return UnderscoreCommentKeyPattern().Replace(stripped, string.Empty);
    }
}
