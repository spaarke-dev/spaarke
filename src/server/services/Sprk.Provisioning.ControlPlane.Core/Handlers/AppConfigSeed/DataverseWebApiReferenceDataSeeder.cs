// -----------------------------------------------------------------------------
// DataverseWebApiReferenceDataSeeder.cs
//
// H12b seeder for the Tier 1 product-configuration reference tables
// (sprk_eventtype_ref, sprk_agreementtype, sprk_triagecategory) — ISS-039.
//
// WHY: these tables are product configuration, not customer taxonomy: per-type
// field configuration, side-pane form routing, classifier cues and thresholds.
// ACME (the demo stamp) had 0 rows in each, and product code hard-codes dev row
// ids of sprk_eventtype_ref, so on a stamp events could not be typed and the
// Daily Briefing / Portfolio task counts and Events-page form routing matched
// nothing. See ReferenceDataSet for the source files and the fixed-key rationale.
//
// ONE GENERIC SEEDER for every table in ReferenceDataSet (L56 — the AI content —
// is intended to reuse it).
//
// SEMANTICS (data-setup-plan.md §2 Tier 1, owner decision A: report only):
//   - Row absent  -> POST with the source primary key in the body. A create can
//     never update: if a row with that id appears between the read and the POST,
//     Dataverse rejects the duplicate key and the step fails (resumable), so
//     this seeder has no code path that overwrites an existing row.
//   - Row present, equal to source -> no write.
//   - Row present, different from source -> DRIFT: logged and returned in the step
//     diagnostic (which H12b records in the run's gate evidence); never written.
//     A customer may tune classifier thresholds or cues; overwriting would
//     silently undo that.
//   - Row present but INACTIVE (statecode != 0) -> also DRIFT ("inactive"); it is
//     not recreated and not reactivated (a customer may have retired it on purpose).
//   - Row absent by id but a row with the same NATURAL KEY exists under a different
//     id (event type code, agreement type key, triage category name) -> COLLISION:
//     not created (it would duplicate the key, or shadow the customer's row), logged
//     and returned in the diagnostic + evidence alongside drift. The step still
//     succeeds; the operator decides. Product code that hard-codes the SOURCE id will
//     not find the row on such a stamp - that is exactly what the report is for.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> that creates the Tier 1 reference rows with their fixed primary keys
/// (create-if-missing) and reports drift on rows that already exist.
/// </summary>
public sealed class DataverseWebApiReferenceDataSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiReferenceDataSeeder";

    private const string ODataVersion = "4.0";

    /// <summary>Drift entries listed in the diagnostic before it says "and N more".</summary>
    private const int MaxDriftListed = 15;

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiReferenceDataSeeder> _logger;
    private readonly Func<IReadOnlyList<ReferenceTable>> _tables;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.ReferenceData;

    /// <summary>Constructs the seeder bound to a named <see cref="HttpClient"/> (production).</summary>
    public DataverseWebApiReferenceDataSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiReferenceDataSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
              () => ReferenceDataSet.Tables)
    {
    }

    /// <summary>Test seam constructor — injects a <paramref name="credentialFactory"/> and the tables to seed.</summary>
    internal DataverseWebApiReferenceDataSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiReferenceDataSeeder> logger,
        Func<string, TokenCredential> credentialFactory,
        Func<IReadOnlyList<ReferenceTable>> tables)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(tables);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _tables = tables;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TenantId);

        if (!Uri.TryCreate(input.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return AppConfigSeederResult.Failed(
                $"reference-data seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        IReadOnlyList<ReferenceTable> tables;
        try
        {
            tables = _tables();
        }
        catch (InvalidOperationException ex)
        {
            return AppConfigSeederResult.Failed($"reference-data seed FAILED — {ex.Message}");
        }

        AccessToken token;
        try
        {
            var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";
            var credential = _credentialFactory(input.TenantId);
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H12b reference-data seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"reference-data seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var created = new List<string>();
        var drift = new List<string>();
        var collisions = new List<string>();
        var unchanged = 0;
        var rowCount = 0;

        foreach (var table in tables)
        {
            try
            {
                var existing = await ReadExistingAsync(envUri, token.Token, table, cancellationToken).ConfigureAwait(false);
                var byNaturalKey = IndexByNaturalKey(table, existing);
                foreach (var row in table.Rows)
                {
                    rowCount++;
                    if (existing.TryGetValue(row.Id, out var current))
                    {
                        var findings = new List<string>();
                        var differing = DifferingColumns(table, row, current);
                        if (differing.Count > 0)
                        {
                            findings.Add($"differs in {string.Join(", ", differing)}");
                        }

                        var state = StateCode(current);
                        if (state != 0)
                        {
                            findings.Add($"is INACTIVE (statecode {state}) and was not reactivated");
                        }

                        if (findings.Count == 0)
                        {
                            unchanged++;
                            continue;
                        }

                        var entry = $"{table.Table}/{row.Id} '{row.DisplayName}' {string.Join(" and ", findings)}";
                        drift.Add(entry);
                        _logger.LogWarning(
                            "H12b reference-data drift (report only, row NOT overwritten): customer={CustomerId} {Drift}",
                            input.CustomerId, entry);
                        continue;
                    }

                    var sourceKey = NaturalKeyOf(table, row.Values);
                    if (sourceKey is not null && byNaturalKey.TryGetValue(sourceKey, out var occupiedBy))
                    {
                        var collision = $"{table.Table} '{row.DisplayName}' ({table.NaturalKey}='{sourceKey}'): a row with this key " +
                                        $"already exists under id {occupiedBy}; source id {row.Id} was NOT created";
                        collisions.Add(collision);
                        _logger.LogWarning(
                            "H12b reference-data natural-key COLLISION (report only, nothing written): customer={CustomerId} {Collision}",
                            input.CustomerId, collision);
                        continue;
                    }

                    await CreateAsync(envUri, token.Token, table, row, cancellationToken).ConfigureAwait(false);
                    created.Add($"{table.Table}/{row.Id} '{row.DisplayName}'");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return AppConfigSeederResult.Failed(
                    $"reference-data seed FAILED in table '{table.Table}': {ex.GetType().Name}: {ex.Message} " +
                    $"({created.Count} row(s) created before the failure this invocation.) " +
                    "Existing rows are never written. A re-run reads the table again: rows already created are seen and " +
                    "skipped, and a row that now exists under another id is reported as a collision.");
            }
        }

        var listed = drift.Take(MaxDriftListed).ToList();
        var driftText = drift.Count == 0
            ? "no drift"
            : $"{drift.Count} row(s) DRIFTED from source and were left unchanged (report only): " +
              string.Join("; ", listed) + (drift.Count > listed.Count ? $"; and {drift.Count - listed.Count} more" : string.Empty);
        var collisionText = collisions.Count == 0
            ? string.Empty
            : $", {collisions.Count} row(s) COLLIDED on a natural key already held by another id and were not created (report only): " +
              string.Join("; ", collisions.Take(MaxDriftListed)) +
              (collisions.Count > MaxDriftListed ? $"; and {collisions.Count - MaxDriftListed} more" : string.Empty);
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new { created, unchanged, drift, collisions })).RootElement.Clone();
        return AppConfigSeederResult.Ok(
            $"reference-data seed OK — {rowCount} source row(s) across {tables.Count} table(s): " +
            $"{created.Count} created, {unchanged} unchanged, {driftText}{collisionText}.",
            evidence);
    }

    private async Task<Dictionary<Guid, JsonElement>> ReadExistingAsync(
        Uri envUri, string bearerToken, ReferenceTable table, CancellationToken cancellationToken)
    {
        // statecode: an inactive row is reported, never silently counted as unchanged. The natural-key column is part of
        // table.Columns for every shipped table; it is unioned in explicitly so a collision check never reads a column it did not select.
        var select = string.Join(
            ",",
            new[] { table.PrimaryKey }.Concat(table.Columns).Append(table.NaturalKey ?? table.PrimaryKey).Append("statecode")
                .Distinct(StringComparer.Ordinal));
        Uri? next = new Uri(envUri, $"/api/data/v9.2/{table.EntitySet}?$select={select}");
        var existing = new Dictionary<Guid, JsonElement>();

        while (next is not null)
        {
            using var request = BuildRequest(HttpMethod.Get, next, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GET {table.EntitySet} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
            }

            using var doc = JsonDocument.Parse(text);
            foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                existing[Guid.Parse(row.GetProperty(table.PrimaryKey).GetString()!, CultureInfo.InvariantCulture)] = row.Clone();
            }

            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { Length: > 0 } nextLink
                ? new Uri(nextLink)
                : null;
        }

        return existing;
    }

    private async Task CreateAsync(
        Uri envUri, string bearerToken, ReferenceTable table, ReferenceRow row, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { [table.PrimaryKey] = row.Id.ToString() };
        foreach (var column in table.Columns)
        {
            var value = row.Values[column];
            if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                body[column] = value;
            }
        }

        using var request = BuildRequest(HttpMethod.Post, new Uri(envUri, $"/api/data/v9.2/{table.EntitySet}"), bearerToken);
        request.Content = JsonContent.Create(body);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST {table.EntitySet} ({row.Id} '{row.DisplayName}') returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
    }

    /// <summary>Existing rows indexed by normalised natural key (first wins on a duplicate); empty when the table has none.</summary>
    private static Dictionary<string, Guid> IndexByNaturalKey(ReferenceTable table, Dictionary<Guid, JsonElement> existing)
    {
        var index = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (id, row) in existing)
        {
            if (NaturalKeyOf(table, row) is { } key)
            {
                index.TryAdd(key, id);
            }
        }

        return index;
    }

    /// <summary>The row's natural key, trimmed and case-folded (Dataverse text keys compare case-insensitively); null when absent or blank.</summary>
    private static string? NaturalKeyOf(ReferenceTable table, JsonElement row)
        => table.NaturalKey is { } column && row.TryGetProperty(column, out var value) ? Normalise(value) : null;

    private static string? NaturalKeyOf(ReferenceTable table, IReadOnlyDictionary<string, JsonElement> values)
        => table.NaturalKey is { } column && values.TryGetValue(column, out var value) ? Normalise(value) : null;

    private static string? Normalise(JsonElement value)
        => value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text
            ? text.ToUpperInvariant()
            : null;

    /// <summary>The row's statecode (0 = active); a row that did not return the column is treated as active.</summary>
    private static int StateCode(JsonElement row)
        => row.TryGetProperty("statecode", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    /// <summary>The source columns whose value differs from the existing row (null and empty text are equal).</summary>
    internal static IReadOnlyList<string> DifferingColumns(ReferenceTable table, ReferenceRow source, JsonElement existing)
    {
        var differing = new List<string>();
        foreach (var column in table.Columns)
        {
            var current = existing.TryGetProperty(column, out var value) ? value : default;
            if (!string.Equals(Canonical(source.Values[column]), Canonical(current), StringComparison.Ordinal))
            {
                differing.Add(column);
            }
        }

        return differing;
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => (value.GetString() ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal),
        JsonValueKind.Number => value.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty, // Null / Undefined
    };

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string bearerToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
