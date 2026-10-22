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

    private const string StorageBlobDataContributorRoleId = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";

    /// <summary>Built-in Storage Blob Data Contributor, Owner and Reader.</summary>
    private static readonly string[] BlobDataRoleIds =
    [
        StorageBlobDataContributorRoleId,
        "b7e6dc6d-f1e8-4753-8033-0f276bb0955b",
        "2a2b9908-6ea1-4ae2-8e65-a410df84e7d1",
    ];

    private const string BlobDataActionPrefix = "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/";
    private const string BlobTagsReadDataAction = "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read";

    /// <summary>Built-in Storage Blob Data Contributor's <c>actions</c>, verbatim (<c>az role definition list --name
    /// "Storage Blob Data Contributor"</c>, 2026-10-21, recorded in ebilling-r1 notes/091-storage-provisioning.md §3).</summary>
    private static readonly string[] ContributorActions =
    [
        "Microsoft.Storage/storageAccounts/blobServices/containers/delete",
        "Microsoft.Storage/storageAccounts/blobServices/containers/read",
        "Microsoft.Storage/storageAccounts/blobServices/containers/write",
        "Microsoft.Storage/storageAccounts/blobServices/generateUserDelegationKey/action",
    ];

    /// <summary>Built-in Storage Blob Data Contributor's <c>dataActions</c>, verbatim (same read).</summary>
    private static readonly string[] ContributorDataActions =
    [
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/move/action",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action",
    ];

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

    [Fact(DisplayName = "Q5 (ebilling-r1 091): every Storage Blob Data role in the stamp is assigned at container scope, never on the account")]
    public void StampBlobDataRoles_AreContainerScoped()
    {
        var violations = BlobDataRoleScopeViolations(CustomerTemplate(), out var containerGrants);

        Assert.True(containerGrants >= 1, "customer.json has no container-scoped Storage Blob Data grant — the scan is no longer seeing the stamp UAMI's grant.");
        Assert.True(violations.Count == 0, "Storage Blob Data roles must be granted per container (owner 2026-10-21, ebilling-r1 re-plan Q5): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "Q5 (ebilling-r1 091): the stamp UAMI is granted the session-file store's default container")]
    public void StampUami_IsGrantedSessionFileContainer()
    {
        var granted = CustomerTemplate()["variables"]?["bffBlobContainerNames"] as JsonArray;

        Assert.NotNull(granted);
        Assert.Contains(
            Sprk.Bff.Api.Services.Ai.Sessions.SessionFileBlobStore.DefaultContainerName,
            granted!.Select(n => n?.GetValue<string>()));
    }

    [Fact(DisplayName = "Q5 (ebilling-r1 091): negative control — an account-scope and an unscoped blob grant are flagged; a container grant and a non-storage role are not")]
    public void StampBlobDataRoles_NegativeControl()
    {
        var template = JsonNode.Parse($$"""
            { "variables": { "blob": "{{StorageBlobDataContributorRoleId}}", "kv": "4633458b-17de-408a-b874-0445c86b69e6" },
              "resources": [
                { "type": "Microsoft.Authorization/roleAssignments", "name": "account",
                  "scope": "[resourceId('Microsoft.Storage/storageAccounts', 'sa')]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', variables('blob'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "unscoped",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '{{StorageBlobDataContributorRoleId}}')]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "container",
                  "scope": "[resourceId('Microsoft.Storage/storageAccounts/blobServices/containers', 'sa', 'default', 'c')]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', variables('blob'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "keyvault",
                  "scope": "[resourceId('Microsoft.KeyVault/vaults', 'kv')]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', variables('kv'))]" } }
              ] }
            """)!;

        var violations = BlobDataRoleScopeViolations(template, out var containerGrants);

        Assert.Equal(1, containerGrants);
        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Contains("'account'", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("'unscoped'", StringComparison.Ordinal));
    }

    // ---- eBilling file store (ebilling-r1 091, option A; unified-access-control-r2 conditions 1 and 4) -----------

    [Fact(DisplayName = "091/uac-r2 c1: the file-store custom role is exactly Storage Blob Data Contributor plus blobs/tags/read")]
    public void FileStoreRole_IsExactlyContributorPlusTagRead()
    {
        var definitions = Resources(FileStoreTemplate()).Select(r => r.Resource)
            .Where(r => IsType(r, "Microsoft.Authorization/roleDefinitions")).ToList();

        var definition = Assert.Single(definitions);
        var violations = FileStoreRoleViolations(definition);
        Assert.True(violations.Count == 0, "ebilling-file-store role differs from Storage Blob Data Contributor + tags/read (uac-r2 condition 1): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "091/uac-r2 c4: the file-store role is assigned per container only, never on the account or resource group")]
    public void FileStoreRole_IsAssignedPerContainerOnly()
    {
        var violations = BlobDataRoleScopeViolations(FileStoreTemplate(), out var containerGrants);

        Assert.True(containerGrants >= 1, "ebilling-file-store.json has no container-scoped grant of its role — the scan is no longer seeing the module's assignments.");
        Assert.True(violations.Count == 0, "The file-store role must be assigned per container (uac-r2 condition 4): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "091/uac-r2 c1: must-not-fire — the exact set, in any order, is accepted")]
    public void FileStoreRole_ExactSet_IsAccepted()
    {
        var definition = RoleDefinition(
            ContributorActions.Reverse(),
            ContributorDataActions.Append(BlobTagsReadDataAction).Reverse());

        Assert.Empty(FileStoreRoleViolations(definition));
    }

    [Theory(DisplayName = "091/uac-r2 c1: must-fire — any widening, narrowing or wildcard of the file-store role is flagged")]
    [InlineData("tags-write")]
    [InlineData("tags-wildcard")]
    [InlineData("blobs-wildcard")]
    [InlineData("action-wildcard")]
    [InlineData("star")]
    [InlineData("missing-tags-read")]
    [InlineData("missing-action")]
    [InlineData("extra-action")]
    [InlineData("not-data-actions")]
    [InlineData("not-actions")]
    [InlineData("second-permission-block")]
    [InlineData("condition")]
    [InlineData("subscription-assignable")]
    [InlineData("account-assignable")]
    public void FileStoreRole_Deviation_IsFlagged(string deviation)
    {
        var dataActions = ContributorDataActions.Append(BlobTagsReadDataAction);
        var definition = deviation switch
        {
            "tags-write" => RoleDefinition(ContributorActions, dataActions.Append("Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write")),
            "tags-wildcard" => RoleDefinition(ContributorActions, ContributorDataActions.Append("Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/*")),
            "blobs-wildcard" => RoleDefinition(ContributorActions, ["Microsoft.Storage/storageAccounts/blobServices/containers/blobs/*"]),
            "action-wildcard" => RoleDefinition(ContributorActions.Append("Microsoft.Storage/storageAccounts/blobServices/*"), dataActions),
            "star" => RoleDefinition(ContributorActions, dataActions.Append("*")),
            "missing-tags-read" => RoleDefinition(ContributorActions, ContributorDataActions),
            "missing-action" => RoleDefinition(ContributorActions.Skip(1), dataActions),
            "extra-action" => RoleDefinition(ContributorActions.Append("Microsoft.Storage/storageAccounts/listKeys/action"), dataActions),
            "not-data-actions" => RoleDefinition(ContributorActions, dataActions, notDataActions: [BlobTagsReadDataAction]),
            "not-actions" => RoleDefinition(ContributorActions, dataActions, notActions: ["Microsoft.Storage/storageAccounts/blobServices/containers/delete"]),
            "second-permission-block" => RoleDefinition(ContributorActions, dataActions, extraBlock: true),
            "condition" => RoleDefinition(ContributorActions, dataActions, condition: "@Resource[x] StringEquals 'y'"),
            "subscription-assignable" => RoleDefinition(ContributorActions, dataActions, assignableScope: "[subscription().id]"),
            "account-assignable" => RoleDefinition(ContributorActions, dataActions, assignableScope: "[resourceId('Microsoft.Storage/storageAccounts', 'sa')]"),
            _ => throw new ArgumentOutOfRangeException(nameof(deviation)),
        };

        Assert.NotEmpty(FileStoreRoleViolations(definition));
    }

    [Fact(DisplayName = "091/uac-r2 c4: negative control — account-, resource-group- and deployment-scope grants of a custom blob role are flagged; a container grant and a non-blob custom role are not")]
    public void CustomBlobRoleScope_NegativeControl()
    {
        var template = JsonNode.Parse("""
            { "resources": [
                { "type": "Microsoft.Authorization/roleDefinitions", "name": "[guid(resourceGroup().id, 'file-store')]",
                  "properties": { "permissions": [ { "actions": [], "dataActions": [ "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read" ] } ] } },
                { "type": "Microsoft.Authorization/roleDefinitions", "name": "[guid(resourceGroup().id, 'other')]",
                  "properties": { "permissions": [ { "actions": [ "Microsoft.Web/sites/read" ], "dataActions": [] } ] } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "account",
                  "scope": "[resourceId('Microsoft.Storage/storageAccounts', 'sa')]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(resourceGroup().id, 'file-store'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "resourceGroup",
                  "scope": "[resourceGroup().id]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(resourceGroup().id, 'file-store'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "deployment",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(resourceGroup().id, 'file-store'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "container",
                  "scope": "[resourceId('Microsoft.Storage/storageAccounts/blobServices/containers', 'sa', 'default', 'c')]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(resourceGroup().id, 'file-store'))]" } },
                { "type": "Microsoft.Authorization/roleAssignments", "name": "web",
                  "scope": "[resourceGroup().id]",
                  "properties": { "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(resourceGroup().id, 'other'))]" } }
              ] }
            """)!;

        var violations = BlobDataRoleScopeViolations(template, out var containerGrants);

        Assert.Equal(1, containerGrants);
        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Contains("'account'", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("'resourceGroup'", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("'deployment'", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "091: the file-store module emits no key material")]
    public void FileStoreTemplate_ContainsNoKeyMaterial()
    {
        var hits = KeyMaterialHits(WithoutDescriptions(FileStoreTemplate()));

        Assert.True(hits.Count == 0, "ebilling-file-store.json produces key material: " + string.Join("; ", hits));
    }

    // ---- eBilling file store: blob soft delete (ebilling-r1 DEF-038, owner decision 2026-10-22) -----------------

    [Fact(DisplayName = "DEF-038: the file-store blob service enables blob soft delete with days = the parameter (default 30, range 1..365)")]
    public void FileStoreBlobService_EnablesSoftDeleteWithParameterDays()
    {
        var template = FileStoreTemplate();
        var blobService = Assert.Single(
            Resources(template).Select(r => r.Resource).Where(r => IsType(r, "Microsoft.Storage/storageAccounts/blobServices")));

        var violations = BlobServiceViolations(blobService);
        Assert.True(violations.Count == 0, "ebilling-file-store blob service (DEF-038): " + string.Join("; ", violations));

        var parameter = template["parameters"]?["blobSoftDeleteRetentionDays"];
        Assert.NotNull(parameter);
        Assert.Equal(30, parameter!["defaultValue"]?.GetValue<int>());
        Assert.Equal(1, parameter["minValue"]?.GetValue<int>());
        Assert.Equal(365, parameter["maxValue"]?.GetValue<int>());
    }

    [Fact(DisplayName = "DEF-038: the file-store containers deploy after the blob service (soft delete is on before the first blob)")]
    public void FileStoreContainers_DependOnBlobService()
    {
        var containers = Resources(FileStoreTemplate()).Select(r => r.Resource)
            .Where(r => IsType(r, "Microsoft.Storage/storageAccounts/blobServices/containers")).ToList();

        var container = Assert.Single(containers);
        var dependsOn = container["dependsOn"] as JsonArray ?? new JsonArray();
        Assert.Contains(dependsOn, d => d?.GetValue<string>().Contains("'Microsoft.Storage/storageAccounts/blobServices'", StringComparison.Ordinal) == true);
    }

    [Fact(DisplayName = "DEF-038: must-not-fire — the exact soft-delete-only blob service is accepted")]
    public void FileStoreBlobService_ExactShape_IsAccepted()
    {
        Assert.Empty(BlobServiceViolations(BlobService()));
    }

    [Theory(DisplayName = "DEF-038: must-fire — a disabled, literal-days, or wider blob service is flagged")]
    [InlineData("disabled")]
    [InlineData("literal-days")]
    [InlineData("missing-policy")]
    [InlineData("versioning")]
    [InlineData("change-feed")]
    [InlineData("container-soft-delete")]
    [InlineData("cors")]
    [InlineData("restore-policy")]
    [InlineData("permanent-delete")]
    public void FileStoreBlobService_Deviation_IsFlagged(string deviation)
    {
        var blobService = BlobService();
        var properties = blobService["properties"]!.AsObject();
        var policy = properties["deleteRetentionPolicy"]!.AsObject();
        switch (deviation)
        {
            case "disabled": policy["enabled"] = false; break;
            case "literal-days": policy["days"] = 30; break;
            case "missing-policy": properties.Remove("deleteRetentionPolicy"); break;
            case "versioning": properties["isVersioningEnabled"] = true; break;
            case "change-feed": properties["changeFeed"] = new JsonObject { ["enabled"] = true }; break;
            case "container-soft-delete": properties["containerDeleteRetentionPolicy"] = new JsonObject { ["enabled"] = true, ["days"] = 7 }; break;
            case "cors": properties["cors"] = new JsonObject(); break;
            case "restore-policy": properties["restorePolicy"] = new JsonObject { ["enabled"] = true, ["days"] = 7 }; break;
            case "permanent-delete": policy["allowPermanentDelete"] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(deviation));
        }

        Assert.NotEmpty(BlobServiceViolations(blobService));
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

    /// <summary>Role assignments of Storage Blob Data Contributor / Owner / Reader, or of a custom role defined in the same
    /// template with any blob data action (the eBilling file-store role), whose scope is not a single blob container
    /// (account, resource group or no scope at all).</summary>
    private static List<string> BlobDataRoleScopeViolations(JsonNode template, out int containerGrants)
    {
        var violations = new List<string>();
        containerGrants = 0;

        // A custom role is referenced by its name expression (a guid(...) call, or a literal), so match on that text.
        var customBlobRoleNames = Resources(template).Select(r => r.Resource)
            .Where(r => IsType(r, "Microsoft.Authorization/roleDefinitions")
                && (r["properties"]?["permissions"] as JsonArray ?? new JsonArray())
                    .SelectMany(p => p?["dataActions"] as JsonArray ?? new JsonArray())
                    .Any(a => a?.GetValue<string>().StartsWith(BlobDataActionPrefix, StringComparison.OrdinalIgnoreCase) == true))
            .Select(r => r["name"]?.GetValue<string>().Trim().TrimStart('[').TrimEnd(']') ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList();

        foreach (var (resource, variables) in Resources(template).Where(r => IsType(r.Resource, "Microsoft.Authorization/roleAssignments")))
        {
            var roleExpression = resource["properties"]?["roleDefinitionId"]?.GetValue<string>() ?? string.Empty;
            var variable = Regex.Match(roleExpression, @"variables\('([^']+)'\)").Groups[1].Value;
            var roleText = variable.Length > 0 ? variables?[variable]?.GetValue<string>() ?? roleExpression : roleExpression;
            if (!BlobDataRoleIds.Any(id => roleText.Contains(id, StringComparison.OrdinalIgnoreCase))
                && !customBlobRoleNames.Any(n => roleText.Contains(n, StringComparison.Ordinal)))
            {
                continue;
            }

            var scope = resource["scope"]?.GetValue<string>() ?? string.Empty;
            if (scope.Contains("Microsoft.Storage/storageAccounts/blobServices/containers", StringComparison.Ordinal))
            {
                containerGrants++;
            }
            else
            {
                violations.Add($"role assignment '{Name(resource)}' grants a Storage Blob Data role at scope '{(scope.Length == 0 ? "(deployment scope)" : scope)}'");
            }
        }

        return violations;
    }

    /// <summary>Every way <paramref name="definition"/> differs from the file-store role uac-r2 accepted: one permission
    /// block whose actions are exactly <see cref="ContributorActions"/> and whose dataActions are exactly
    /// <see cref="ContributorDataActions"/> plus <see cref="BlobTagsReadDataAction"/>; no wildcard; empty notActions and
    /// notDataActions; no condition; assignable only at the deployment's resource group.</summary>
    private static List<string> FileStoreRoleViolations(JsonObject definition)
    {
        var violations = new List<string>();
        var properties = definition["properties"];
        var permissions = properties?["permissions"] as JsonArray ?? new JsonArray();
        if (permissions.Count != 1)
        {
            violations.Add($"{permissions.Count} permission blocks (expected exactly 1)");
        }

        static string[] Values(JsonNode? node, string key) =>
            (node?[key] as JsonArray ?? new JsonArray()).Select(v => v?.GetValue<string>() ?? string.Empty).ToArray();

        void CompareSet(string key, IEnumerable<string> actual, IEnumerable<string> expected)
        {
            var actualSet = actual.ToHashSet(StringComparer.Ordinal);
            var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
            violations.AddRange(actualSet.Except(expectedSet).Select(a => $"{key} has unexpected '{a}'"));
            violations.AddRange(expectedSet.Except(actualSet).Select(e => $"{key} lacks '{e}'"));
        }

        var block = permissions.FirstOrDefault();
        var actions = permissions.SelectMany(p => Values(p, "actions")).ToList();
        var dataActions = permissions.SelectMany(p => Values(p, "dataActions")).ToList();
        CompareSet("actions", actions, ContributorActions);
        CompareSet("dataActions", dataActions, ContributorDataActions.Append(BlobTagsReadDataAction));
        violations.AddRange(actions.Concat(dataActions).Where(a => a.Contains('*', StringComparison.Ordinal)).Select(a => $"wildcard '{a}'"));
        violations.AddRange(dataActions.Where(a => a.Contains("/tags/write", StringComparison.OrdinalIgnoreCase)).Select(a => $"tag write '{a}'"));
        foreach (var key in new[] { "notActions", "notDataActions" })
        {
            var values = permissions.SelectMany(p => Values(p, key)).ToList();
            if (values.Count > 0)
            {
                violations.Add($"{key} is not empty: {string.Join(", ", values)}");
            }
        }

        if (block?["condition"] is not null || properties?["condition"] is not null)
        {
            violations.Add("has a condition");
        }

        var scopes = Values(properties, "assignableScopes");
        if (scopes.Length != 1 || scopes[0] != "[resourceGroup().id]")
        {
            violations.Add($"assignableScopes is [{string.Join(", ", scopes)}] (expected only [resourceGroup().id])");
        }

        return violations;
    }

    /// <summary>Every way the file store's blob service differs from "blob soft delete on, days = the retention parameter,
    /// and nothing else": the PUT must not write versioning, change feed, container soft delete, CORS, restore policy or
    /// permanent-delete, because an ARM PUT of a property the template does not state may reset it on the live account.</summary>
    private static List<string> BlobServiceViolations(JsonObject blobService)
    {
        var violations = new List<string>();
        var properties = blobService["properties"] as JsonObject;
        var policy = properties?["deleteRetentionPolicy"] as JsonObject;
        if (policy is null)
        {
            violations.Add("no properties.deleteRetentionPolicy");
            return violations;
        }

        if (policy["enabled"]?.GetValueKind() != System.Text.Json.JsonValueKind.True)
        {
            violations.Add("deleteRetentionPolicy.enabled is not true");
        }

        if (policy["days"]?.ToString() != "[parameters('blobSoftDeleteRetentionDays')]")
        {
            violations.Add($"deleteRetentionPolicy.days is '{policy["days"]}', not the blobSoftDeleteRetentionDays parameter");
        }

        foreach (var extra in policy.Select(p => p.Key).Where(k => k is not ("enabled" or "days")))
        {
            violations.Add($"deleteRetentionPolicy sets '{extra}'");
        }

        foreach (var extra in properties!.Select(p => p.Key).Where(k => k != "deleteRetentionPolicy"))
        {
            violations.Add($"blob service sets '{extra}' (only deleteRetentionPolicy is in scope)");
        }

        return violations;
    }

    private static JsonObject BlobService() => JsonNode.Parse("""
        { "type": "Microsoft.Storage/storageAccounts/blobServices", "name": "[format('{0}/{1}', parameters('storageAccountName'), 'default')]",
          "properties": { "deleteRetentionPolicy": { "enabled": true, "days": "[parameters('blobSoftDeleteRetentionDays')]" } } }
        """)!.AsObject();

    private static JsonObject RoleDefinition(
        IEnumerable<string> actions,
        IEnumerable<string> dataActions,
        string[]? notActions = null,
        string[]? notDataActions = null,
        bool extraBlock = false,
        string? condition = null,
        string assignableScope = "[resourceGroup().id]")
    {
        static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

        var block = new JsonObject
        {
            ["actions"] = Array(actions),
            ["notActions"] = Array(notActions ?? []),
            ["dataActions"] = Array(dataActions),
            ["notDataActions"] = Array(notDataActions ?? []),
        };
        if (condition is not null)
        {
            block["condition"] = condition;
        }

        var permissions = new JsonArray(block);
        if (extraBlock)
        {
            permissions.Add(new JsonObject { ["actions"] = new JsonArray(), ["dataActions"] = new JsonArray() });
        }

        return new JsonObject
        {
            ["type"] = "Microsoft.Authorization/roleDefinitions",
            ["name"] = "[guid(resourceGroup().id, 'file-store')]",
            ["properties"] = new JsonObject
            {
                ["type"] = "customRole",
                ["permissions"] = permissions,
                ["assignableScopes"] = Array([assignableScope]),
            },
        };
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

    /// <summary>The checked-in compile of <c>modules/ebilling-file-store.bicep</c> (not yet wired into customer.bicep —
    /// that is customer-provisioning-orchestration-r1's hand-off). Recompile with
    /// <c>az bicep build --file ebilling-file-store.bicep --outfile ebilling-file-store.json</c> after any edit.</summary>
    private static JsonNode FileStoreTemplate() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "infrastructure", "bicep", "modules", "ebilling-file-store.json")))
        ?? throw new InvalidOperationException("ebilling-file-store.json did not parse.");
}
