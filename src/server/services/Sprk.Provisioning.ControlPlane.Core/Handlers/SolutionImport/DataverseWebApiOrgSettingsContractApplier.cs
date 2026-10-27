// -----------------------------------------------------------------------------
// DataverseWebApiOrgSettingsContractApplier.cs
//
// Task 253 (G38) — production <see cref="IOrgSettingsContractApplier"/>.
// Replaced PacOrgSettingsContractApplier (`pac org list-settings` /
// `pac org update-settings`): the L2 Worker host has no pac CLI.
//
// WHAT IT DOES (F14): the org-settings contract names columns of the
// environment's single `organization` row (today `maxuploadfilesize`, which a
// fresh environment holds at 5 MB — too small for the
// UniversalDocumentUpload PCF bundle). One GET reads the row; a setting
// already at or above its target is left alone (numeric compare — an
// environment at 30 MB is never lowered to 25 MB); the rest go in ONE PATCH
// of `organizations({organizationid})` with `If-Match: *` (update only, never
// an upsert). A target of -1 (Dataverse's "unlimited" — auditretentionperiodv2
// = Forever) is met only by -1. After the PATCH a second GET reads the row back
// and fails the step on any setting that did not take: Dataverse answers 204 to
// a PATCH whose column it ignores (#2027).
//
// IDENTITY: the same as H6's importer and verifier — the FR-39 ordered
// credential chain (WorkerDataverseCredentialFactory, MI-FIC first: the
// Worker's UAMI acting as the stamp's BFF app registration, which H10 made a
// System Administrator application user — the role that may write
// `organization`). No new identity, permission or secret.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Applies the org-settings contract to the target environment's <c>organization</c> row through the Dataverse Web
/// API — read once, PATCH only the settings below target. Domain failures are a
/// <see cref="OrgSettingsContractOutcome.Failure"/>; cancellation throws.
/// </summary>
/// <remarks>
/// <b>Component justification (CLAUDE.md §11).</b> <i>Existing:</i> the interface, manifest and H6 gate already exist
/// (HANDLER-08); <see cref="DataverseWebApiSolutionVerifier"/> is the Web API + credential pattern reused here.
/// <i>Extension:</i> this replaces the pac implementation behind the same interface — no new seam. <i>Cost of doing
/// nothing:</i> the pac implementation cannot run on the Worker host, so H6 failed every live run before importing.
/// </remarks>
public sealed partial class DataverseWebApiOrgSettingsContractApplier : IOrgSettingsContractApplier
{
    private const string ODataVersion = "4.0";
    private const int DiagnosticTailBudget = 800;

    /// <summary>Dataverse's "unlimited" value for a numeric organization setting (e.g. audit retention Forever).</summary>
    private const long UnlimitedSetting = -1;

    private readonly HttpClient _httpClient;
    private readonly Func<string, string, string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiOrgSettingsContractApplier> _logger;
    private readonly WorkerCredentialSelectionOptions _propagation;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Production constructor (typed-client registration in Worker/Program.cs). The credential is selected by the
    /// FR-39 ordered chain of <see cref="SolutionImportOptions.Credentials"/> — the chain H6's importer and verifier use.
    /// </summary>
    public DataverseWebApiOrgSettingsContractApplier(
        HttpClient httpClient,
        IOptions<SolutionImportOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiOrgSettingsContractApplier> logger,
        TimeProvider? timeProvider = null)
        : this(
            httpClient,
            logger,
            (tenantId, clientId, clientSecret) => credentialFactory
                .Create(
                    options.Value.Credentials,
                    SolutionImportOptions.SectionName,
                    tenantId,
                    clientId,
                    string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret)
                .Credential,
            options.Value.Credentials,
            timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
    }

