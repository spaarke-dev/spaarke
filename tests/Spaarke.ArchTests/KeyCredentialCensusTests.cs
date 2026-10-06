using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// customer-provisioning-orchestration-r1 task 230b (owner D13) — the KEY-credential census. Every place server code
/// builds a key credential, sends a key header or opens a key-bearing connection string is listed here per file, with
/// the configuration key that arms it and a reason. A new site fails the build until it is listed — and listing it
/// forces the question D13 asks: a customer stamp holds no key, so why does this code take one?
/// </summary>
/// <remarks>
/// <para><b>What the pinned sites are.</b> "Key if configured, else the managed identity" branches kept for the shared
/// dev environment (ADR-028 E-2 and the auth-v4 transition) — on a stamp the setting is absent, so the identity path
/// runs. Each listed config key must therefore also be (1) in L2's <c>StampKeySettingCatalog</c>, so H13 fails a stamp
/// whose App Service carries it, and (2) in the BFF's keyless-proof probes, so the proof reports
/// <c>key-credential</c> instead of calling. Both are asserted below by reading those source files.</para>
/// <para><b>Relationship to <see cref="CredentialCensusTests"/>.</b> That census counts CONFIDENTIAL clients (MSAL /
/// Azure.Identity client credentials — the BFF identity's secret story). This one counts KEYS to Azure services and
/// APIs. Neither subsumes the other.</para>
/// <para>Scans <c>src/server/**</c> production code (the <c>*.Tests</c> projects are excluded — test fakes may build
/// keyed clients). No DI resolution — ADR-038 ban B3. MAINTAIN-class structural fitness function.</para>
/// </remarks>
public class KeyCredentialCensusTests
{
    // =============================================================================================
    // THE CENSUS — MAINTENANCE: a failure is NOT a prompt to bump a number. Ask whether the new site should exist:
    // a stamp is keyless (owner D13). If it must exist, add the entry with its config key(s) and a reason, add the
    // keys to StampKeySettingCatalog (L2) and to the BFF keyless-proof probe — or mark StampResource=false with the
    // reason when the key belongs to an external service that is not part of the stamp.
    // =============================================================================================
    private static readonly IReadOnlyList<KeySite> Census = new[]
    {
        new KeySite("SearchClientFactory.cs", 2,
            ConfigKeys: new[] { "DocumentIntelligence:AiSearchKey", "AiSearch:ReferencesApiKey", "RecordSync:AiSearchApiKey" },
            StampResource: true,
            Reason: "AI Search admin key if one is configured and AiSearch:ManagedIdentity:Enabled is not set, else the "
                    + "managed identity — SearchClient and SearchIndexClient. The shared dev service stays aadOrApiKey "
                    + "(auth-v4 task 053); a stamp's service has local auth disabled, so the key path cannot work there."),

        new KeySite("AiModule.cs", 1,
            ConfigKeys: new[] { "AzureOpenAI:ApiKey" },
            StampResource: true,
            Reason: "Agent chat client: ADR-028 exception E-2 (MI 401 on the shared dev kind=AIServices account). A "
                    + "stamp's kind: OpenAI account has local auth disabled; the keyless proof is E-2's first measurement there."),

        new KeySite("OpenAiClient.cs", 1,
            ConfigKeys: new[] { "DocumentIntelligence:OpenAiKey" },
            StampResource: true,
            Reason: "Analysis / embeddings client: the same E-2 key-or-identity branch (auth-v4 task 054) as AiModule; "
                    + "resolving E-2 is a config change, not a code change."),

        new KeySite("TextExtractorService.cs", 1,
            ConfigKeys: new[] { "DocumentIntelligence:DocIntelKey" },
            StampResource: true,
            Reason: "Document Intelligence: dev's account has no custom subdomain and accepts keys only (auth-v4 task 054); "
                    + "a stamp's account has a subdomain, local auth disabled and the identity's role."),

        new KeySite("ContentSafetyAuthHandler.cs", 1,
            ConfigKeys: new[] { "AiSafety:ContentSafety:ApiKey" },
            StampResource: true,
            Reason: "Ocp-Apim-Subscription-Key for local development only; with no key (every deployed environment, "
                    + "task 246) the handler attaches the managed-identity bearer."),

        new KeySite("ServiceBusClientFactory.cs", 1,
            ConfigKeys: new[] { "ServiceBus:ConnectionString" },
            StampResource: true,
            Reason: "Connection-string fallback when no namespace is configured (local development / emulator); a stamp "
                    + "sets ServiceBus:FullyQualifiedNamespace and its namespace has local auth disabled."),

        new KeySite("CacheModule.cs", 1,
            ConfigKeys: new[] { "Redis:ConnectionString", "ConnectionStrings:Redis" },
            StampResource: true,
            Reason: "Redis connection string — accepted only in Development/Testing (CacheModule refuses it elsewhere, task "
                    + "242); a stamp sets Redis:Endpoint and its Azure Managed Redis has access keys disabled."),

        new KeySite("WebSearchHandler.cs", 1,
            ConfigKeys: new[] { "BingSearch:ApiKey" },
            StampResource: false,
            Reason: "Bing Web Search is an external Microsoft API, not a stamp resource: it has no Entra data-plane auth, "
                    + "so its subscription key is the only credential. Outside owner D13's scope (stamp resources)."),
    };

