// -----------------------------------------------------------------------------
// ArmStampKeylessVerifier.cs — task 230b (owner D13)
//
// Production IStampKeylessVerifier. Reads the DEPLOYED stamp through ARM REST as the L2 Worker identity (Owner on the
// customer subscription, T228):
//   1. lists the stamp resource group's resources;
//   2. for each keyed resource type GETs the resource and checks the property that disables key auth —
//      disableLocalAuth on AI Search (which must also carry no authOptions), Cognitive Services accounts (OpenAI,
//      Document Intelligence, Content Safety), Service Bus, Cosmos DB and SignalR; allowSharedKeyAccess=false on
//      Storage; accessKeysAuthentication=Disabled on every Azure Managed Redis database. The set mirrors
//      CustomerStampKeylessTemplateTests (its exclusions — Application Insights ingestion and ACS — too); a parity test
//      in that file reads THIS file so the two cannot drift;
//   3. lists the app settings and connection strings of every App Service in the group (the BFF always included) and of
//      every slot, and refuses any key
//      setting (StampKeySettingCatalog — the settings the BFF would use a key from; pinned against the BFF's
//      key-credential sites by tests/Spaarke.ArchTests/KeyCredentialCensusTests) and any value shaped like a key.
// Violations name resources and settings only — never a value.
//
// Raw REST with explicit api-versions (those the stamp modules deploy) rather than typed SDK resources: three of the
// seven types (Search, Redis Enterprise, SignalR) have no ARM SDK package in this project, and one uniform read keeps
// the check small. Tests use a hand-rolled fake HttpMessageHandler (never Mock<HttpMessageHandler>).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IStampKeylessVerifier"/>
public sealed class ArmStampKeylessVerifier : IStampKeylessVerifier
{
    /// <summary>Named HttpClient registered by <see cref="E2EAcceptanceModule"/>.</summary>
    public const string HttpClientName = "H13-StampKeyless";

    internal const string ArmBase = "https://management.azure.com";
    private const string ResourcesApiVersion = "2021-04-01";
    private const string WebApiVersion = "2023-01-01";
    private const string RedisEnterpriseApiVersion = "2025-07-01";

    /// <summary>A keyed resource type: the api-version to read it with, the minimum count a stamp deploys, and the rule.</summary>
    private sealed record KeyedType(string ApiVersion, int Minimum, Func<JsonElement, string?> Violation);

