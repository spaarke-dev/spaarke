using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Owner decision D13 (customer-provisioning-orchestration-r1 task 244, plan G16) and ADR-028 (app-only outbound on the
/// managed identity): a customer stamp is keyless. The stamp resources listed in <see cref="LocalAuthTypes"/> refuse
/// keys, Storage refuses shared keys, nothing in the template mints or emits a key, and the identities that need
/// data-plane access hold roles instead.
/// </summary>
/// <remarks>
/// <para>Reads the checked-in compile of <c>customer.bicep</c> (<c>infrastructure/bicep/customer.json</c>). CI publishes its
/// own compile of the same <c>customer.bicep</c> for H2a, so the checked-in JSON is only as current as the last
/// recompile — recompile it with every module change (procedure below). A module edit that re-enables local auth, or a
/// new module that never disabled it, then fails here rather than on a live stamp. Keys
/// came back before because nothing stopped them (plan D13, "no forcing function"); this is that forcing function on
/// the template side, and T230's one-real-MI-call-per-service is the runtime side.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the resource or expression. Fix the module — do not add an
/// exemption. A new resource of a type already listed (e.g. the Content Safety account T246 added is a
/// <c>Microsoft.CognitiveServices/accounts</c>) is checked automatically — raise its minimum count. A NEW resource TYPE that
/// supports <c>disableLocalAuth</c> belongs in <see cref="LocalAuthTypes"/>, or in the documented exclusions below with a
/// reason. Regenerate <c>customer.json</c> with <c>az bicep build --file customer.bicep --outfile customer.json</c> after
/// any module change.</para>
/// <para><b>Exclusions</b> (key-capable types in the stamp that are deliberately not in <see cref="LocalAuthTypes"/>):
/// <c>Microsoft.Insights/components</c> — telemetry ingestion uses the connection string's instrumentation key, and
/// Entra-only ingestion needs agent configuration on the App Service (not a data-access key; out of D13's scope);
/// <c>Microsoft.Communication/communicationServices</c> — the BFF already authenticates to ACS with its credential, but
/// <c>disableLocalAuth</c> is not verified on the pinned API version (2023-04-01); revisit when the module's API is
/// raised.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class CustomerStampKeylessTemplateTests
{
    /// <summary>Resource types whose key/SAS auth a stamp disables with <c>properties.disableLocalAuth</c>, with the minimum count the stamp deploys.</summary>
    private static readonly IReadOnlyDictionary<string, int> LocalAuthTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Search/searchServices"] = 1,
        ["Microsoft.CognitiveServices/accounts"] = 3, // OpenAI + Document Intelligence + Content Safety (T246)
        ["Microsoft.ServiceBus/namespaces"] = 1,
        ["Microsoft.DocumentDB/databaseAccounts"] = 1, // Cosmos — added by the owner 2026-10-06
        ["Microsoft.SignalRService/signalR"] = 0, // optional (signalrEnabled); checked whenever present
    };

    private const string SearchServiceContributorRoleId = "7ca78c08-252a-4471-8644-bb5ff32d4ba0";
    private const string SearchIndexDataReaderRoleId = "1407120a-92aa-4202-b7e9-c0e197c71c8f";
    private const string L2PrincipalExpression = "[parameters('controlPlaneUamiPrincipalId')]";

    /// <summary>Any ARM <c>list*()</c> call (listKeys, listSecrets, listCredentials, listAccountSas, …), a key-bearing
    /// connection string, or a SAS authorization rule. Scanned over the template with descriptions removed.</summary>
    private static readonly Regex KeyMaterial = new(
        @"\blist[A-Z]\w*\s*\(|AccountKey=|SharedAccessKey|/AuthorizationRules",
        RegexOptions.Compiled);

    [Fact(DisplayName = "D13/T244: local auth is disabled on AI Search, OpenAI, Document Intelligence, Service Bus, Cosmos and SignalR")]
    public void KeyedStampResources_HaveLocalAuthDisabled()
    {
        var template = CustomerTemplate();

        foreach (var (type, minimum) in LocalAuthTypes)
        {
            var count = Resources(template).Count(r => IsType(r.Resource, type));
            Assert.True(count >= minimum, $"customer.json deploys {count} '{type}' resource(s), expected at least {minimum} — the scan is no longer seeing the stamp.");
        }

        var violations = LocalAuthViolations(template);
        Assert.True(violations.Count == 0, "These stamp resources accept keys (owner D13 — set disableLocalAuth: true): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "D13/T244: the stamp Storage account is deployed with shared-key access disabled")]
    public void StampStorage_HasSharedKeyAccessDisabled()
    {
        var violations = SharedKeyViolations(CustomerTemplate(), out var storageDeployments);

        Assert.True(storageDeployments >= 1, "customer.json has no storage-account deployment — the scan is no longer seeing the stamp.");
        Assert.True(violations.Count == 0, "The stamp Storage account still accepts shared keys (owner D13): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "D13/T244: nothing in the stamp template lists, builds or emits a key or SAS")]
    public void StampTemplate_ContainsNoKeyMaterial()
    {
        var hits = KeyMaterialHits(WithoutDescriptions(CustomerTemplate()));

        Assert.True(hits.Count == 0, "customer.json still produces key material (owner D13 — remove the output / rule, use the identity): " + string.Join("; ", hits));
    }

    [Fact(DisplayName = "G16/T244: the L2 identity holds Search Service Contributor + Search Index Data Reader on the stamp search service")]
    public void L2Identity_HoldsBothSearchRoles_OnlyWhenPrincipalIsSupplied()
    {
        var grants = L2SearchRoleGrants(CustomerTemplate());

        Assert.Equal(
            new[] { SearchIndexDataReaderRoleId, SearchServiceContributorRoleId }.OrderBy(x => x, StringComparer.Ordinal),
            grants.Select(g => g.RoleId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(grants, g => Assert.Equal("[not(empty(parameters('controlPlaneUamiPrincipalId')))]", g.Condition));
    }

    [Fact(DisplayName = "D13/T244: Event Grid dead-letters with the system topic's managed identity, never with a storage key")]
    public void EventGridDeadLetter_UsesResourceIdentity()
    {
        var violations = DeadLetterViolations(CustomerTemplate(), out var subscriptions);

        Assert.True(subscriptions >= 1, "customer.json has no Event Grid subscription — the scan is no longer seeing the ACS module.");
        Assert.True(violations.Count == 0, "Event Grid dead-lettering is not keyless: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "D13/T230b: H13's ARM keyless check reads every keyed type this test pins on the template")]
    public void RuntimeArmCheck_CoversEveryKeyedTemplateType()
    {
        // The template test proves the stamp is DEPLOYED keyless by intent; H13's ArmStampKeylessVerifier proves the
        // deployed stamp still IS. A type added here and not there would be checked in CI and never on a live stamp.
        var verifier = File.ReadAllText(Path.Combine(SourceScan.RepoRoot,
            "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers", "E2EAcceptance", "ArmStampKeylessVerifier.cs"));
        var types = LocalAuthTypes.Keys.Concat(new[] { "Microsoft.Storage/storageAccounts", "Microsoft.Cache/redisEnterprise" });

        var missing = types.Where(t => !verifier.Contains($"[\"{t}\"]", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, "ArmStampKeylessVerifier.KeyedTypes does not read: " + string.Join(", ", missing));
    }

    // ---- negative controls: each rule flags the shape it exists to stop ----------------------------------------

    [Fact(DisplayName = "D13/T244: negative control — an account without disableLocalAuth, and one with it false, are flagged")]
    public void LocalAuth_NegativeControl()
    {
        var template = JsonNode.Parse("""
            { "resources": [
              { "type": "Microsoft.CognitiveServices/accounts", "name": "missing", "properties": { } },
              { "type": "Microsoft.CognitiveServices/accounts", "name": "off", "properties": { "disableLocalAuth": false } },
              { "type": "Microsoft.Search/searchServices", "name": "withAuthOptions", "properties": { "disableLocalAuth": true, "authOptions": { "apiKeyOnly": {} } } },
              { "type": "Microsoft.ServiceBus/namespaces", "name": "ok", "properties": { "disableLocalAuth": true } }
            ] }
            """)!;

        var violations = LocalAuthViolations(template);

        Assert.Equal(3, violations.Count);
        Assert.DoesNotContain(violations, v => v.Contains("'ok'", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "D13/T244: negative control — list*() calls, key-bearing connection strings and a SAS rule are flagged; descriptions are not")]
    public void KeyMaterial_NegativeControl()
    {
        var template = JsonNode.Parse("""
            { "metadata": { "description": "Never emit AccountKey= or a SharedAccessKey here." },
              "parameters": { "p": { "type": "string", "metadata": { "description": "listKeys( is banned" } } },
              "outputs": {
                "a": { "value": "[listKeys(resourceId('Microsoft.CognitiveServices/accounts', 'x'), '2024-10-01').key1]" },
                "b": { "value": "[listSecrets(resourceId('Microsoft.Web/sites/functions', 'x', 'y'), '2022-03-01').key]" },
                "c": { "value": "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=abc" },
                "d": { "value": "Endpoint=sb://x/;SharedAccessKeyName=r;SharedAccessKey=abc" },
                "e": { "value": "[reference('x').endpoint]" } },
              "resources": [ { "type": "Microsoft.ServiceBus/namespaces/AuthorizationRules", "name": "r",
                               "properties": { "description": "AccountKey= mentioned in prose" } } ] }
            """)!;

        var hits = KeyMaterialHits(WithoutDescriptions(template));

        Assert.Equal(6, hits.Count); // a, b, c, d (twice: SharedAccessKeyName + SharedAccessKey), the rule type — not the descriptions, not e
    }

    [Fact(DisplayName = "D13/T244: positive control — an existing (referenced, not deployed) resource is not judged")]
    public void ExistingResource_IsNotJudged()
    {
        var template = JsonNode.Parse("""
            { "resources": [
              { "type": "Microsoft.Search/searchServices", "name": "referenced", "existing": true },
              { "type": "Microsoft.Search/searchServices", "name": "deployed", "properties": { "disableLocalAuth": true } }
            ] }
            """)!;

        Assert.Empty(LocalAuthViolations(template));
        Assert.Single(Resources(template));
    }

    [Fact(DisplayName = "D13/T244: negative control — a dead-letter with no identity is flagged")]
    public void DeadLetter_NegativeControl()
    {
        var template = JsonNode.Parse("""
            { "resources": [
              { "type": "Microsoft.EventGrid/systemTopics/eventSubscriptions", "name": "keyed",
                "properties": { "deadLetterDestination": { "endpointType": "StorageBlob" } } },
              { "type": "Microsoft.EventGrid/systemTopics/eventSubscriptions", "name": "keyless",
                "properties": { "deadLetterWithResourceIdentity": { "identity": { "type": "SystemAssigned" }, "deadLetterDestination": { "endpointType": "StorageBlob" } } } }
            ] }
            """)!;

        var violations = DeadLetterViolations(template, out var subscriptions);

        Assert.Equal(2, subscriptions);
        Assert.Single(violations);
        Assert.Contains("'keyed'", violations[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "D13/T244: negative control — a storage deployment passing disableSharedKeyAccess=false is flagged, true is not")]
    public void SharedKey_NegativeControl()
    {
        static string Deployment(string name, string passed) => $$"""
            { "type": "Microsoft.Resources/deployments", "name": "{{name}}", "properties": {
                "parameters": { "disableSharedKeyAccess": { "value": {{passed}} } },
                "template": { "resources": [ { "type": "Microsoft.Storage/storageAccounts",
                  "properties": { "allowSharedKeyAccess": "[not(parameters('disableSharedKeyAccess'))]" } } ] } } }
            """;
        var template = JsonNode.Parse($$"""{ "resources": [ {{Deployment("keyed", "false")}}, {{Deployment("keyless", "true")}} ] }""")!;

        var violations = SharedKeyViolations(template, out var storageDeployments);

        Assert.Equal(2, storageDeployments);
        Assert.Single(violations);
        Assert.Contains("keyed", violations[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G16/T244: negative control — a Search role for another principal is not counted, and an unconditional L2 grant is visible")]
    public void L2SearchRoles_NegativeControl()
    {
        var template = JsonNode.Parse($$"""
            { "variables": { "svc": "{{SearchServiceContributorRoleId}}" },
              "resources": [
                { "type": "Microsoft.Authorization/roleAssignments", "scope": "[resourceId('Microsoft.Search/searchServices', 'x')]",
                  "properties": { "principalId": "[parameters('bffUamiPrincipalId')]",
                    "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', variables('svc'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "scope": "[resourceId('Microsoft.Search/searchServices', 'x')]",
                  "properties": { "principalId": "{{L2PrincipalExpression}}",
                    "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', variables('svc'))]" } }
              ] }
            """)!;

        var grants = L2SearchRoleGrants(template);

        var grant = Assert.Single(grants);
        Assert.Equal(SearchServiceContributorRoleId, grant.RoleId);
        Assert.Null(grant.Condition); // the real-template test requires the non-empty-principal condition
    }

    // ---- rules ------------------------------------------------------------------------------------------------

    private static List<string> LocalAuthViolations(JsonNode template)
    {
        var violations = new List<string>();
        foreach (var (resource, _) in Resources(template))
        {
            var type = LocalAuthTypes.Keys.FirstOrDefault(t => IsType(resource, t));
            if (type is null)
            {
                continue;
            }

            var properties = resource["properties"] as JsonObject;
            if (properties?["disableLocalAuth"]?.GetValueKind() != System.Text.Json.JsonValueKind.True)
            {
                violations.Add($"{type} '{Name(resource)}' — disableLocalAuth is not true");
            }

            if (IsType(resource, "Microsoft.Search/searchServices") && properties?["authOptions"] is not null)
            {
                violations.Add($"{type} '{Name(resource)}' — authOptions must be absent when local auth is disabled");
            }
        }

        return violations;
    }

    private static List<string> SharedKeyViolations(JsonNode template, out int storageDeployments)
    {
        var violations = new List<string>();
        storageDeployments = 0;
        foreach (var (deployment, _) in Resources(template).Where(r => IsType(r.Resource, "Microsoft.Resources/deployments")))
        {
            var inner = deployment["properties"]?["template"];
            var storage = inner is null ? null : Resources(inner).Select(r => r.Resource).FirstOrDefault(r => IsType(r, "Microsoft.Storage/storageAccounts"));
            if (storage is null)
            {
                continue;
            }

            storageDeployments++;
            var allow = storage["properties"]?["allowSharedKeyAccess"];
            var passed = deployment["properties"]?["parameters"]?["disableSharedKeyAccess"]?["value"];
            var literalFalse = allow?.GetValueKind() == System.Text.Json.JsonValueKind.False;
            var drivenByParameter = allow?.GetValueKind() == System.Text.Json.JsonValueKind.String
                && allow.GetValue<string>() == "[not(parameters('disableSharedKeyAccess'))]"
                && passed?.GetValueKind() == System.Text.Json.JsonValueKind.True;
            if (!literalFalse && !drivenByParameter)
            {
                violations.Add($"storage account in deployment '{Name(deployment)}' — allowSharedKeyAccess={allow?.ToString() ?? "unset"}, disableSharedKeyAccess passed={passed?.ToString() ?? "unset"}");
            }
        }

        return violations;
    }

    /// <summary>The template as text with every <c>description</c> property removed, so prose about keys is not flagged.</summary>
    private static string WithoutDescriptions(JsonNode template)
    {
        var copy = template.DeepClone();
        Strip(copy);
        return copy.ToJsonString();

        static void Strip(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove("description");
                    foreach (var child in obj.Select(p => p.Value).ToList())
                    {
                        Strip(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        Strip(child);
                    }

                    break;
            }
        }
    }

    private static List<string> KeyMaterialHits(string templateText) =>
        KeyMaterial.Matches(templateText)
            .Select(m => templateText.Substring(Math.Max(0, m.Index - 40), Math.Min(100, templateText.Length - Math.Max(0, m.Index - 40))).Trim())
            .ToList();

    private static List<(string RoleId, string? Condition)> L2SearchRoleGrants(JsonNode template)
    {
        var grants = new List<(string, string?)>();
        foreach (var (resource, variables) in Resources(template))
        {
            if (!IsType(resource, "Microsoft.Authorization/roleAssignments")
                || resource["properties"]?["principalId"]?.GetValue<string>() != L2PrincipalExpression
                || resource["scope"]?.GetValue<string>()?.Contains("Microsoft.Search/searchServices", StringComparison.Ordinal) != true)
            {
                continue;
            }

            var roleExpression = resource["properties"]?["roleDefinitionId"]?.GetValue<string>() ?? string.Empty;
            var variable = Regex.Match(roleExpression, @"variables\('([^']+)'\)").Groups[1].Value;
            var roleId = variables?[variable]?.GetValue<string>() ?? roleExpression;
            grants.Add((roleId, resource["condition"]?.GetValue<string>()));
        }

        return grants;
    }

    private static List<string> DeadLetterViolations(JsonNode template, out int subscriptions)
    {
        var violations = new List<string>();
        subscriptions = 0;
        foreach (var (resource, _) in Resources(template).Where(r => IsType(r.Resource, "Microsoft.EventGrid/systemTopics/eventSubscriptions")))
        {
            subscriptions++;
            var properties = resource["properties"];
            if (properties?["deadLetterDestination"] is not null)
            {
                violations.Add($"subscription '{Name(resource)}' uses deadLetterDestination (no identity; fails on a shared-key-disabled account)");
            }
            else if (properties?["deadLetterWithResourceIdentity"]?["identity"]?["type"] is null)
            {
                violations.Add($"subscription '{Name(resource)}' has no dead-letter identity");
            }
        }

        return violations;
    }

    // ---- template walking -------------------------------------------------------------------------------------

    /// <summary>Every resource in the template and its nested deployment templates, with the variables in scope.</summary>
    private static IEnumerable<(JsonObject Resource, JsonObject? Variables)> Resources(JsonNode template)
    {
        var variables = template["variables"] as JsonObject;
        var resources = template["resources"] switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject map => map.Select(p => p.Value).OfType<JsonObject>(), // languageVersion 2.0 symbolic names
            _ => Enumerable.Empty<JsonObject>(),
        };

        foreach (var resource in resources)
        {
            if (resource["existing"]?.GetValueKind() == System.Text.Json.JsonValueKind.True)
            {
                continue; // a reference to a resource deployed elsewhere, not one this template deploys
            }

            yield return (resource, variables);
            if (resource["properties"]?["template"] is JsonObject inner)
            {
                foreach (var nested in Resources(inner))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsType(JsonObject resource, string type) =>
        string.Equals(resource["type"]?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase);

    private static string Name(JsonObject resource) => resource["name"]?.ToString() ?? "?";

    private static string CustomerTemplatePath() =>
        Path.Combine(SourceScan.RepoRoot, "infrastructure", "bicep", "customer.json");

    private static JsonNode CustomerTemplate() =>
        JsonNode.Parse(File.ReadAllText(CustomerTemplatePath()))
        ?? throw new InvalidOperationException("customer.json did not parse.");
}
