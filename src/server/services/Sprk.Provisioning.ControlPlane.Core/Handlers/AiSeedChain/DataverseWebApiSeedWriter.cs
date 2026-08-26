// -----------------------------------------------------------------------------
// DataverseWebApiSeedWriter.cs
//
// Task 150 (Wave G-5 Batch G-5A) — production ISeedManifestRunner. Replaces
// InvokeSeedManifestScriptRunner (pwsh shell-out to task-069's
// scripts/seed-data/Invoke-SeedManifest.ps1 -Live, which itself required a
// second PowerShell YAML-parsing module — see H12aAiSeedChainHandler.cs file
// header + the DS-1b matrix-correction finding this task closes). Reuses
// H12cRuntimeReferencesHandler.cs's collaborator
// DataverseWebApiModelDeploymentReferenceWriter's EXACT in-process Dataverse
// Web API idiom — HttpClient + DefaultAzureCredential token acquisition
// scoped to `{envUri}/.default`, find-by-filter existence check, JsonContent
// POST with the same OData headers — per DS-1b §1 H12a row + the POML
// constraint "REUSE H12c's exact in-process Dataverse Web API upsert idiom
// -- do not invent a second, parallel Dataverse-write helper".
//
// SCOPE BOUNDARY (documented per CLAUDE.md §11 minimal-diff reuse + parity
// with H12cRuntimeReferencesHandler.cs's own "LIVE DATAVERSE SCHEMA
// deviation note" precedent):
//   task-069's manifest.yaml declares 12 artifacts. This writer directly
//   seeds the 4 whose deployer.idempotencyMode is "existence-check-then-
//   insert" AND whose authoritativeSource is a flat, self-contained JSON
//   content file with NO relationship/association wiring:
//     - type-lookups  (sprk_analysisactiontype / sprk_aiskilltype /
//       sprk_aiknowledgetype / sprk_aitooltype — sprk_name only)
//     - knowledge     (sprk_analysisknowledges — sprk_name/sprk_description/
//       sprk_content + a KnowledgeType @odata.bind lookup, exact field
//       shape verbatim from Deploy-Knowledge.ps1's New-KnowledgeRecord)
//     - skills        (sprk_analysisskills — sprk_name/sprk_description/
//       sprk_promptfragment + a SkillType @odata.bind lookup, verbatim from
//       Deploy-Skills.ps1's New-SkillRecord)
//     - output-types  (sprk_aioutputtypes — sprk_name ONLY; Deploy-
//       OutputTypes.ps1's own New-OutputTypeRecord comment: "Entity only
//       supports sprk_name field. Field mapping metadata ... stored in
//       output-types.json for documentation but not deployed to Dataverse")
//   The remaining 8 artifacts (input-schemas / output-schemas / actions-r7 /
//   tools-r7 — 4 R7 per-file directory loaders declared deployer:null +
//   deployerOwnedBy:H12a in the manifest, i.e. PENDING under the ORIGINAL
//   script too; playbooks-mvp / action-outputschema-patches /
//   playbook-consumers — 3 artifacts whose PS deployers perform N:N
//   relationship $ref association wiring, a materially different operation
//   shape than H12c's flat upsert idiom this task's constraint scopes the
//   writer to reuse; aimodeldeployment — the H12c-owned placeholder, MUST
//   NOT be populated by H12a) are reported with the SAME PENDING/PLACEHOLDER
//   marker semantics Invoke-SeedManifest.ps1 already emits for its own
//   deployer:null artifacts (Invoke-SeedManifest.ps1:475-486) — this writer
//   does not regress observability for those artifacts, it only declines to
//   invent a second write-idiom (association wiring) beyond what the POML
//   constraint scopes to "H12c's exact... upsert idiom". Relationship-wiring
//   support is a legitimate, cleanly-separable follow-on (own idempotency +
//   test shape), not a silent gap: it is called out here + in task 150's
//   completion notes.
//
// IDEMPOTENCY (existence-check-then-insert, per each seeded artifact's
// deployer.idempotencyMode in manifest.yaml): find-by-sprk_name; skip (no
// write) if found; POST if not found. This deliberately does NOT patch an
// existing row (unlike H12c's find-then-patch-or-create) — patch-vs-skip is
// the manifest's OWN declared per-artifact idempotencyMode, verbatim from
// each PS seeder's Test-RecordExists-then-skip behavior; the HTTP
// REQUEST-SHAPE (auth header, filter-query, JSON POST body, OData headers)
// is what this task's constraint requires reusing from H12c, and that shape
// is reused exactly.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <inheritdoc cref="ISeedManifestRunner"/>
public sealed class DataverseWebApiSeedWriter : ISeedManifestRunner
{
    private const int SummaryBudget = 800;

