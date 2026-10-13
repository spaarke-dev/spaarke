// -----------------------------------------------------------------------------
// ControlPlaneSecretFreeTemplateTests.cs
//
// customer-provisioning-orchestration-r1 task 252 (owner-approved 2026-10-09): the L2 control plane carries no Key
// Vault reference that its configured credential chain does not read. Reads the checked-in compile of
// platform-controlplane.bicep (infrastructure/bicep/platform-controlplane.json) and the dev parameter file:
//   - the Worker is secret-free by default (requireSecretFreeIdentity defaults to true at both template levels and is
//     passed through) and dev states it explicitly;
//   - the only BFF-API-ClientSecret references sit in the legacy (prong-3 opt-in) app-setting set, selected only when
//     requireSecretFreeIdentity is false;
//   - the secret-free set is exactly the chain settings WorkerSecretFreeBootTests boots the real Worker with;
//   - nothing references Dataverse-ClientSecret, and no sentinel value appears anywhere.
// A Bicep edit that re-adds a reference, flips the default or changes the chain settings fails here, before a deploy.
// Recompile after any module change: az bicep build --file platform-controlplane.bicep --outfile platform-controlplane.json
//
// §11: Existing — CustomerStampKeylessTemplateTests (ArchTests) pins customer.json, not the control-plane template;
// WorkerSecretFreeBootTests proves the Worker boots on the chain but not that the template emits it. Extension — this
// is the template-side half of that boot test, kept beside it so the shared settings list has one home. Cost of doing
// nothing — the template could silently reintroduce a reference to a secret the binding rule forbids creating
// (provisioning.md "KV credential lifecycle" rule 1), which is how the old seeding sentinel came about.
// MAINTAIN-class structural fitness test (tests/CLAUDE.md).
// -----------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class ControlPlaneSecretFreeTemplateTests
{
    private const string SecretFreeSelector =
        "if(parameters('requireSecretFreeIdentity'), variables('secretFreeCredentialAppSettings'), variables('legacyClientSecretAppSettings'))";

    private static readonly Regex KeyVaultReference = new(@"@Microsoft\.KeyVault\(", RegexOptions.Compiled);

    [Fact(DisplayName = "T252: the control plane is secret-free by default and the dev parameter file says so explicitly")]
    public void RequireSecretFreeIdentity_DefaultsTrue_AndDevSetsTrue()
    {
        var template = Template();

        ((bool?)template["parameters"]!["requireSecretFreeIdentity"]!["defaultValue"]).Should().BeTrue(
            "a new control-plane environment must not get Key Vault references to BFF-API-ClientSecret");

        var worker = WorkerModule(template);
        ((bool?)worker["properties"]!["template"]!["parameters"]!["requireSecretFreeIdentity"]!["defaultValue"])
            .Should().BeTrue();
        ((string?)worker["properties"]!["parameters"]!["requireSecretFreeIdentity"]!["value"])
            .Should().Be("[parameters('requireSecretFreeIdentity')]", "the top-level flag must reach the Worker module");

        var devParams = File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "parameters", "platform-controlplane-dev.bicepparam"));
        Regex.IsMatch(devParams, @"^param requireSecretFreeIdentity = true\s*$", RegexOptions.Multiline)
            .Should().BeTrue("dev runs the secret-free chain (T252) and must not depend on the template default");
        Regex.IsMatch(devParams, @"^param requireSecretFreeIdentity = false", RegexOptions.Multiline).Should().BeFalse();
    }

    [Fact(DisplayName = "T252: BFF-API-ClientSecret is referenced only by the legacy set, selected only when the chain is not secret-free")]
    public void BffApiClientSecretReferences_LiveOnlyInTheLegacySet()
    {
        var template = Template();
        var worker = WorkerModule(template);
        var variables = worker["properties"]!["template"]!["variables"]!;

        // String values, not ToJsonString(): the serializer escapes the apostrophes in ARM expressions.
        var legacy = StringValues(variables["legacyClientSecretAppSettings"]!);
        var legacyRefs = legacy.Count(s => KeyVaultReference.IsMatch(s));
        legacyRefs.Should().Be(2, "the legacy set holds EnvVarValues__ClientSecret and SolutionImportOptions__ClientSecret");
        legacy.Count(s => s.Contains("parameters('bffApiClientSecretName')", StringComparison.Ordinal)).Should().Be(2);

        StringValues(template).Count(s => KeyVaultReference.IsMatch(s)
                && s.Contains("parameters('bffApiClientSecretName')", StringComparison.Ordinal))
            .Should().Be(legacyRefs, "no Key Vault reference outside the legacy set may name the BFF secret");

        var appSettings = (string?)FindProperty(worker, "appSettings") ?? string.Empty;
        appSettings.Should().Contain(SecretFreeSelector,
            "the legacy set must be appended only when requireSecretFreeIdentity is false");
        KeyVaultReference.Matches(appSettings).Count.Should().Be(1,
            "the only Key Vault reference outside the two sets is ExchangeSidecar__SharedSecret");
        appSettings.Should().Contain(
            "format('@Microsoft.KeyVault(VaultName={0};SecretName={1})', parameters('keyVaultName'), parameters('sidecarSharedSecretKvSecretName'))");
    }

    [Fact(DisplayName = "T252: the secret-free set is exactly the chain the Worker boot tests start with, and holds no Key Vault reference")]
    public void SecretFreeSet_MatchesTheBootTestFixture()
    {
        var variables = WorkerModule(Template())["properties"]!["template"]!["variables"]!;
        var emitted = variables["secretFreeCredentialAppSettings"]!.AsArray()
            .Select(s => new KeyValuePair<string, string>((string)s!["name"]!, (string)s["value"]!))
            .ToList();

        emitted.Should().Equal(SecretFreeWorkerTestFactory.SecretFreeChainAppSettings);
        emitted.Should().NotContain(s => s.Key.EndsWith("__ClientSecret", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "T252: nothing in the control-plane template references Dataverse-ClientSecret or carries a sentinel")]
    public void NoDataverseClientSecretReference_AndNoSentinel()
    {
        var template = Template();

        StringValues(template).Should().NotContain(s => s.Contains("Dataverse-ClientSecret", StringComparison.OrdinalIgnoreCase),
            "the Api lost its last reference in FR-38 and the Worker never had one");
        ((JsonObject)template["parameters"]!).ContainsKey("dataverseClientSecretName").Should().BeFalse();
        template.ToJsonString().Should().NotContain("pending-oob-population",
            "a sentinel in a credential slot fails opaquely with AADSTS7000215 (auth-v4 §9.1)");
    }

    // ---------- helpers ----------

    private static JsonNode Template()
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "platform-controlplane.json")))!;

    /// <summary>The nested deployment of modules/controlplane-worker-app-service.bicep (the one declaring the two credential sets).</summary>
    private static JsonNode WorkerModule(JsonNode template)
    {
        var modules = template["resources"]!.AsArray()
            .Where(r => r?["properties"]?["template"]?["variables"]?["secretFreeCredentialAppSettings"] is not null)
            .ToList();
        modules.Should().HaveCount(1, "exactly one module (the Worker) declares the credential sets");
        return modules[0]!;
    }

    /// <summary>Every string value in the template, excluding <c>metadata</c> blocks (parameter descriptions).</summary>
    private static List<string> StringValues(JsonNode node)
    {
        var values = new List<string>();
        Walk(node, values);
        return values;

        static void Walk(JsonNode? current, List<string> acc)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj)
                    {
                        if (key == "metadata") continue;
                        Walk(child, acc);
                    }
                    break;
                case JsonArray arr:
                    foreach (var child in arr) Walk(child, acc);
                    break;
                case JsonValue value when value.TryGetValue<string>(out var s):
                    acc.Add(s);
                    break;
            }
        }
    }

    private static JsonNode? FindProperty(JsonNode? node, string name)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    if (key == name) return child;
                    var found = FindProperty(child, name);
                    if (found is not null) return found;
                }
                break;
            case JsonArray arr:
                foreach (var child in arr)
                {
                    var found = FindProperty(child, name);
                    if (found is not null) return found;
                }
                break;
        }
        return null;
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ArmTemplateInspectorTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
