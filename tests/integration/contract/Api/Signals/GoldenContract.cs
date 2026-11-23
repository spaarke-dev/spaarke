using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Contract.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 011 (FR-09): the golden-file harness for the signal worklist, decision-plan, suppression and
/// decision routes. One case = one file under <c>Baselines/</c> holding the HTTP status, the Content-Type and the response body
/// bytes (after the only normalisations: <see cref="VolatileFields"/>, and the generated-GUID rule below).
/// </summary>
/// <remarks>
/// <para><b>A missing golden file FAILS the test.</b> Nothing writes a file during a normal or CI run. A baseline is (re)recorded only
/// by setting <c>SPAARKE_GOLDEN_RECORD=1</c> on a developer machine; the switch is ignored when <c>CI</c>, <c>TF_BUILD</c> or
/// <c>GITHUB_ACTIONS</c> is set. Later tasks never edit a golden file to make a refactor pass (FAILURE-MODES AP-9): a REST change that
/// needs a different golden is an escalation to the owner.</para>
/// <para><b>Normalisation (ADR-019: the only values that change per run).</b> <see cref="VolatileFields"/>: the per-request trace and
/// correlation ids. Generated GUIDs (<c>generatedGuids: true</c>): a case whose fixture mints its ids with <c>Guid.NewGuid()</c> (the worklist
/// World) has every GUID replaced by <c>&lt;guid-N&gt;</c>, N the order of first appearance, so two references to one id stay two
/// references to one id. Everything else, key order and whitespace included, is compared as written.</para>
/// </remarks>
internal static class GoldenContract
{
    /// <summary>ProblemDetails members that carry a per-request id. The value is replaced by <see cref="VolatilePlaceholder"/>.</summary>
    internal static readonly string[] VolatileFields = ["correlationId", "traceId"];

    internal const string VolatilePlaceholder = "<volatile>";

    private const string RecordSwitch = "SPAARKE_GOLDEN_RECORD";

    private static readonly string[] CiMarkers = ["CI", "TF_BUILD", "GITHUB_ACTIONS"];

    private static readonly Regex GuidPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>What a route answered: status, Content-Type and the body text, before normalisation.</summary>
    internal sealed record Observed(int Status, string ContentType, string Body);

    internal static async Task<Observed> ObserveAsync(HttpResponseMessage response) =>
        new((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            await response.Content.ReadAsStringAsync());

    internal static string Normalize(string body, bool generatedGuids)
    {
        foreach (var field in VolatileFields)
        {
            body = Regex.Replace(body, "(\"" + field + "\"\\s*:\\s*)\"[^\"]*\"", "$1\"" + VolatilePlaceholder + "\"", RegexOptions.CultureInvariant);
        }

        if (!generatedGuids)
        {
            return body;
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return GuidPattern.Replace(body, m =>
        {
            if (!seen.TryGetValue(m.Value, out var n))
            {
                n = seen.Count + 1;
                seen[m.Value] = n;
            }

            return "<guid-" + n + ">";
        });
    }

    internal static string BaselineDirectory
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "worklist.gridconfiguration.json")))
                {
                    return Path.Combine(dir.FullName, "tests", "integration", "contract", "Api", "Signals", "Baselines");
                }
            }

            throw new InvalidOperationException("Repository root (config/worklist.gridconfiguration.json) not found.");
        }
    }

    private static bool Recording =>
        Environment.GetEnvironmentVariable(RecordSwitch) == "1"
        && CiMarkers.All(m => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(m)));

    /// <summary>
    /// Compares <paramref name="observed"/> with <c>Baselines/{caseName}.json</c>: status, Content-Type and the normalised body, exactly.
    /// A missing file fails (unless a developer is recording).
    /// </summary>
    internal static void AssertMatches(string caseName, Observed observed, bool generatedGuids = false)
    {
        var body = Normalize(observed.Body, generatedGuids);
        var path = Path.Combine(BaselineDirectory, caseName + ".json");

        if (Recording)
        {
            Directory.CreateDirectory(BaselineDirectory);
            File.WriteAllText(path, Envelope(caseName, observed.Status, observed.ContentType, body), new UTF8Encoding(false));
            return;
        }

        if (!File.Exists(path))
        {
            Assert.Fail($"Golden file missing: {path}. A baseline is never written by a test run. Record it on a developer machine with "
                + $"{RecordSwitch}=1 against the UNCHANGED code, review it, and commit it.");
        }

        using var golden = JsonDocument.Parse(File.ReadAllText(path));
        var root = golden.RootElement;
        var expected = new Observed(root.GetProperty("status").GetInt32(), root.GetProperty("contentType").GetString()!, root.GetProperty("body").GetString()!);

        Assert.True(
            expected.Status == observed.Status && expected.ContentType == observed.ContentType && expected.Body == body,
            $"Contract baseline '{caseName}' differs from {path}.{Environment.NewLine}"
            + $"  expected: {expected.Status} {expected.ContentType}{Environment.NewLine}{expected.Body}{Environment.NewLine}"
            + $"  actual:   {observed.Status} {observed.ContentType}{Environment.NewLine}{body}");
    }

    /// <summary>The golden file: the three compared members, plus <c>bodyPretty</c> for a reader (never compared).</summary>
    private static string Envelope(string caseName, int status, string contentType, string body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("case", caseName);
            writer.WriteNumber("status", status);
            writer.WriteString("contentType", contentType);
            writer.WriteString("body", body);
            writer.WritePropertyName("bodyPretty");
            try
            {
                using var parsed = JsonDocument.Parse(body);
                parsed.RootElement.WriteTo(writer);
            }
            catch (JsonException)
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }
}