    /// <summary>The forms that count as a key site.</summary>
    private static readonly (Regex Pattern, string Kind)[] Forms =
    {
        (new Regex(@"new\s+(?:Azure\.)?AzureKeyCredential\s*\(", RegexOptions.Compiled), "AzureKeyCredential"),
        (new Regex(@"\b(?:AzureKeyCredential|ApiKeyCredential|AzureNamedKeyCredential|AzureSasCredential|StorageSharedKeyCredential)\s+\w+\s*=\s*new\s*\(", RegexOptions.Compiled), "target-typed key credential"),
        // The non-Azure OpenAI SDK clients take a key string. Qualified forms only: an unqualified ChatClient is also the ACS
        // chat SDK's type (Services/Communication), which takes a token credential.
        (new Regex(@"new\s+(?:OpenAI\.(?:Chat\.|Embeddings\.)?(?:ChatClient|EmbeddingClient)|(?:OpenAI\.)?OpenAIClient)\s*\(", RegexOptions.Compiled), "OpenAI client (string-key overloads)"),
        (new Regex(@"new\s+(?:BlobServiceClient|QueueServiceClient|TableServiceClient|CosmosClient)\s*\(\s*[\w.]*[Cc]onnection[Ss]tring", RegexOptions.Compiled), "connection-string client"),
        (new Regex(@"(?:ConfigurationOptions\.Parse|ConnectionMultiplexer\.Connect(?:Async)?)\s*\(\s*[\w.]*[Cc]onnection[Ss]tring", RegexOptions.Compiled), "Redis connection string"),
        (new Regex(@"new\s+(?:System\.ClientModel\.)?ApiKeyCredential\s*\(", RegexOptions.Compiled), "ApiKeyCredential"),
        (new Regex(@"new\s+(?:Azure\.)?(?:AzureNamedKeyCredential|AzureSasCredential)\s*\(", RegexOptions.Compiled), "named key / SAS credential"),
        (new Regex(@"new\s+StorageSharedKeyCredential\s*\(", RegexOptions.Compiled), "StorageSharedKeyCredential"),
        (new Regex(@"""(?:Ocp-Apim-Subscription-Key|api-key)""", RegexOptions.Compiled | RegexOptions.IgnoreCase), "key header"),
        (new Regex(@"new\s+ServiceBusClient\s*\(\s*[\w.]*ConnectionString\s*\)", RegexOptions.Compiled), "Service Bus connection string"),
    };

    /// <summary>L2's catalog of stamp key settings — H13 fails a slot that carries any of them.</summary>
    private const string L2CatalogFile = "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/ArmStampKeylessVerifier.cs";

    /// <summary>The BFF's keyless-proof probes — each must report key-credential for every stamp key setting.</summary>
    private static readonly string[] ProbeFiles =
    {
        "src/server/api/Sprk.Bff.Api/Services/Ai/Diagnostics/AiKeylessProbe.cs",
        "src/server/api/Sprk.Bff.Api/Infrastructure/Diagnostics/KeylessProofService.cs",
    };

    [Fact(DisplayName = "D13/T230b: every key-credential site in server code is in the census, with the expected count")]
    public void EveryKeySiteIsCensused()
    {
        var actual = ScanSites();
        var expected = Census.ToDictionary(e => e.FileName, e => e.Sites, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var (file, sites) in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(file, out var count))
                problems.Add($"UNLISTED key site(s) in {file}:\n" + string.Join("\n", sites.Select(s => "    " + s)));
            else if (sites.Count != count)
                problems.Add($"COUNT CHANGED in {file}: census says {count}, source has {sites.Count}:\n" + string.Join("\n", sites.Select(s => "    " + s)));
        }
        foreach (var file in expected.Keys.Where(f => !actual.ContainsKey(f)))
            problems.Add($"CENSUSED BUT ABSENT: {file} has no key site — remove its entry.");

