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
// STARTER TAXONOMY (data-setup plan D4, owner decisions A and B): after the Tier 1
// tables, the seeder seeds the pack the run selected (referenceDataPack: legal-standard
// by default, none = nothing) — matter / subtype / practice-area / project / document /
// contact / organization types. Those rows are the CUSTOMER'S, so the rule differs from
// Tier 1: a source row counts as present when the environment holds a row with its id,
// its code or its name (trimmed, case-insensitive, active or inactive) and is then left
// alone — no write, no report unless its columns differ (diagnostic only). An absent row
// is created with the pack's id (stable across stamps; on the two tables with no alternate
// key a repeated POST is refused instead of duplicating a row). Nothing is ever updated.
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
    private readonly Func<string, IReadOnlyList<ReferenceTable>> _packTables;

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

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>, the Tier 1 tables to seed and, optionally, the
    /// starter-taxonomy pack lookup (pack name → tables; defaults to the embedded packs).
    /// </summary>
    internal DataverseWebApiReferenceDataSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiReferenceDataSeeder> logger,
        Func<string, TokenCredential> credentialFactory,
        Func<IReadOnlyList<ReferenceTable>> tables,
        Func<string, IReadOnlyList<ReferenceTable>>? packTables = null)
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
        _packTables = packTables ?? ReferenceDataSet.Pack;
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
        IReadOnlyList<ReferenceTable> packTables;
        try
        {
            tables = _tables();
            packTables = _packTables(input.ReferenceDataPack);
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

        // Starter taxonomy (data-setup plan D4): create-if-missing by natural key, never an update. Its own counters, so the
        // Tier 1 counts and wording above stay what ISS-039 shipped.
        var pack = new PackOutcome(input.ReferenceDataPack);
        try
        {
            foreach (var table in packTables)
            {
                await SeedPackTableAsync(envUri, token.Token, table, pack, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return AppConfigSeederResult.Failed(
                $"reference-data seed FAILED in starter taxonomy pack '{pack.Name}': {ex.GetType().Name}: {ex.Message} " +
                $"({pack.Created.Count} pack row(s) created before the failure this invocation; {created.Count} Tier 1 row(s).) " +
                "Existing rows are never written. A re-run reads each table again: rows already created are matched and skipped.");
        }

        if (pack.Differs.Count > 0)
        {
            _logger.LogInformation(
                "H12b reference-data pack rows that differ from the pack (report only, NOT overwritten): customer={CustomerId} pack={Pack} count={Count}",
                input.CustomerId, pack.Name, pack.Differs.Count);
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
        var packText = pack.Tables == 0
            ? $"starter taxonomy pack '{pack.Name}': nothing seeded"
            : $"starter taxonomy pack '{pack.Name}': {pack.Rows} row(s) across {pack.Tables} table(s), {pack.Created.Count} created, " +
              $"{pack.Present} already present" +
              (pack.Differs.Count == 0
                  ? string.Empty
                  : $", {pack.Differs.Count} present row(s) DIFFER from the pack and were left unchanged (report only): " +
                    string.Join("; ", pack.Differs.Take(MaxDriftListed)) +
                    (pack.Differs.Count > MaxDriftListed ? $"; and {pack.Differs.Count - MaxDriftListed} more" : string.Empty));
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            created,
            unchanged,
            drift,
            collisions,
            pack = new { name = pack.Name, tables = pack.Tables, rows = pack.Rows, created = pack.Created, present = pack.Present, differs = pack.Differs },
        })).RootElement.Clone();
        return AppConfigSeederResult.Ok(
            $"reference-data seed OK — {rowCount} source row(s) across {tables.Count} table(s): " +
            $"{created.Count} created, {unchanged} unchanged, {driftText}{collisionText}; {packText}.",
            evidence);
    }

    /// <summary>Counters for one starter-taxonomy pack across its tables.</summary>
    private sealed class PackOutcome(string name)
    {
        public string Name { get; } = name;
        public int Tables { get; set; }
        public int Rows { get; set; }
        public int Present { get; set; }
        public List<string> Created { get; } = [];
        public List<string> Differs { get; } = [];
    }

    /// <summary>
    /// Seeds one starter-taxonomy table. A source row is PRESENT when the environment holds a row with its id, or with its code,
    /// or with its name (trimmed, case-insensitive; active or inactive) — nothing is written and nothing is reported unless the
    /// present row's columns differ, which goes in the diagnostic only. An absent row is created with the pack's id. There is no
    /// update path: the customer owns these rows once they exist.
    /// </summary>
    private async Task SeedPackTableAsync(
        Uri envUri, string bearerToken, ReferenceTable table, PackOutcome pack, CancellationToken cancellationToken)
    {
        var existing = await ReadExistingAsync(envUri, bearerToken, table, cancellationToken).ConfigureAwait(false);
        var byCode = IndexByColumn(table.NaturalKey, existing);
        var byName = IndexByColumn(table.NameColumn, existing);
        pack.Tables++;

        foreach (var row in table.Rows)
        {
            pack.Rows++;
            JsonElement current = default;
            var found = existing.TryGetValue(row.Id, out current)
                        || (ValueKey(table.NaturalKey, row.Values) is { } code && byCode.TryGetValue(code, out current))
                        || (ValueKey(table.NameColumn, row.Values) is { } name && byName.TryGetValue(name, out current));
            if (found)
            {
                pack.Present++;
                var differing = DifferingColumns(table, row, current);
                if (differing.Count > 0)
                {
                    pack.Differs.Add($"{table.Table} '{PackLabel(table, row)}' differs in {string.Join(", ", differing)}");
                }

                continue;
            }

            await CreateAsync(envUri, bearerToken, table, row, cancellationToken).ConfigureAwait(false);
            pack.Created.Add($"{table.Table}/{row.Id} '{PackLabel(table, row)}'");
        }
    }

    private static string PackLabel(ReferenceTable table, ReferenceRow row)
        => table.NameColumn is { } column && row.Values.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : row.DisplayName;

    /// <summary>Existing rows indexed by a column's normalised value (first wins on a duplicate); empty when the column is null.</summary>
    private static Dictionary<string, JsonElement> IndexByColumn(string? column, Dictionary<Guid, JsonElement> existing)
    {
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (column is null)
        {
            return index;
        }

        foreach (var row in existing.Values)
        {
            if (row.TryGetProperty(column, out var value) && Normalise(value) is { } key)
            {
                index.TryAdd(key, row);
            }
        }

        return index;
    }

    private static string? ValueKey(string? column, IReadOnlyDictionary<string, JsonElement> values)
        => column is not null && values.TryGetValue(column, out var value) ? Normalise(value) : null;

    private async Task<Dictionary<Guid, JsonElement>> ReadExistingAsync(
        Uri envUri, string bearerToken, ReferenceTable table, CancellationToken cancellationToken)
    {
        // statecode: an inactive row is reported, never silently counted as unchanged. The natural-key column is part of
        // table.Columns for every shipped table; it is unioned in explicitly so a collision check never reads a column it did not select.
        var select = string.Join(
            ",",
            new[] { table.PrimaryKey }.Concat(table.Columns).Append(table.NaturalKey ?? table.PrimaryKey).Append(table.NameColumn ?? table.PrimaryKey).Append("statecode")
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