    /// <summary>The keyed resource types of a stamp (keys compared case-insensitively, as ARM types are).</summary>
    private static readonly IReadOnlyDictionary<string, KeyedType> KeyedTypes = new Dictionary<string, KeyedType>(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Search/searchServices"] = new("2023-11-01", 1, p =>
            !IsTrue(p, "disableLocalAuth") ? "disableLocalAuth is not true"
            : p.TryGetProperty("authOptions", out var auth) && auth.ValueKind is not JsonValueKind.Null ? "authOptions is set (keys accepted)"
            : null),
        ["Microsoft.CognitiveServices/accounts"] = new("2024-10-01", 3, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.ServiceBus/namespaces"] = new("2022-10-01-preview", 1, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.DocumentDB/databaseAccounts"] = new("2024-05-15", 1, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.SignalRService/signalR"] = new("2024-03-01", 0, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.Storage/storageAccounts"] = new("2023-01-01", 1, p =>
            p.TryGetProperty("allowSharedKeyAccess", out var v) && v.ValueKind == JsonValueKind.False ? null : "allowSharedKeyAccess is not false"),
        // Redis Enterprise keys live on the DATABASE (accessKeysAuthentication) — read through the cluster.
        ["Microsoft.Cache/redisEnterprise"] = new(RedisEnterpriseApiVersion, 1, _ => null),
        // Not in today's stamp, but key-capable: checked whenever present so one added later cannot slip through.
        ["Microsoft.Cache/redis"] = new("2024-03-01", 0, p => IsTrue(p, "disableAccessKeyAuthentication") ? null : "disableAccessKeyAuthentication is not true"),
        ["Microsoft.EventHub/namespaces"] = new("2024-01-01", 0, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.AppConfiguration/configurationStores"] = new("2023-03-01", 0, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
        ["Microsoft.EventGrid/topics"] = new("2022-06-15", 0, p => IsTrue(p, "disableLocalAuth") ? null : "disableLocalAuth is not true"),
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ILogger<ArmStampKeylessVerifier> _logger;

    public ArmStampKeylessVerifier(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        ILogger<ArmStampKeylessVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<StampKeylessOutcome> VerifyAsync(StampKeylessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { ArmBase + "/.default" }), cancellationToken).ConfigureAwait(false);
            var arm = new ArmReader(_httpClientFactory.CreateClient(HttpClientName), token.Token);

            var violations = new List<string>();
            var checkedItems = new List<string>();

            // (1) + (2) keyed resources.
            var groupId = $"/subscriptions/{request.SubscriptionId}/resourceGroups/{request.ResourceGroupName}";
            var resources = await arm.ListAsync($"{groupId}/resources?api-version={ResourcesApiVersion}", cancellationToken).ConfigureAwait(false);
            var sites = resources
                .Where(r => string.Equals(r.GetProperty("type").GetString(), "Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.GetProperty("name").GetString() ?? string.Empty)
                .Append(request.AppServiceName)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var counts = KeyedTypes.Keys.ToDictionary(k => k, _ => 0, StringComparer.OrdinalIgnoreCase);

            foreach (var resource in resources)
            {
                var type = resource.GetProperty("type").GetString() ?? string.Empty;
                if (!KeyedTypes.TryGetValue(type, out var rule))
                {
                    continue;
                }
                var id = resource.GetProperty("id").GetString()!;
                var name = resource.GetProperty("name").GetString() ?? id;
                counts[type]++;

                if (type.Equals("Microsoft.Cache/redisEnterprise", StringComparison.OrdinalIgnoreCase))
                {
                    var databases = await arm.ListAsync($"{id}/databases?api-version={RedisEnterpriseApiVersion}", cancellationToken).ConfigureAwait(false);
                    if (databases.Count == 0)
                    {
                        violations.Add($"{type} '{name}': no database found");
                    }
                    foreach (var db in databases)
                    {
                        var dbName = db.GetProperty("name").GetString();
                        var keys = db.TryGetProperty("properties", out var dp) && dp.TryGetProperty("accessKeysAuthentication", out var a) ? a.GetString() : null;
                        if (!string.Equals(keys, "Disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add($"{type} '{name}/{dbName}': accessKeysAuthentication is '{keys ?? "(absent)"}', not Disabled");
                        }
                    }
                    checkedItems.Add(name);
                    continue;
                }

                var body = await arm.GetAsync($"{id}?api-version={rule.ApiVersion}", cancellationToken).ConfigureAwait(false);
                var properties = body.TryGetProperty("properties", out var p) ? p : default;
                var violation = properties.ValueKind == JsonValueKind.Object ? rule.Violation(properties) : "no properties returned";
                if (violation is not null)
                {
                    violations.Add($"{type} '{name}': {violation}");
                }
                checkedItems.Add(name);
            }

            foreach (var (type, rule) in KeyedTypes)
            {
                if (counts[type] < rule.Minimum)
                {
                    violations.Add($"{type}: {counts[type]} found in '{request.ResourceGroupName}', the stamp deploys at least {rule.Minimum}");
                }
            }

            // (3) App Service settings — every site in the stamp group (the BFF included), production and every slot.
            foreach (var site in sites)
            {
                var siteId = $"{groupId}/providers/Microsoft.Web/sites/{site}";
                var slotScopes = new List<(string Label, string Id)> { ("production", siteId) };
                IReadOnlyList<JsonElement> slots;
                try
                {
                    slots = await arm.ListAsync($"{siteId}/slots?api-version={WebApiVersion}", cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                                                      && !string.Equals(site, request.AppServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    // L2 holds Website Contributor on the BFF only (customer.bicep): another site's settings cannot be read,
                    // so the stamp cannot be shown keyless — fail closed and name it.
                    violations.Add($"App Service '{site}': its settings cannot be read by the L2 identity (403) — the stamp deploys one site; remove it or grant L2 access");
                    continue;
                }
                foreach (var slot in slots)
                {
                    var full = slot.GetProperty("name").GetString() ?? string.Empty; // "{site}/{slot}"
                    var slotName = full.Contains('/') ? full[(full.LastIndexOf('/') + 1)..] : full;
                    slotScopes.Add((slotName, $"{siteId}/slots/{slotName}"));
                }

                foreach (var (label, scopeId) in slotScopes)
                {
                    var settings = await arm.PostAsync($"{scopeId}/config/appsettings/list?api-version={WebApiVersion}", cancellationToken).ConfigureAwait(false);
                    foreach (var name in StampKeySettingCatalog.KeyBearingSettingNames(PropertyPairs(settings, connectionStrings: false)))
                    {
                        violations.Add($"App Service '{site}' slot '{label}': app setting '{name}' is a key");
                    }
                    var connections = await arm.PostAsync($"{scopeId}/config/connectionstrings/list?api-version={WebApiVersion}", cancellationToken).ConfigureAwait(false);
                    foreach (var name in StampKeySettingCatalog.KeyBearingSettingNames(PropertyPairs(connections, connectionStrings: true)))
                    {
                        violations.Add($"App Service '{site}' slot '{label}': connection string '{name}' is a key");
                    }
                    checkedItems.Add($"{site}[{label}]");
                }
            }

            if (violations.Count > 0)
            {
                _logger.LogWarning(
                    "H13 keyless (ARM) FAILED: customerId={CustomerId} runId={RunId} violations={Violations}",
                    request.CustomerId, request.RunId, string.Join(" | ", violations));
                return new StampKeylessOutcome.Failed(violations);
            }

            _logger.LogInformation(
                "H13 keyless (ARM) passed: customerId={CustomerId} runId={RunId} checked={Checked}",
                request.CustomerId, request.RunId, string.Join(", ", checkedItems));
            return new StampKeylessOutcome.Passed(checkedItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StampKeylessOutcome.InfraFault(
                $"ARM read of '{request.ResourceGroupName}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsTrue(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>(name, value) pairs of an appsettings/connectionstrings list response — values stay in memory only.</summary>
    private static IEnumerable<(string Name, string? Value)> PropertyPairs(JsonElement body, bool connectionStrings)
    {
        if (!body.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        foreach (var p in props.EnumerateObject())
        {
            var value = connectionStrings
                ? (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("value", out var v) ? v.GetString() : null)
                : (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null);
            yield return (p.Name, value);
        }
    }

    /// <summary>Minimal ARM REST reader; any non-success status throws (→ InfraFault).</summary>
    private sealed class ArmReader(HttpClient client, string token)
    {
        public async Task<JsonElement> GetAsync(string relative, CancellationToken ct) => await SendAsync(HttpMethod.Get, ArmBase + relative, ct).ConfigureAwait(false);

        public async Task<JsonElement> PostAsync(string relative, CancellationToken ct) => await SendAsync(HttpMethod.Post, ArmBase + relative, ct).ConfigureAwait(false);

        public async Task<IReadOnlyList<JsonElement>> ListAsync(string relative, CancellationToken ct)
        {
            var items = new List<JsonElement>();
            string? next = ArmBase + relative;
            while (next is not null)
            {
                var page = await SendAsync(HttpMethod.Get, next, ct).ConfigureAwait(false);
                if (page.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(value.EnumerateArray());
                }
                next = page.TryGetProperty("nextLink", out var link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null;
                // The ARM bearer token follows nextLink — only ever to ARM itself (https, the ARM host, default port).
                if (next is not null
                    && !(Uri.TryCreate(next, UriKind.Absolute, out var link2)
                         && link2.Scheme == Uri.UriSchemeHttps
                         && string.Equals(link2.Host, "management.azure.com", StringComparison.OrdinalIgnoreCase)
                         && link2.IsDefaultPort))
                {
                    throw new InvalidOperationException("ARM returned a nextLink outside management.azure.com; not followed.");
                }
            }
            return items;
        }

        private async Task<JsonElement> SendAsync(HttpMethod method, string uri, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"ARM {method} {new Uri(uri).AbsolutePath} returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }
}

/// <summary>
/// The App Service settings from which the customer's BFF would use a KEY, in app-setting form (<c>__</c>). Each is the
/// config key of a key-credential site in the BFF (tests/Spaarke.ArchTests/KeyCredentialCensusTests pins the two lists
/// together), plus the connection-string forms. A stamp must carry none of them, on any slot (owner D13, task 230b).
/// </summary>
public static class StampKeySettingCatalog
{
    /// <summary>Setting names (compared case-insensitively, as App Service treats them).</summary>
    public static readonly IReadOnlyList<string> SettingNames = new[]
    {
        "AzureOpenAI__ApiKey",
        "DocumentIntelligence__OpenAiKey",
        "DocumentIntelligence__DocIntelKey",
        "DocumentIntelligence__AiSearchKey",
        "AiSearch__ReferencesApiKey",
        "RecordSync__AiSearchApiKey",
        "AiSafety__ContentSafety__ApiKey",
        "ServiceBus__ConnectionString",
        "ConnectionStrings__ServiceBus",
        "Redis__ConnectionString",
        "ConnectionStrings__Redis",
    };

    /// <summary>Connection-string names (App Service "Connection strings" section) that carry a key.</summary>
    public static readonly IReadOnlyList<string> ConnectionStringNames = new[] { "ServiceBus", "Redis" };

    /// <summary>Fragments that make any value a key, whatever its name.</summary>
    public static readonly IReadOnlyList<string> KeyValueMarkers = new[]
    {
        "AccountKey=", "SharedAccessKey=", "SharedAccessSignature=", "password=",
    };

    /// <summary>The names, among <paramref name="settings"/>, that carry a key — a non-empty value under a catalogued name, or any key-shaped value.</summary>
    public static IEnumerable<string> KeyBearingSettingNames(IEnumerable<(string Name, string? Value)> settings)
    {
        foreach (var (name, value) in settings)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            var named = SettingNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                        || ConnectionStringNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            var shaped = KeyValueMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (named || shaped)
            {
                yield return name;
            }
        }
    }
}