        Assert.True(problems.Count == 0,
            "The key-credential census does not match the source. A customer stamp is keyless (owner D13): ask first "
            + "whether the new site should exist. If it must, add the entry with its config key and reason, and add the "
            + "key to L2's StampKeySettingCatalog and the BFF keyless-proof probe.\n\n" + string.Join("\n\n", problems));
    }

    [Fact(DisplayName = "D13/T230b: every stamp key setting is refused by H13 (L2 StampKeySettingCatalog) and reported by the keyless proof")]
    public void EveryStampKeySetting_IsCatalogued_AndProbed()
    {
        var catalog = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, L2CatalogFile));
        var probes = string.Concat(ProbeFiles.Select(f => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, f))));
        var missing = new List<string>();

        foreach (var entry in Census.Where(e => e.StampResource))
        {
            foreach (var key in entry.ConfigKeys)
            {
                var appSetting = key.Replace(":", "__", StringComparison.Ordinal);
                if (!catalog.Contains($"\"{appSetting}\"", StringComparison.Ordinal))
                    missing.Add($"{key}: not in StampKeySettingCatalog as \"{appSetting}\" ({L2CatalogFile})");
                if (!probes.Contains($"\"{key}\"", StringComparison.Ordinal))
                    missing.Add($"{key}: no keyless-proof probe reports it ({string.Join(", ", ProbeFiles)})");
            }
        }

        Assert.True(missing.Count == 0,
            "A key the BFF can use is not covered by the keyless gate — H13 would pass a stamp carrying it:\n"
            + string.Join("\n", missing));
    }

    [Fact(DisplayName = "D13/T230b: every census entry names its config keys and a substantive reason")]
    public void EveryCensusEntryIsExplained()
    {
        var unexplained = Census.Where(e => e.ConfigKeys.Count == 0 || e.Reason.Trim().Length < 60).Select(e => e.FileName).ToList();
        Assert.True(unexplained.Count == 0, "Entries without config keys or a reason: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "D13/T230b: negative control — the detector finds every form and ignores comments")]
    public void Detector_NegativeControl()
    {
        var sites = FindSites(new[]
        {
            "    var a = new AzureKeyCredential(key);",
            "    var b = new System.ClientModel.ApiKeyCredential(key);",
            "    var c = new AzureSasCredential(sas);",
            "    var d = new StorageSharedKeyCredential(account, key);",
            "    request.Headers.Add(\"api-key\", key);",
            "    private const string Header = \"Ocp-Apim-Subscription-Key\";",
            "    return new ServiceBusClient(options.ConnectionString);",
            "    // new AzureKeyCredential(key) in a comment",
            "    /// <c>Ocp-Apim-Subscription-Key</c> in documentation",
            "    return new ServiceBusClient(options.FullyQualifiedNamespace, credential);",
            "    AzureKeyCredential c = new(key);",
            "    var chat = new OpenAI.Chat.ChatClient(model, apiKey);",
            "    var acs = new ChatClient(endpoint, credential);", // the ACS chat SDK — not a key site
            "    var blob = new BlobServiceClient(options.ConnectionString);",
            "    var o = ConfigurationOptions.Parse(redisConnectionString);",
            "    var o2 = ConfigurationOptions.Parse(endpoint);",
            "    var blob2 = new BlobServiceClient(endpointUri, credential);",
        });

        Assert.Equal(11, sites.Count);
    }

    // =============================================================================================

    private sealed record KeySite(string FileName, int Sites, IReadOnlyList<string> ConfigKeys, bool StampResource, string Reason);

    private static Dictionary<string, List<string>> ScanSites()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var testProject = $"{Path.DirectorySeparatorChar}Sprk.Provisioning.ControlPlane.Tests{Path.DirectorySeparatorChar}";
        foreach (var file in SourceScan.ServerSourceFiles().Where(f => !f.Contains(testProject, StringComparison.Ordinal)))
        {
            var sites = FindSites(File.ReadAllLines(file));
            if (sites.Count > 0)
            {
                var name = Path.GetFileName(file);
                if (!found.TryGetValue(name, out var list))
                    found[name] = list = new List<string>();
                list.AddRange(sites.Select(s => $"{SourceScan.Relative(file)}:{s}"));
            }
        }
        return found;
    }

    private static List<string> FindSites(IReadOnlyList<string> lines)
    {
        var sites = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            foreach (var (pattern, kind) in Forms)
            {
                foreach (Match _ in pattern.Matches(code))
                    sites.Add($"{i + 1}: {kind}");
            }
        }
        return sites;
    }
}
