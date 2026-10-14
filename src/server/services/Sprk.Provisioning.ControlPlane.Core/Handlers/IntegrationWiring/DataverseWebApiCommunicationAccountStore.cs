// -----------------------------------------------------------------------------
// DataverseWebApiCommunicationAccountStore.cs
//
// Task 263 — production ICommunicationAccountStore: Dataverse Web API v9.2 against sprk_communicationaccounts.
//
// AUTH: the SAME identity H6/H7/H7b sign in with — the stamp's BFF app registration (a System Administrator application
// user of the environment, H10), credential chosen by the FR-39 ordered chain (WorkerDataverseCredentialFactory over
// EnvVarValuesOptions:Credentials; MI-FIC on secret-free Workers). No new app setting, no new secret (ADR-028 A4).
// Token audience: the environment origin + /.default.
//
// CI coverage: CommunicationAccountWebApi (the HTTP half) is tested against a hand-written HttpMessageHandler
// (DataverseWebApiCommunicationAccountStoreTests — never Mock<HttpMessageHandler>, ADR-038); the token half is the
// factory's own (WorkerDataverseCredentialFactoryTests).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="ICommunicationAccountStore"/>
public sealed class DataverseWebApiCommunicationAccountStore : ICommunicationAccountStore
{
    /// <summary>Named HttpClient for the store's Dataverse calls.</summary>
    public const string HttpClientName = "H14m.DataverseWebApiCommunicationAccountStore";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EnvVarValuesOptions _options;
    private readonly WorkerDataverseCredentialFactory _credentialFactory;
    private readonly ILogger<DataverseWebApiCommunicationAccountStore> _logger;

