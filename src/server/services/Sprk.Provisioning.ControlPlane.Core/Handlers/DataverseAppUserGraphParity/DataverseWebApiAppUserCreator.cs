// -----------------------------------------------------------------------------
// DataverseWebApiAppUserCreator.cs
//
// Production IDataverseAppUserCreator — issues Dataverse Web API calls to
// find or create the customer's business unit (T259) and to upsert an
// Application User IN it (find-by-applicationid, else create) with the
// requested security role — the copy of that role in the same unit.
// Auth via DefaultAzureCredential (ADR-028 MI-outbound MUST rule; §4D I5
// explicit per-tenant scope — never a default-tenant credential); the internal
// constructor takes a credential factory so the request shapes are CI-tested
// against a hand-written HttpMessageHandler (DataverseWebApiAppUserCreatorTests —
// never Mock<HttpMessageHandler>, ADR-038).
//
// T259 (ISS-010 / #1486, owner decision 2026-10-09 — INCOMING-145 §6 T1/T3):
//   EnsureCustomerBusinessUnitAsync — GET the root (exactly one), GET units by
//   name ($top=2): two → Ambiguous; one under the root → reuse; one anywhere
//   else (or the root itself) → WrongParent; none → POST businessunits under
//   the root. Never re-parents a unit.
//   EnsureAppUserAsync — an existing App User in another unit is refused
//   (InForeignBusinessUnit, nothing written: a business-unit change strips every
//   role); a new one is created with businessunitid = the customer's unit; the
//   role is resolved in THAT unit (Dataverse assigns a user only its own unit's
//   roles) — exactly one match, never a name-only fallback that could pick a
//   copy from another unit.
//
// auth-v4 §10.4 DUAL APP-USER TRAP (task 205d / punch row A41): the UAMI row's
// azureactivedirectoryobjectid MUST be the UAMI's principalId — NEVER its
// clientId. See DataverseAppUserCreationRequest.SystemUserAzureActiveDirectoryObjectId's
// remarks for the full silent-fail shape. This creator writes that field
// explicitly (in the same POST as applicationid) whenever the caller supplies
// it; it does NOT rely on Dataverse's applicationid-only auto-resolution for
// that field.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IDataverseAppUserCreator"/>
public sealed class DataverseWebApiAppUserCreator : IDataverseAppUserCreator
{
    private const string Api = "/api/data/v9.2/";

    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseWebApiAppUserCreator> _logger;
    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public DataverseWebApiAppUserCreator(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserCreator> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory.</summary>
    internal DataverseWebApiAppUserCreator(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserCreator> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = options.Value.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<CustomerBusinessUnitOutcome> EnsureCustomerBusinessUnitAsync(
        string environmentUrl,
        string tenantId,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            return new CustomerBusinessUnitOutcome.Failure($"Environment URL '{environmentUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new CustomerBusinessUnitOutcome.Failure($"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var roots = Values(await GetAsync(envUri, token,
                "businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid&$top=2", cancellationToken)
                .ConfigureAwait(false));
            if (roots.Count != 1)
            {
                return new CustomerBusinessUnitOutcome.Failure(
                    $"The environment reports {roots.Count} root business units (expected exactly one). Nothing was written.");
            }
            var rootId = Id(roots[0], "businessunitid");

            var named = Values(await GetAsync(envUri, token,
                $"businessunits?$filter=name eq '{Literal(name)}'&$select=businessunitid,_parentbusinessunitid_value&$top=2",
                cancellationToken).ConfigureAwait(false));
            switch (named.Count)
            {
                case > 1:
                    return new CustomerBusinessUnitOutcome.Ambiguous(named.Count);
                case 1:
                {
                    var unitId = Id(named[0], "businessunitid");
                    var parentId = OptionalId(named[0], "_parentbusinessunitid_value");
                    return parentId == rootId
                        ? new CustomerBusinessUnitOutcome.Success(unitId, Created: false)
                        : new CustomerBusinessUnitOutcome.WrongParent(unitId, parentId, rootId);
                }
            }

            var created = await CreateAsync(envUri, token, "businessunits", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["parentbusinessunitid@odata.bind"] = $"/businessunits({rootId:D})",
            }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("H10 created the customer business unit {BusinessUnitId} under the root {RootId}", created, rootId);
            return new CustomerBusinessUnitOutcome.Success(created, Created: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new CustomerBusinessUnitOutcome.Failure("A Dataverse request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataverseWebApiAppUserCreator business-unit fault for env={EnvUrl}", environmentUrl);
            return new CustomerBusinessUnitOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<DataverseAppUserCreationOutcome> EnsureAppUserAsync(
        DataverseAppUserCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SecurityRoleName);

        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"Environment URL '{request.EnvironmentUrl}' is not a valid absolute URI.");
        }

        if (request.BusinessUnitId == Guid.Empty)
        {
            return new DataverseAppUserCreationOutcome.Failure(
                "BusinessUnitId is empty — the App User's business unit (the customer's unit) must be resolved first.");
        }

        // Defense-in-depth: applicationId is interpolated directly into an OData
        // $filter query string below. It is always a machine-produced GUID from
        // InterStepState (H2a/H3 output), never free-text user input, so
        // injection risk is negligible — but validating the shape here produces
        // a clear diagnostic instead of a confusing Dataverse 400.
        if (!Guid.TryParse(request.ApplicationId, out var applicationId))
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"ApplicationId '{request.ApplicationId}' is not a valid GUID — refusing to build an OData filter from it.");
        }

        // auth-v4 §10.4 defense-in-depth: if the caller supplied an explicit
        // azureactivedirectoryobjectid (the UAMI row's principalId — see
        // DataverseAppUserCreationRequest's remarks for the silent-fail trap
        // this guards against), validate its GUID shape before it reaches the
        // POST payload, same rationale as the ApplicationId check above.
        if (request.SystemUserAzureActiveDirectoryObjectId is not null
            && !Guid.TryParse(request.SystemUserAzureActiveDirectoryObjectId, out _))
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"AzureActiveDirectoryObjectId '{request.SystemUserAzureActiveDirectoryObjectId}' is not a valid GUID — " +
                "refusing to write it to the systemuser row.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            // (1) Idempotency: look for an existing App User by applicationid — and the unit it is in.
            var existing = Values(await GetAsync(envUri, token,
                $"systemusers?$filter=applicationid eq {applicationId:D}&$select=systemuserid,_businessunitid_value&$top=2",
                cancellationToken).ConfigureAwait(false));
            if (existing.Count > 1)
            {
                return new DataverseAppUserCreationOutcome.Failure(
                    $"{existing.Count} systemusers carry applicationid {applicationId:D} (expected at most one). Nothing was written.");
            }

            Guid systemUserId;
            if (existing.Count == 1)
            {
                systemUserId = Id(existing[0], "systemuserid");
                var unit = OptionalId(existing[0], "_businessunitid_value");
                if (unit != request.BusinessUnitId)
                {
                    // T259: never moved — a business-unit change strips every role of the user.
                    return new DataverseAppUserCreationOutcome.InForeignBusinessUnit(systemUserId.ToString("D"), unit ?? Guid.Empty);
                }
            }
            else
            {
                // (2) Create the App User IN the customer's unit. Explicit azureactivedirectoryobjectid
                // (when supplied) is written in the SAME POST as applicationid — never a follow-up PATCH —
                // so there is no window where the row exists with an unset/auto-resolved value (auth-v4 §10.4).
                var payload = new Dictionary<string, object?>
                {
                    ["applicationid"] = applicationId.ToString("D"),
                    ["businessunitid@odata.bind"] = $"/businessunits({request.BusinessUnitId:D})",
                };
                if (!string.IsNullOrWhiteSpace(request.SystemUserAzureActiveDirectoryObjectId))
                {
                    payload["azureactivedirectoryobjectid"] = request.SystemUserAzureActiveDirectoryObjectId;
                }
                systemUserId = await CreateAsync(envUri, token, "systemusers", payload, cancellationToken).ConfigureAwait(false);
            }

            // (3) The role's copy IN the user's unit — exactly one.
            var roles = Values(await GetAsync(envUri, token,
                $"roles?$filter=name eq '{Literal(request.SecurityRoleName)}' and _businessunitid_value eq {request.BusinessUnitId:D}" +
                "&$select=roleid&$top=2", cancellationToken).ConfigureAwait(false));
            if (roles.Count != 1)
            {
                return new DataverseAppUserCreationOutcome.Failure(roles.Count == 0
                    ? $"Security role '{request.SecurityRoleName}' not found in business unit {request.BusinessUnitId:D}."
                    : $"More than one role named '{request.SecurityRoleName}' in business unit {request.BusinessUnitId:D} — refusing to guess.");
            }
            var roleId = Id(roles[0], "roleid");

            // (4) Ensure role association (idempotent — check-then-insert).
            var held = Values(await GetAsync(envUri, token,
                $"systemusers({systemUserId:D})/systemuserroles_association?$filter=roleid eq {roleId:D}&$select=roleid",
                cancellationToken).ConfigureAwait(false));
            if (held.Count == 0)
            {
                var root = new Uri(envUri, "/").ToString().TrimEnd('/');
                await SendAsync(envUri, token, HttpMethod.Post,
                    $"systemusers({systemUserId:D})/systemuserroles_association/$ref",
                    new Dictionary<string, object?> { ["@odata.id"] = $"{root}{Api}roles({roleId:D})" },
                    cancellationToken).ConfigureAwait(false);
            }

            return new DataverseAppUserCreationOutcome.Success(systemUserId.ToString("D"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DataverseAppUserCreationOutcome.Failure("A Dataverse request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseWebApiAppUserCreator infrastructure fault for applicationId={ApplicationId}",
                request.ApplicationId);
            return new DataverseAppUserCreationOutcome.Failure(
                $"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        var scope = $"{new Uri(envUri, "/").ToString().TrimEnd('/')}/.default";
        return await _credentialFactory(tenantId).GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetAsync(Uri envUri, AccessToken token, string relative, CancellationToken ct)
    {
        using var response = await SendRawAsync(envUri, token, HttpMethod.Get, relative, body: null, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {relative.Split('?')[0]} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task SendAsync(Uri envUri, AccessToken token, HttpMethod method, string relative, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(envUri, token, method, relative, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{method} {relative} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
    }

    /// <summary>POSTs a new row and returns its id from the <c>OData-EntityId</c> header.</summary>
    private async Task<Guid> CreateAsync(Uri envUri, AccessToken token, string entitySet, object body, CancellationToken ct)
    {
        using var response = await SendRawAsync(envUri, token, HttpMethod.Post, entitySet, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST {entitySet} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }

        // Dataverse returns the new row's id in the OData-EntityId response header:
        // "{envUrl}/api/data/v9.2/{entitySet}(guid)".
        var header = response.Headers.TryGetValues("OData-EntityId", out var values) ? values.FirstOrDefault() : null;
        return ExtractGuidFromEntityId(header)
            ?? throw new InvalidOperationException(
                $"POST {entitySet} succeeded but the response carried no parseable OData-EntityId header.");
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        Uri envUri, AccessToken token, HttpMethod method, string relative, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(envUri, Api + relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static List<JsonElement> Values(JsonDocument document)
    {
        using (document)
        {
            if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The Dataverse response has no 'value' array.");
            }
            return value.EnumerateArray().Select(e => e.Clone()).ToList();
        }
    }

    // ADR-044: ids read back from Dataverse are canonicalized before they reach a filter or a reference URL.
    private static Guid Id(JsonElement row, string property)
        => OptionalId(row, property) ?? throw new InvalidOperationException($"A Dataverse row has no usable '{property}'.");

    private static Guid? OptionalId(JsonElement row, string property)
        => row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
           && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty
            ? id
            : null;

    /// <summary>An OData string literal's content: quotes doubled, then URL-escaped.</summary>
    private static string Literal(string value) => Uri.EscapeDataString(value.Replace("'", "''", StringComparison.Ordinal));

    private static Guid? ExtractGuidFromEntityId(string? entityIdHeader)
    {
        if (string.IsNullOrWhiteSpace(entityIdHeader)) return null;
        var openParen = entityIdHeader.LastIndexOf('(');
        var closeParen = entityIdHeader.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen) return null;
        var candidate = entityIdHeader.Substring(openParen + 1, closeParen - openParen - 1);
        return Guid.TryParse(candidate, out var guid) ? guid : null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
