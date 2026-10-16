using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// An authored Action mirror (<c>infra/dataverse/actions/{code}.action.json</c> + <c>outputschemas/{code}.schema.json</c>) as the runtime
/// stores it: <c>Deploy-ActionMirrors.ps1</c> writes the JPS keys of the mirror (in this order, without <c>$comment*</c> keys) to
/// <c>sprk_systemprompt</c> and the output-schema mirror to <c>sprk_outputschemajson</c>. Shared by the Finance Action seams
/// (task 047 <c>invoice-line-code</c>, task 054 <c>invoice-codeset-match</c>; lifted out of <c>LineCodingSeamTests</c> unchanged so the
/// two seams cannot render the mirrors differently).
/// </summary>
internal static class AuthoredActionMirror
{
    private static readonly string[] JpsKeys = ["$schema", "$version", "instruction", "input", "output", "examples", "metadata"];

    public static string SystemPrompt(string actionCode)
    {
        var mirror = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json")))!.AsObject();
        var jps = new JsonObject();
        foreach (var key in JpsKeys)
        {
            if (mirror.TryGetPropertyValue(key, out var value) && value is not null)
            {
                jps[key] = StripComments(value.DeepClone());
            }
        }

        return jps.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string OutputSchema(string actionCode) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", $"{actionCode}.schema.json"));

    /// <summary>Reads the JSON value of the rendered prompt's <c>## Input</c> section.</summary>
    public static JsonElement InputSection(string prompt)
    {
        var start = prompt.IndexOf("## Input\n\n", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the operand renders through the ## Input section");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(prompt[(start + "## Input\n\n".Length)..]));
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Clone();
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }

    private static JsonNode StripComments(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(k => k.StartsWith("$comment", StringComparison.Ordinal)).ToList())
                {
                    obj.Remove(key);
                }

                foreach (var property in obj.ToList())
                {
                    if (property.Value is not null)
                    {
                        StripComments(property.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        StripComments(item);
                    }
                }

                break;
        }

        return node;
    }
}
