// -----------------------------------------------------------------------------
// DataverseWebApiAiContentSeeder.cs
//
// H12a writer for the AI content set (L56 / data-setup plan D3 / demo plan W2):
// tools, skills, knowledge, actions, playbooks, nodes, N:N links, canvases and
// routing rows, created with their FIXED primary keys from git (AiContentSet).
//
// WHY A NEW WRITER (CLAUDE.md §11):
//   - Existing: DataverseWebApiSeedWriter (H12a) creates 4 flat artifacts BY
//     NAME with generated ids; DataverseWebApiReferenceDataSeeder (H12b) creates
//     flat Tier 1 rows by fixed id. Neither writes lookups, N:N links or the
//     canvas, and playbook/Binding ids must be the source ids.
//   - Extension: the reference seeder is H12b's (another workstream owns it) and
//     its file shape has no lookups or links; the by-name writer cannot keep ids.
//     This writer reuses their Web API idiom (token per env, GET-then-POST,
//     POST with the primary key in the body).
//   - Cost of doing nothing: a stamp has no playbooks (ACME: 0), so Document
//     Profile summaries, file summaries, pre-fill and every routed AI capability
//     fail on every customer stamp.
//
// SEMANTICS (create-if-missing, never an update):
//   - Rows, in AiContentSet.Phases order. A row present by id is left alone;
//     columns that differ from source and an inactive state are REPORTED (a
//     customer may tune prompts), never written. A fixed-id row absent by id but
//     present by a match key (code, name, routing key) under another id is a
//     COLLISION: not created, reported, and its id is used for references so
//     links and routing rows still resolve. Type rows match by name silently.
//     Otherwise POST with the source id, the non-null columns and each lookup as
//     @odata.bind to the (mapped) target row. A create can never update: a row
//     that appears between the read and the POST makes Dataverse reject the
//     duplicate id and the step fails (a re-run finds it).
//   - Links: for each source row this set created or found by id, the missing
//     N:N links are associated ($ref POST). Links are never removed, and a
//     customer's own row found only by a match key gets no links from us.
//   - Canvas: written only to a playbook carrying the source id whose canvas is
//     empty (fill-if-empty), so a re-run after a failure completes it and a
//     maker's canvas is never replaced.
//   - Missing dependency: AiContentSet.Validate() runs first; if any lookup,
//     link or canvas points at a row the set does not carry, or at a table that
//     is written later, the seed is REFUSED with no Dataverse call at all.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>Production <see cref="IAiContentSeeder"/>: Dataverse Web API, create-if-missing, fixed primary keys.</summary>
public sealed class DataverseWebApiAiContentSeeder : IAiContentSeeder
{
    private const string ODataVersion = "4.0";
    private const int MaxListed = 15;

    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiAiContentSeeder> _logger;
    private readonly Func<AiContentSet> _content;

    /// <summary>Production constructor (typed HttpClient).</summary>
    public DataverseWebApiAiContentSeeder(
        HttpClient httpClient,
        IOptions<AiSeedChainOptions> options,
        ILogger<DataverseWebApiAiContentSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
              () => AiContentSet.Embedded)
    {
    }