    /// <summary>Creates the store (reuses H7's EnvVarValuesOptions for the one BFF-app-registration identity).</summary>
    public DataverseWebApiCommunicationAccountStore(
        IHttpClientFactory httpClientFactory,
        IOptions<EnvVarValuesOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiCommunicationAccountStore> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _credentialFactory = credentialFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CommunicationAccountEnsureOutcome> EnsureVerifiedAsync(
        CommunicationAccountTarget target, CommunicationAccountSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spec);
        var api = Open(target, out var failure);
        return api is null
            ? new CommunicationAccountEnsureOutcome.Failure(failure!)
            : await api.EnsureVerifiedAsync(spec, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CommunicationAccountReadOutcome> ReadAsync(
        CommunicationAccountTarget target, string emailAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var api = Open(target, out var failure);
        return api is null
            ? new CommunicationAccountReadOutcome.Failure(failure!)
            : await api.ReadAsync(emailAddress, cancellationToken).ConfigureAwait(false);
    }

    private CommunicationAccountWebApi? Open(CommunicationAccountTarget target, out string? failure)
    {
        if (!Uri.TryCreate(target.DataverseUrl, UriKind.Absolute, out var envUri) || envUri.Scheme != Uri.UriSchemeHttps)
        {
            failure = $"Target Dataverse URL '{target.DataverseUrl}' is not an absolute https URI.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(target.TenantId) || string.IsNullOrWhiteSpace(target.ClientId))
        {
            failure = "The Dataverse target needs TenantId and ClientId (the stamp's BFF app registration).";
            return null;
        }

        var scope = new Uri(envUri, "/").ToString().TrimEnd('/') + "/.default";
        TokenCredential? credential = null;
        async ValueTask<string> TokenAsync(CancellationToken ct)
        {
            credential ??= _credentialFactory.Create(
                _options.Credentials, EnvVarValuesOptions.SectionName, target.TenantId, target.ClientId, _options.ClientSecret).Credential;
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
            return token.Token;
        }

        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.Timeout = _options.RequestTimeout;
        failure = null;
        return new CommunicationAccountWebApi(http, envUri, TokenAsync, _logger);
    }
}

/// <summary>The HTTP half of the store — internal so the tests drive it with a hand-written handler.</summary>
internal sealed class CommunicationAccountWebApi
{
    internal const string EntitySet = "sprk_communicationaccounts";

    internal const string RowSelect =
        "sprk_communicationaccountid,sprk_emailaddress,statecode,sprk_accounttype,sprk_verificationstatus," +
        "sprk_verificationmessage,sprk_sendenabled,sprk_receiveenabled";

    private const string Api = "/api/data/v9.2";
    private static readonly Regex EntityIdPattern = new(@"\(([0-9a-fA-F-]{36})\)\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly Func<CancellationToken, ValueTask<string>> _token;
    private readonly ILogger _logger;

    internal CommunicationAccountWebApi(HttpClient http, Uri envUri, Func<CancellationToken, ValueTask<string>> token, ILogger logger)
    {
        _http = http;
        _apiBase = new Uri(envUri, "/").ToString().TrimEnd('/') + Api;
        _token = token;
        _logger = logger;
    }

    internal async Task<CommunicationAccountReadOutcome> ReadAsync(string emailAddress, CancellationToken ct)
    {
        try
        {
            return new CommunicationAccountReadOutcome.Found(await ReadRowsAsync(emailAddress, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "sprk_communicationaccount read failed for {Address}", emailAddress);
            return new CommunicationAccountReadOutcome.Failure($"Reading {EntitySet} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal async Task<CommunicationAccountEnsureOutcome> EnsureVerifiedAsync(CommunicationAccountSpec spec, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var rows = await ReadRowsAsync(spec.EmailAddress, ct).ConfigureAwait(false);
            if (rows.Count > 1)
            {
                return new CommunicationAccountEnsureOutcome.Conflict(
                    $"{rows.Count} {EntitySet} rows carry '{spec.EmailAddress}' ({string.Join(", ", rows.Select(r => r.Id))}) — the stamp must have exactly one. Nothing written.");
            }
            if (rows.Count == 0)
            {
                var id = await CreateAsync(spec, now, ct).ConfigureAwait(false);
                return new CommunicationAccountEnsureOutcome.Ready(id, Written: true);
            }

            var row = rows[0];
            if (row.StateCode != 0)
            {
                return new CommunicationAccountEnsureOutcome.Conflict(
                    $"The {EntitySet} row {row.Id} for '{spec.EmailAddress}' is inactive (statecode {row.StateCode}) — someone deactivated it. Nothing written.");
            }
            if (row.AccountType is { } type && type != CommunicationAccountValues.SharedAccount)
            {
                return new CommunicationAccountEnsureOutcome.Conflict(
                    $"The {EntitySet} row {row.Id} for '{spec.EmailAddress}' has sprk_accounttype {type}, not Shared Account " +
                    $"({CommunicationAccountValues.SharedAccount}). Nothing written.");
            }
            switch (row.VerificationStatus)
            {
                case CommunicationAccountValues.Verified:
                    return new CommunicationAccountEnsureOutcome.Ready(row.Id, Written: false);
                case CommunicationAccountValues.Failed:
                    return new CommunicationAccountEnsureOutcome.VerificationFailed(row.Id, row.VerificationMessage);
                default:
                    await PatchVerificationAsync(row.Id, spec.VerificationMessage, now, ct).ConfigureAwait(false);
                    return new CommunicationAccountEnsureOutcome.Ready(row.Id, Written: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "sprk_communicationaccount ensure failed for {Address}", spec.EmailAddress);
            return new CommunicationAccountEnsureOutcome.Failure($"Ensuring the {EntitySet} row failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<CommunicationAccountRow>> ReadRowsAsync(string emailAddress, CancellationToken ct)
    {
        // OData string literal: a single quote is doubled. Dataverse compares strings case-insensitively.
        var literal = emailAddress.Replace("'", "''", StringComparison.Ordinal);
        var relative = $"{EntitySet}?$select={RowSelect}&$filter=sprk_emailaddress eq '{Uri.EscapeDataString(literal)}'";
        using var response = await SendAsync(HttpMethod.Get, relative, null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"Reading {EntitySet}", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Reading {EntitySet} returned no 'value' array.");
        }
        return value.EnumerateArray().Select(ToRow).ToArray();
    }

    private static CommunicationAccountRow ToRow(JsonElement e) => new(
        Guid.Parse(e.GetProperty("sprk_communicationaccountid").GetString()!),
        e.TryGetProperty("sprk_emailaddress", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : string.Empty,
        e.TryGetProperty("statecode", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
        Int(e, "sprk_accounttype"),
        Int(e, "sprk_verificationstatus"),
        e.TryGetProperty("sprk_sendenabled", out var send) && send.ValueKind == JsonValueKind.True,
        e.TryGetProperty("sprk_receiveenabled", out var receive) && receive.ValueKind == JsonValueKind.True,
        e.TryGetProperty("sprk_verificationmessage", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null);

    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private async Task<Guid> CreateAsync(CommunicationAccountSpec spec, DateTimeOffset now, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_name"] = spec.Name,
            ["sprk_emailaddress"] = spec.EmailAddress,
            ["sprk_displayname"] = spec.DisplayName,
            ["sprk_accounttype"] = CommunicationAccountValues.SharedAccount,
            ["sprk_authmethod"] = CommunicationAccountValues.AppOnly,
            ["sprk_sendenabled"] = true,
            ["sprk_receiveenabled"] = true,
            ["sprk_isdefaultsender"] = true,
            ["sprk_securitygroupid"] = spec.SecurityGroupId,
            ["sprk_verificationstatus"] = CommunicationAccountValues.Verified,
            ["sprk_lastverified"] = now.UtcDateTime,
            ["sprk_verificationmessage"] = spec.VerificationMessage,
        };
        using var response = await SendAsync(HttpMethod.Post, EntitySet, body, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"Creating the {EntitySet} row", ct).ConfigureAwait(false);
        var entityId = response.Headers.TryGetValues("OData-EntityId", out var values) ? values.FirstOrDefault() : null;
        var match = entityId is null ? null : EntityIdPattern.Match(entityId);
        if (match is not { Success: true } || !Guid.TryParse(match.Groups[1].Value, out var id))
        {
            throw new InvalidOperationException($"Creating the {EntitySet} row: the response carried no OData-EntityId header with the new id.");
        }
        return id;
    }

    private async Task PatchVerificationAsync(Guid id, string message, DateTimeOffset now, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_verificationstatus"] = CommunicationAccountValues.Verified,
            ["sprk_lastverified"] = now.UtcDateTime,
            ["sprk_verificationmessage"] = message,
        };
        // If-Match: * — update only; never an upsert that would recreate a row deleted meanwhile.
        using var response = await SendAsync(HttpMethod.Patch, $"{EntitySet}({id})", body, ct, ifMatchAny: true).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"Updating the verification of {EntitySet}({id})", ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, CancellationToken ct, bool ifMatchAny = false)
    {
        var token = await _token(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, new Uri(_apiBase + "/" + relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        if (ifMatchAny)
        {
            request.Headers.TryAddWithoutValidation("If-Match", "*");
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"{what}: HTTP {(int)response.StatusCode} {response.StatusCode}: {(text.Length > 400 ? text[..400] + "...[truncated]" : text)}");
    }
}