    /// <summary>
    /// Test seam constructor — injects the credential factory so tests never reach a real credential. Its third
    /// parameter is the secret slot value — EMPTY on secret-free environments (A44.5).
    /// </summary>
    internal DataverseWebApiOrgSettingsContractApplier(
        HttpClient httpClient,
        ILogger<DataverseWebApiOrgSettingsContractApplier> logger,
        Func<string, string, string, TokenCredential> credentialFactory,
        WorkerCredentialSelectionOptions? propagation = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _propagation = propagation ?? new WorkerCredentialSelectionOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<OrgSettingsContractOutcome> ApplyAsync(
        OrgSettingsContractApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentNullException.ThrowIfNull(request.OrgSettings);

        if (request.OrgSettings.Count == 0)
        {
            return new OrgSettingsContractOutcome.Success(request.OrgSettings);
        }

        // The names go into $select and the PATCH body: only column-shaped names (the contract is code, so a bad name
        // is a programming error — refused before any call, never sent).
        var badName = request.OrgSettings.Keys.FirstOrDefault(name => !ColumnName().IsMatch(name));
        if (badName is not null)
        {
            return new OrgSettingsContractOutcome.Failure(
                $"Org-settings contract names '{badName}', which is not an organization column name. Nothing was applied.");
        }

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new OrgSettingsContractOutcome.Failure(
                $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI. Nothing was applied.");
        }

        AccessToken token;
        try
        {
            var credential = _credentialFactory(request.TenantId, request.ClientId, request.ClientSecret ?? string.Empty);
            var scope = new Uri(envUri, "/").ToString().TrimEnd('/') + "/.default";
            // T186 audit: this is H6's FIRST token exchange for the just-created BFF app / FIC — wait out Entra propagation.
            token = await EntraPropagationRetry.GetTokenAsync(
                credential, new TokenRequestContext(new[] { scope }), _propagation, _timeProvider, _logger,
                "H6 org settings", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "H6 org settings: token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
            return new OrgSettingsContractOutcome.Failure(
                $"Token acquisition for '{request.TargetDataverseUrl}' failed: {ex.GetType().Name}: {ex.Message}. Nothing was applied.");
        }

        // (1) Read the organization row once.
        var names = request.OrgSettings.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var select = "organizationid," + string.Join(",", names);
        // T186 audit: H6's first Dataverse call — the H10 application user / role may not be effective yet, so a
        // 401/403 is retried within the shared budget before it becomes the failure below.
        var propagationGate = new DataverseAppUserPropagationGate(_propagation, _timeProvider, _logger, "H6 org settings");
        var orgUri = new Uri(envUri, $"/api/data/v9.2/organizations?$select={select}");
        var read = await SendAsync(
            () => NewRequest(HttpMethod.Get, orgUri, token), "organization GET", request.TargetDataverseUrl,
            propagationGate, cancellationToken).ConfigureAwait(false);
        if (read.Failure is not null)
        {
            return new OrgSettingsContractOutcome.Failure(read.Failure + " Nothing was applied.");
        }

        if (!TryParseOrganization(read.Body, names, out var organizationId, out var current, out var parseFailure))
        {
            return new OrgSettingsContractOutcome.Failure(parseFailure + " Nothing was applied.");
        }

        // (2) Keep what is already at or above target; PATCH the rest in one request.
        var appliedOrAlreadyCorrect = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var toWrite = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var target = request.OrgSettings[name];
            if (current.TryGetValue(name, out var value) && value is not null && SettingAlreadyAtOrAboveTarget(value, target))
            {
                appliedOrAlreadyCorrect[name] = value;
                continue;
            }
            toWrite[name] = target;
        }

        if (toWrite.Count == 0)
        {
            _logger.LogInformation(
                "H6 org settings already satisfied on env={EnvUrl}: {Settings}",
                request.TargetDataverseUrl, string.Join(", ", appliedOrAlreadyCorrect.Select(kv => $"{kv.Key}={kv.Value}")));
            return new OrgSettingsContractOutcome.Success(appliedOrAlreadyCorrect);
        }

        var patchBody = BuildPatchBody(toWrite);
        var written = await SendAsync(
            () =>
            {
                var patch = NewRequest(HttpMethod.Patch, new Uri(envUri, $"/api/data/v9.2/organizations({organizationId})"), token);
                patch.Headers.TryAddWithoutValidation("If-Match", "*");
                patch.Content = new StringContent(patchBody, Encoding.UTF8, "application/json");
                return patch;
            },
            "organization PATCH", request.TargetDataverseUrl, propagationGate, cancellationToken).ConfigureAwait(false);
        if (written.Failure is not null)
        {
            return new OrgSettingsContractOutcome.Failure(
                $"{written.Failure} Settings not applied: {string.Join(", ", toWrite.Select(kv => $"{kv.Key}={kv.Value}"))}.");
        }

        // (3) Read the row back: a PATCH of a column Dataverse ignores still returns 204, so success is what the row
        // now says, not what the PATCH answered (#2027).
        var readBack = await SendAsync(
            () => NewRequest(HttpMethod.Get, orgUri, token), "organization read-back GET", request.TargetDataverseUrl,
            propagationGate, cancellationToken).ConfigureAwait(false);
        var applied = string.Join(", ", toWrite.Select(kv => $"{kv.Key}={kv.Value}"));
        if (readBack.Failure is not null)
        {
            return new OrgSettingsContractOutcome.Failure(
                $"The PATCH of {applied} returned success, but reading it back failed: {readBack.Failure}");
        }
        if (!TryParseOrganization(readBack.Body, names, out _, out var after, out var readBackFailure))
        {
            return new OrgSettingsContractOutcome.Failure(
                $"The PATCH of {applied} returned success, but the read-back is unusable: {readBackFailure}");
        }
        var didNotTake = toWrite
            .Where(kv => !after.TryGetValue(kv.Key, out var value) || value is null
                         || !SettingAlreadyAtOrAboveTarget(value, kv.Value))
            .Select(kv => $"{kv.Key} (target {kv.Value}, reads back " +
                          $"{(after.TryGetValue(kv.Key, out var value) ? value ?? "null" : "absent")})")
            .ToArray();
        if (didNotTake.Length > 0)
        {
            return new OrgSettingsContractOutcome.Failure(
                $"The organization PATCH against '{request.TargetDataverseUrl}' returned success, but the environment " +
                $"did not take: {string.Join("; ", didNotTake)}. Set it in the Power Platform admin center or find why " +
                "the column is refused, then resume.");
        }

        foreach (var (name, target) in toWrite)
        {
            appliedOrAlreadyCorrect[name] = after[name] ?? target;
        }
        _logger.LogInformation(
            "H6 org settings applied on env={EnvUrl}: {Applied} (was {Previous})",
            request.TargetDataverseUrl,
            string.Join(", ", toWrite.Select(kv => $"{kv.Key}={kv.Value}")),
            string.Join(", ", toWrite.Keys.Select(k => $"{k}={(current.TryGetValue(k, out var v) ? v ?? "(null)" : "(absent)")}")));
        return new OrgSettingsContractOutcome.Success(appliedOrAlreadyCorrect);
    }

    /// <summary>
    /// Numeric-first idempotency check: prefer <see cref="long"/> compare (<c>maxuploadfilesize</c> and peers only ever
    /// go up); otherwise case-insensitive equality. A target of -1 is Dataverse's "unlimited"
    /// (<c>auditretentionperiodv2</c> = Forever), the top of the range, so only -1 meets it — a 30-day retention is
    /// below Forever, not above it. Internal for unit tests.
    /// </summary>
    internal static bool SettingAlreadyAtOrAboveTarget(string current, string target)
    {
        if (long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentNumber)
            && long.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetNumber))
        {
            if (targetNumber == UnlimitedSetting || currentNumber == UnlimitedSetting)
            {
                return currentNumber == UnlimitedSetting;
            }
            return currentNumber >= targetNumber;
        }
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The PATCH body: an integer target as a JSON number, <c>true</c>/<c>false</c> as a JSON boolean, anything else
    /// as a string — so a typed column (<c>maxuploadfilesize</c> is an integer) receives its own type. Internal for
    /// unit tests.
    /// </summary>
    internal static string BuildPatchBody(IEnumerable<KeyValuePair<string, string>> settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in settings)
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    writer.WriteNumber(name, number);
                }
                else if (bool.TryParse(value, out var flag))
                {
                    writer.WriteBoolean(name, flag);
                }
                else
                {
                    writer.WriteString(name, value);
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads the single <c>organization</c> row: its id and the current value of each named column (as text; a JSON
    /// null stays null). Internal for unit tests.
    /// </summary>
    internal static bool TryParseOrganization(
        string body,
        IReadOnlyList<string> names,
        out string organizationId,
        out IReadOnlyDictionary<string, string?> current,
        out string failure)
    {
        organizationId = string.Empty;
        current = new Dictionary<string, string?>(StringComparer.Ordinal);
        failure = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() != 1)
            {
                failure = $"The organization GET did not return exactly one row. Body tail: {Truncate(body)}";
                return false;
            }

            var row = rows[0];
            if (!row.TryGetProperty("organizationid", out var id) || id.ValueKind != JsonValueKind.String
                || !Guid.TryParse(id.GetString(), out var organizationGuid))
            {
                failure = $"The organization row carries no organizationid. Body tail: {Truncate(body)}";
                return false;
            }
            organizationId = organizationGuid.ToString("D");

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!row.TryGetProperty(name, out var element))
                {
                    continue;
                }
                values[name] = element.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => element.GetString(),
                    _ => element.GetRawText(),
                };
            }
            current = values;
            return true;
        }
        catch (JsonException ex)
        {
            failure = $"The organization GET response is not JSON: {ex.Message}. Body tail: {Truncate(body)}";
            return false;
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, Uri uri, AccessToken token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private async Task<(string Body, string? Failure)> SendAsync(
        Func<HttpRequestMessage> buildRequest, string what, string environmentUrl,
        DataverseAppUserPropagationGate propagationGate, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await propagationGate.SendAsync(_httpClient, buildRequest, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (body, $"The {what} against '{environmentUrl}' returned {(int)response.StatusCode} " +
                              $"{response.StatusCode}. Body: {Truncate(body)}");
            }
            return (body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "H6 org settings: {What} failed for env={EnvUrl}", what, environmentUrl);
            return (string.Empty, $"The {what} against '{environmentUrl}' failed: {ex.GetType().Name}: {ex.Message}.");
        }
    }

    private static string Truncate(string s)
        => string.IsNullOrEmpty(s) || s.Length <= DiagnosticTailBudget ? s : s[..DiagnosticTailBudget] + "...[truncated]";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnName();
}