    /// <summary>Test seam: injects the credential factory and the content set.</summary>
    internal DataverseWebApiAiContentSeeder(
        HttpClient httpClient,
        IOptions<AiSeedChainOptions> options,
        ILogger<DataverseWebApiAiContentSeeder> logger,
        Func<string, TokenCredential> credentialFactory,
        Func<AiContentSet> content)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(content);
        _httpClient = httpClient;
        _httpClient.Timeout = options.Value.DataverseRequestTimeout;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _content = content;
    }

    /// <inheritdoc/>
    public string ContentHash => _content().ContentHash;

    /// <inheritdoc/>
    public async Task<AiContentSeedOutcome> SeedAsync(SeedManifestInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        AiContentSet content;
        try
        {
            content = _content();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException or KeyNotFoundException)
        {
            return new AiContentSeedOutcome.Refused($"AI content set could not be read: {ex.GetType().Name}: {ex.Message}");
        }

        var problems = content.Validate();
        if (problems.Count > 0)
        {
            return new AiContentSeedOutcome.Refused(
                $"AI content set has {problems.Count} missing dependenc{(problems.Count == 1 ? "y" : "ies")}; nothing was written: " +
                string.Join(" ", problems.Take(MaxListed)) + (problems.Count > MaxListed ? $" (and {problems.Count - MaxListed} more)" : string.Empty));
        }

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new AiContentSeedOutcome.Refused($"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        string token;
        try
        {
            var scope = new Uri(envUri, "/").ToString().TrimEnd('/') + "/.default";
            token = (await _credentialFactory(request.TenantId)
                .GetTokenAsync(new TokenRequestContext([scope]), cancellationToken).ConfigureAwait(false)).Token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AiContentSeedOutcome.Failed($"AI content seed: token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var run = new SeedRun(envUri, token);
        var step = "start";
        try
        {
            foreach (var phase in content.Order)
            {
                switch (phase)
                {
                    case AiContentPhase.Rows rows:
                        step = rows.Table;
                        await SeedRowsAsync(run, content.Tables[rows.Table], cancellationToken).ConfigureAwait(false);
                        break;
                    case AiContentPhase.Links links:
                        foreach (var association in content.Associations.Where(a => a.Source == links.SourceTable))
                        {
                            step = association.Relationship;
                            await SeedLinksAsync(run, content, association, cancellationToken).ConfigureAwait(false);
                        }

                        break;
                    case AiContentPhase.Canvas when content.Canvas is { } canvas:
                        step = "canvas";
                        await SeedCanvasAsync(run, content.Tables[canvas.Table], canvas, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new AiContentSeedOutcome.Failed(
                $"AI content seed FAILED at '{step}': {ex.GetType().Name}: {ex.Message} ({run.Created} row(s) and {run.Linked} link(s) " +
                "created before the failure.) Existing rows are never written; a re-run reads each table again and continues.");
        }

        if (run.Reported.Count > 0)
        {
            _logger.LogWarning(
                "H12a AI content: {Count} row(s) differ from source, are inactive, or collide on a key (report only, nothing overwritten): customer={CustomerId} {Items}",
                run.Reported.Count, request.CustomerId, string.Join("; ", run.Reported.Take(MaxListed)));
        }

        var tables = string.Join(", ", content.Tables.Values.Select(t => $"{t.Table} {t.Rows.Count}"));
        var reported = run.Reported.Count == 0
            ? "nothing to report"
            : $"{run.Reported.Count} reported (left unchanged): {string.Join("; ", run.Reported.Take(5))}" +
              (run.Reported.Count > 5 ? $"; and {run.Reported.Count - 5} more" : string.Empty);
        return new AiContentSeedOutcome.Success(
            $"AI content OK ({tables}): {run.Created} row(s) created, {run.Present} present, {run.Linked} link(s) created, " +
            $"{run.LinksPresent} present, {run.Canvases} canvas(es) written; {reported}.");
    }

    /// <summary>State of one seed: per-table source-id → environment-id map and counters.</summary>
    private sealed class SeedRun(Uri envUri, string token)
    {
        public Uri EnvUri { get; } = envUri;
        public string Token { get; } = token;
        public Dictionary<string, Dictionary<Guid, Guid>> IdMap { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> EntitySets { get; } = new(StringComparer.Ordinal);
        public int Created { get; set; }
        public int Present { get; set; }
        public int Linked { get; set; }
        public int LinksPresent { get; set; }
        public int Canvases { get; set; }
        public List<string> Reported { get; } = [];
    }

    private async Task SeedRowsAsync(SeedRun run, AiContentTable table, CancellationToken cancellationToken)
    {
        run.EntitySets[table.Table] = table.EntitySet;
        var map = new Dictionary<Guid, Guid>();
        run.IdMap[table.Table] = map;

        var select = string.Join(",", new[] { table.PrimaryKey }.Concat(table.Columns).Append("statecode").Distinct(StringComparer.Ordinal));
        var existing = await ReadAllAsync(run, $"{table.EntitySet}?$select={select}", cancellationToken).ConfigureAwait(false);
        var byId = new Dictionary<Guid, JsonElement>();
        foreach (var row in existing)
        {
            byId[Guid.Parse(row.GetProperty(table.PrimaryKey).GetString()!, CultureInfo.InvariantCulture)] = row;
        }

        // Match-key indexes over the rows that were there BEFORE this seed (rows created now never shadow later source rows).
        var indexes = table.MatchKeys.Select(key =>
        {
            var index = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var (id, row) in byId)
            {
                if (KeyOf(key, column => row.TryGetProperty(column, out var v) ? v : default) is { } k)
                {
                    index.TryAdd(k, id);
                }
            }

            return (key, index);
        }).ToList();

        foreach (var row in table.Rows)
        {
            if (byId.TryGetValue(row.Id, out var current))
            {
                map[row.Id] = row.Id;
                run.Present++;
                var findings = new List<string>();
                var differing = table.Columns.Where(c => Canonical(row.Values[c]) != Canonical(current.TryGetProperty(c, out var v) ? v : default)).ToList();
                if (differing.Count > 0)
                {
                    findings.Add($"differs in {string.Join(", ", differing)}");
                }

                if (current.TryGetProperty("statecode", out var state) && state.ValueKind == JsonValueKind.Number && state.GetInt32() != 0)
                {
                    findings.Add("is INACTIVE");
                }

                if (findings.Count > 0)
                {
                    run.Reported.Add($"{table.Table}/{row.Id} '{row.DisplayName}' {string.Join(" and ", findings)}");
                }

                continue;
            }

            Guid? matched = null;
            string? matchedBy = null;
            foreach (var (key, index) in indexes)
            {
                if (KeyOf(key, column => row.Values[column]) is { } k && index.TryGetValue(k, out var other))
                {
                    matched = other;
                    matchedBy = string.Join("+", key);
                    break;
                }
            }

            if (matched is { } otherId)
            {
                map[row.Id] = otherId;
                run.Present++;
                if (table.Match == AiContentMatch.FixedId)
                {
                    run.Reported.Add($"{table.Table} '{row.DisplayName}': a row with the same {matchedBy} exists under id {otherId}; " +
                                     $"source id {row.Id} was NOT created (COLLISION) and references use the existing row");
                }

                continue;
            }

            await CreateAsync(run, table, row, cancellationToken).ConfigureAwait(false);
            map[row.Id] = row.Id;
            run.Created++;
        }
    }

    private async Task CreateAsync(SeedRun run, AiContentTable table, AiContentRow row, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { [table.PrimaryKey] = row.Id.ToString() };
        foreach (var column in table.Columns)
        {
            if (row.Values[column].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                body[column] = row.Values[column];
            }
        }

        foreach (var lookup in table.Lookups)
        {
            if (row.Lookups[lookup.Column] is not { } target)
            {
                continue;
            }

            // Validate() guarantees the target table is written first and carries the row; a miss here is a seeder defect.
            if (!run.IdMap.TryGetValue(lookup.Target, out var targetMap) || !targetMap.TryGetValue(target, out var mapped))
            {
                throw new InvalidOperationException(
                    $"{table.Table}/{row.Id} '{row.DisplayName}': {lookup.Target}/{target} ({lookup.Column}) is not present in the environment.");
            }

            body[$"{lookup.NavigationProperty}@odata.bind"] = $"/{run.EntitySets[lookup.Target]}({mapped})";
        }

        using var request = BuildRequest(HttpMethod.Post, new Uri(run.EnvUri, $"/api/data/v9.2/{table.EntitySet}"), run.Token);
        request.Content = JsonContent.Create(body);
        await SendAsync(request, $"POST {table.EntitySet} ({row.Id} '{row.DisplayName}')", cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedLinksAsync(SeedRun run, AiContentSet content, AiContentAssociation association, CancellationToken cancellationToken)
    {
        var sourceTable = content.Tables[association.Source];
        var targetTable = content.Tables[association.Target];
        var sourceMap = run.IdMap[association.Source];
        var targetMap = run.IdMap[association.Target];

        foreach (var group in association.Pairs.GroupBy(p => p.Source))
        {
            // Only rows that carry the source id are ours to link; a customer's row found by a match key is left as it is.
            if (sourceMap[group.Key] != group.Key)
            {
                continue;
            }

            var linked = await ReadAllAsync(
                run, $"{sourceTable.EntitySet}({group.Key})/{association.NavigationProperty}?$select={targetTable.PrimaryKey}", cancellationToken)
                .ConfigureAwait(false);
            var present = linked.Select(l => Guid.Parse(l.GetProperty(targetTable.PrimaryKey).GetString()!, CultureInfo.InvariantCulture)).ToHashSet();

            foreach (var (_, target) in group)
            {
                var mapped = targetMap[target];
                if (present.Contains(mapped))
                {
                    run.LinksPresent++;
                    continue;
                }

                using var request = BuildRequest(
                    HttpMethod.Post,
                    new Uri(run.EnvUri, $"/api/data/v9.2/{sourceTable.EntitySet}({group.Key})/{association.NavigationProperty}/$ref"),
                    run.Token);
                request.Content = JsonContent.Create(new Dictionary<string, string>
                {
                    ["@odata.id"] = new Uri(run.EnvUri, $"/api/data/v9.2/{targetTable.EntitySet}({mapped})").ToString(),
                });
                await SendAsync(request, $"{association.Relationship} {group.Key} -> {mapped}", cancellationToken).ConfigureAwait(false);
                run.Linked++;
            }
        }
    }

    private async Task SeedCanvasAsync(SeedRun run, AiContentTable table, AiContentCanvas canvas, CancellationToken cancellationToken)
    {
        var map = run.IdMap[canvas.Table];
        foreach (var (id, value) in canvas.Rows)
        {
            if (map[id] != id)
            {
                continue; // a customer's playbook found by a match key keeps its own canvas
            }

            var current = await ReadAllAsync(
                run, $"{table.EntitySet}?$select={canvas.Column}&$filter={table.PrimaryKey} eq {id}", cancellationToken).ConfigureAwait(false);
            if (current.Count == 1 && current[0].TryGetProperty(canvas.Column, out var existing)
                && existing.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(existing.GetString()))
            {
                continue;
            }

            using var request = BuildRequest(HttpMethod.Patch, new Uri(run.EnvUri, $"/api/data/v9.2/{table.EntitySet}({id})"), run.Token);
            request.Headers.Add("If-Match", "*"); // update only; never upsert-create a playbook here
            request.Content = JsonContent.Create(new Dictionary<string, string> { [canvas.Column] = value });
            await SendAsync(request, $"PATCH {table.EntitySet}({id}) {canvas.Column}", cancellationToken).ConfigureAwait(false);
            run.Canvases++;
        }
    }

    private async Task<List<JsonElement>> ReadAllAsync(SeedRun run, string relative, CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        Uri? next = new(run.EnvUri, $"/api/data/v9.2/{relative}");
        while (next is not null)
        {
            using var request = BuildRequest(HttpMethod.Get, next, run.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GET {relative} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text)}");
            }

            using var doc = JsonDocument.Parse(text);
            rows.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(r => r.Clone()));
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { Length: > 0 } nextLink
                ? new Uri(nextLink)
                : null;
        }

        return rows;
    }

    private async Task SendAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{what} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text)}");
        }
    }

    /// <summary>A row's key for one match key (columns joined; trimmed, case-folded); null when every part is empty.</summary>
    private static string? KeyOf(IReadOnlyList<string> columns, Func<string, JsonElement> value)
    {
        var parts = columns.Select(c => value(c) is { ValueKind: JsonValueKind.String } v ? v.GetString()!.Trim().ToUpperInvariant() : string.Empty).ToList();
        return parts.All(p => p.Length == 0) ? null : string.Join("\u001f", parts);
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => (value.GetString() ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal),
        JsonValueKind.Number => value.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
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

    private static string Truncate(string s) => string.IsNullOrEmpty(s) || s.Length <= 400 ? s : s[..400] + "...[truncated]";
}