    /// <summary>Artifact ids this writer performs a real Dataverse upsert for — see file header SCOPE BOUNDARY.</summary>
    private static readonly IReadOnlySet<string> DirectlySeededArtifactIds =
        new HashSet<string>(StringComparer.Ordinal) { "type-lookups", "knowledge", "skills", "output-types" };

    private readonly HttpClient _httpClient;
    private readonly AiSeedChainOptions _options;
    private readonly ILogger<DataverseWebApiSeedWriter> _logger;
    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Constructs the writer bound to a typed <see cref="HttpClient"/> (production via <c>services.AddHttpClient</c> in Worker/Program.cs).</summary>
    public DataverseWebApiSeedWriter(
        HttpClient httpClient,
        IOptions<AiSeedChainOptions> options,
        ILogger<DataverseWebApiSeedWriter> logger)
        : this(httpClient, options, logger, tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// (so tests never invoke the real DefaultAzureCredential network path)
    /// alongside a fake-transport <see cref="HttpClient"/>. Same seam shape as
    /// <see cref="SolutionImport.DataverseWebApiSolutionImporter"/>'s own
    /// internal test constructor.
    /// </summary>
    internal DataverseWebApiSeedWriter(
        HttpClient httpClient,
        IOptions<AiSeedChainOptions> options,
        ILogger<DataverseWebApiSeedWriter> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<SeedManifestInvocationOutcome> InvokeAsync(
        SeedManifestInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SeedManifestDocument manifest;
        IReadOnlyList<string> order;
        try
        {
            manifest = YamlSeedManifestEngine.ParseFromEmbeddedResource();
            order = YamlSeedManifestEngine.ComputeTopologicalOrder(manifest.Artifacts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SeedManifestInvocationOutcome.Failure(
                $"Seed manifest engine error: {ex.GetType().Name}: {ex.Message}");
        }

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new SeedManifestInvocationOutcome.Failure(
                $"Dataverse environment URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SeedManifestInvocationOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var byId = manifest.Artifacts.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var summaryLines = new List<string>(order.Count);
        var totalUpserted = 0;

        foreach (var id in order)
        {
            var artifact = byId[id];
            try
            {
                var (status, detail, upsertedCount) = await SeedArtifactAsync(envUri, token, artifact, cancellationToken)
                    .ConfigureAwait(false);
                totalUpserted += upsertedCount;
                summaryLines.Add($"[{id}] {status} ({detail})");
                _logger.LogInformation(
                    "H12a seed writer: artifact={ArtifactId} status={Status} detail={Detail}", id, status, detail);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "H12a seed writer infrastructure fault on artifact {ArtifactId} — {UpsertedCount} row(s) " +
                    "upserted across prior artifacts before the failure (retry-safe — existence-check-then-insert " +
                    "is idempotent on re-run).",
                    id, totalUpserted);
                return new SeedManifestInvocationOutcome.Failure(
                    $"Seed write failed on artifact '{id}' after upserting {totalUpserted} row(s) across prior " +
                    $"artifacts: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var summary = $"Seed complete. {totalUpserted} row(s) upserted across {order.Count} artifact(s). " +
                       string.Join(' ', summaryLines);
        return new SeedManifestInvocationOutcome.Success(Truncate(summary, SummaryBudget));
    }

    private async Task<(string Status, string Detail, int UpsertedCount)> SeedArtifactAsync(
        Uri envUri, AccessToken token, SeedArtifact artifact, CancellationToken ct)
    {
        if (artifact.Deployer is null)
        {
            var owner = artifact.DeployerOwnedBy ?? "UNASSIGNED";
            var isPlaceholder = artifact.AuthoritativeSource is null
                && string.Equals(owner, "H12c", StringComparison.Ordinal);
            return (isPlaceholder ? "PLACEHOLDER" : "PENDING", $"owner={owner}", 0);
        }

        if (!DirectlySeededArtifactIds.Contains(artifact.Id))
        {
            // Deployer declared in the manifest but this writer does not port
            // that artifact's seeder (relationship-association wiring or an
            // R7 per-file directory loader) — see file header SCOPE BOUNDARY.
            return ("PENDING", "owner=out-of-writer-scope (relationship wiring / per-file loader — see file header)", 0);
        }

        var count = artifact.Id switch
        {
            "type-lookups" => await SeedTypeLookupsAsync(envUri, token, ct).ConfigureAwait(false),
            "knowledge" => await SeedKnowledgeAsync(envUri, token, ct).ConfigureAwait(false),
            "skills" => await SeedSkillsAsync(envUri, token, ct).ConfigureAwait(false),
            "output-types" => await SeedOutputTypesAsync(envUri, token, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unreachable: '{artifact.Id}' is in {nameof(DirectlySeededArtifactIds)} without a seeder case."),
        };
        return ("OK", $"{count} row(s) upserted", count);
    }

    // ---- Per-artifact seeders (field shapes verbatim from the Deploy-*.ps1 scripts they replace) ----

    private async Task<int> SeedTypeLookupsAsync(Uri envUri, AccessToken token, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(ReadEmbeddedJson("type-lookups.json"));
        var count = 0;
        foreach (var entitySetProperty in doc.RootElement.EnumerateObject())
        {
            // Top-level keys ARE the Dataverse entity set names for this file
            // (sprk_analysisactiontype / sprk_aiskilltype / sprk_aiknowledgetype
            // / sprk_aitooltype); description/created/notes are plain strings
            // — skip anything that isn't an array.
            if (entitySetProperty.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var entitySet = entitySetProperty.Name;
            foreach (var row in entitySetProperty.Value.EnumerateArray())
            {
                var name = row.GetProperty("sprk_name").GetString()
                    ?? throw new InvalidOperationException($"type-lookups.json: entry under '{entitySet}' missing sprk_name.");
                if (await ExistsByNameAsync(envUri, token, entitySet, name, ct).ConfigureAwait(false))
                {
                    continue;
                }
                await CreateAsync(envUri, token, entitySet, new Dictionary<string, object?> { ["sprk_name"] = name }, ct)
                    .ConfigureAwait(false);
                count++;
            }
        }
        return count;
    }

    private async Task<int> SeedKnowledgeAsync(Uri envUri, AccessToken token, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(ReadEmbeddedJson("knowledge.json"));
        var typeLookup = ReadStringGuidDictionary(doc.RootElement, "knowledgeTypes");
        const string entitySet = "sprk_analysisknowledges";

        var count = 0;
        foreach (var record in doc.RootElement.GetProperty("knowledge").EnumerateArray())
        {
            var name = record.GetProperty("sprk_name").GetString()
                ?? throw new InvalidOperationException("knowledge.json: entry missing sprk_name.");
            if (await ExistsByNameAsync(envUri, token, entitySet, name, ct).ConfigureAwait(false))
            {
                continue;
            }

            var typeKey = record.GetProperty("knowledgeType").GetString() ?? string.Empty;
            if (!typeLookup.TryGetValue(typeKey, out var typeId))
            {
                throw new InvalidOperationException(
                    $"knowledge.json: entry '{name}' references knowledgeType '{typeKey}' not present in the " +
                    "manifest's knowledgeTypes lookup.");
            }

            var payload = new Dictionary<string, object?>
            {
                ["sprk_name"] = name,
                ["sprk_description"] = GetStringOrNull(record, "sprk_description"),
                ["sprk_KnowledgeTypeId@odata.bind"] = $"/sprk_aiknowledgetypes({typeId})",
            };

            // Verbatim from Deploy-Knowledge.ps1's New-KnowledgeRecord content
            // branch: inline content for isInline=true, RAG deployment
            // reference note otherwise.
            var isInline = record.TryGetProperty("isInline", out var inlineEl) && inlineEl.ValueKind == JsonValueKind.True;
            var content = GetStringOrNull(record, "sprk_content");
            if (isInline && content is not null)
            {
                payload["sprk_content"] = content;
            }
            else if (!isInline && GetStringOrNull(record, "ragDeployment") is { } ragDeployment)
            {
                payload["sprk_content"] = $"RAG Deployment: {ragDeployment}";
            }

            await CreateAsync(envUri, token, entitySet, payload, ct).ConfigureAwait(false);
            count++;
        }
        return count;
    }

    private async Task<int> SeedSkillsAsync(Uri envUri, AccessToken token, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(ReadEmbeddedJson("skills.json"));
        var typeLookup = ReadStringGuidDictionary(doc.RootElement, "skillTypes");
        const string entitySet = "sprk_analysisskills";

        var count = 0;
        foreach (var record in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            var name = record.GetProperty("sprk_name").GetString()
                ?? throw new InvalidOperationException("skills.json: entry missing sprk_name.");
            if (await ExistsByNameAsync(envUri, token, entitySet, name, ct).ConfigureAwait(false))
            {
                continue;
            }

            var typeKey = record.GetProperty("skillType").GetString() ?? string.Empty;
            if (!typeLookup.TryGetValue(typeKey, out var typeId))
            {
                throw new InvalidOperationException(
                    $"skills.json: entry '{name}' references skillType '{typeKey}' not present in the manifest's " +
                    "skillTypes lookup.");
            }

            var payload = new Dictionary<string, object?>
            {
                ["sprk_name"] = name,
                ["sprk_description"] = GetStringOrNull(record, "sprk_description"),
                ["sprk_promptfragment"] = GetStringOrNull(record, "sprk_promptfragment"),
                ["sprk_SkillTypeId@odata.bind"] = $"/sprk_aiskilltypes({typeId})",
            };

            await CreateAsync(envUri, token, entitySet, payload, ct).ConfigureAwait(false);
            count++;
        }
        return count;
    }

    private async Task<int> SeedOutputTypesAsync(Uri envUri, AccessToken token, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(ReadEmbeddedJson("output-types.json"));
        const string entitySet = "sprk_aioutputtypes";

        var count = 0;
        foreach (var record in doc.RootElement.GetProperty("outputTypes").EnumerateArray())
        {
            var name = record.GetProperty("sprk_name").GetString()
                ?? throw new InvalidOperationException("output-types.json: entry missing sprk_name.");
            if (await ExistsByNameAsync(envUri, token, entitySet, name, ct).ConfigureAwait(false))
            {
                continue;
            }

            // Verbatim from Deploy-OutputTypes.ps1's New-OutputTypeRecord —
            // "Entity only supports sprk_name field. Field mapping metadata
            // ... stored in output-types.json for documentation but not
            // deployed to Dataverse."
            await CreateAsync(envUri, token, entitySet, new Dictionary<string, object?> { ["sprk_name"] = name }, ct)
                .ConfigureAwait(false);
            count++;
        }
        return count;
    }

    // ---- Generic Dataverse Web API upsert primitives (H12c idiom, reused verbatim in shape) ----

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = _credentialFactory(tenantId);
        return await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
    }

    private async Task<bool> ExistsByNameAsync(Uri envUri, AccessToken token, string entitySet, string name, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(name);
        var uri = new Uri(envUri, $"/api/data/v9.2/{entitySet}?$filter=sprk_name eq '{encodedName}'&$select=sprk_name");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {uri.PathAndQuery} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
        using var body = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return body.RootElement.TryGetProperty("value", out var values) && values.GetArrayLength() > 0;
    }

    private async Task CreateAsync(Uri envUri, AccessToken token, string entitySet, Dictionary<string, object?> payload, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/{entitySet}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST {entitySet} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private static void ApplyCommonHeaders(HttpRequestMessage request, AccessToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("Prefer", "return=representation");
    }

    // ---- Embedded seed-content helpers ----

    private static string ReadEmbeddedJson(string fileName)
    {
        var resourceName = $"Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain.SeedContent.{fileName}";
        return YamlSeedManifestEngine.ReadEmbeddedResourceText(typeof(DataverseWebApiSeedWriter).Assembly, resourceName);
    }

    private static Dictionary<string, string> ReadStringGuidDictionary(JsonElement root, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(propertyName, out var dictEl))
        {
            return result;
        }
        foreach (var entry in dictEl.EnumerateObject())
        {
            var value = entry.Value.GetString();
            if (value is not null)
            {
                result[entry.Name] = value;
            }
        }
        return result;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
