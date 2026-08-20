// =====================================================================================
// SPIKE 002 — THROWAWAY. DO NOT MERGE.
//
// spaarke-auth-v4-dataverse-MI task 002: prove the OBO chain works when the confidential
// client authenticates with a Managed-Identity-issued client assertion (MI-FIC) instead of
// a client secret.
//
// This file exists ONLY on branch spike/002-obo-mi-fic and is deployed ONLY to the
// spaarke-bff-dev/staging slot. Phase 2 (task 020) builds the real seam; nothing here is
// intended to survive.
//
// Security notes (ADR-015): no token, assertion, or secret material is ever returned or
// logged. Only MSAL error CODES and non-sensitive JWT claims (aud/iss/sub/exp) are surfaced.
// =====================================================================================

using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Sprk.Bff.Api.Api;

internal static class SpikeOboEndpoints
{
    public static void MapSpikeOboEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/spike/obo", RunAsync)
           .RequireAuthorization()
           .WithName("Spike002_Obo")
           .ExcludeFromDescription();
    }

    private static async Task<IResult> RunAsync(
        HttpContext http,
        IConfiguration cfg,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("Spike002");

        var tenantId = cfg["AzureAd:TenantId"] ?? cfg["TENANT_ID"];
        var appRegId = cfg["AzureAd:ClientId"] ?? cfg["API_APP_ID"];
        var uamiClientId = cfg["Graph:ManagedIdentity:ClientId"] ?? cfg["AZURE_CLIENT_ID"];
        var envUrl = (cfg["Dataverse:ServiceUrl"] ?? cfg["Dataverse:EnvironmentUrl"])?.TrimEnd('/');
        var secret = cfg["AzureAd:ClientSecret"] ?? cfg["API_CLIENT_SECRET"];

        // The inbound bearer token IS the user assertion for OBO.
        var authHeader = http.Request.Headers.Authorization.ToString();
        var userToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        var results = new List<object>();
        var env = new
        {
            tenantId_set = !string.IsNullOrWhiteSpace(tenantId),
            appRegId,
            uamiClientId,
            envUrl,
            secret_present = !string.IsNullOrWhiteSpace(secret),
            user = http.User.Identity?.Name ?? http.User.FindFirst("preferred_username")?.Value,
            graphScope = "https://graph.microsoft.com/.default",
            dataverseScope = envUrl is null ? null : $"{envUrl}/.default"
        };

        if (userToken is null || tenantId is null || appRegId is null)
        {
            return Results.Json(new { ok = false, reason = "missing user token or configuration", env });
        }

        var authority = $"https://login.microsoftonline.com/{tenantId}";
        var graphScopes = new[] { "https://graph.microsoft.com/.default" };
        var dvScopes = envUrl is null ? null : new[] { $"{envUrl}/.default" };

        // -----------------------------------------------------------------------------
        // T0 — introspect the managed-identity assertion itself.
        // Proves WHICH identity is signing, and that the audience is api://AzureADTokenExchange.
        // -----------------------------------------------------------------------------
        ManagedIdentityClientAssertion? mia = null;
        try
        {
            mia = new ManagedIdentityClientAssertion(uamiClientId);
            var assertion = await mia.GetSignedAssertionAsync(
                new AssertionRequestOptions { ClientID = appRegId, TokenEndpoint = authority });
            results.Add(new
            {
                test = "T0_assertion_introspect",
                ok = true,
                note = "assertion minted by the UAMI; claims below (raw assertion NOT returned)",
                claims = DecodeClaims(assertion, "aud", "iss", "sub", "appid", "exp")
            });
        }
        catch (Exception ex)
        {
            results.Add(new { test = "T0_assertion_introspect", ok = false, error = ex.GetType().Name, code = ErrCode(ex), message = Trim(ex.Message) });
        }

        // -----------------------------------------------------------------------------
        // T1 — OBO to Graph/SPE under the MI client assertion. THE CORE QUESTION.
        // -----------------------------------------------------------------------------
        IConfidentialClientApplication? miCca = null;
        if (mia is not null)
        {
            miCca = ConfidentialClientApplicationBuilder
                .Create(appRegId)
                .WithAuthority(authority)
                .WithClientAssertion(opts => mia.GetSignedAssertionAsync(opts))
                .Build();

            results.Add(await OboAsync(miCca, graphScopes, userToken, "T1_mi_assertion_obo_GRAPH", ct));

            // -------------------------------------------------------------------------
            // T2 — OBO to Dataverse user_impersonation under the same assertion.
            // -------------------------------------------------------------------------
            if (dvScopes is not null)
                results.Add(await OboAsync(miCca, dvScopes, userToken, "T2_mi_assertion_obo_DATAVERSE", ct));
            else
                results.Add(new { test = "T2_mi_assertion_obo_DATAVERSE", ok = false, error = "no Dataverse env url configured" });

            // -------------------------------------------------------------------------
            // T3 — long-running OBO (InitiateLongRunningProcessInWebApi + retrieval).
            // This is what the AI/agent paths depend on across turns.
            // -------------------------------------------------------------------------
            try
            {
                // Long-running OBO lives on ILongRunningWebApi, which IConfidentialClientApplication implements.
                var lr = (ILongRunningWebApi)miCca;
                string sessionKey = null!;
                var initBuilder = lr.InitiateLongRunningProcessInWebApi(graphScopes, userToken, ref sessionKey);
                var initResult = await initBuilder.ExecuteAsync(ct);

                var second = await lr.AcquireTokenInLongRunningProcess(graphScopes, sessionKey)
                                     .ExecuteAsync(ct);

                results.Add(new
                {
                    test = "T3_mi_assertion_LONG_RUNNING_obo",
                    ok = true,
                    initSource = initResult.AuthenticationResultMetadata.TokenSource.ToString(),
                    retrievedSource = second.AuthenticationResultMetadata.TokenSource.ToString(),
                    sessionKeyPresent = !string.IsNullOrEmpty(sessionKey),
                    expiresOn = second.ExpiresOn
                });
            }
            catch (Exception ex)
            {
                results.Add(new { test = "T3_mi_assertion_LONG_RUNNING_obo", ok = false, error = ex.GetType().Name, code = ErrCode(ex), message = Trim(ex.Message) });
            }
        }

        // -----------------------------------------------------------------------------
        // T4 — CONTROL: same OBO with the client secret. Proves the harness itself is sound,
        // so a T1/T2 failure cannot be blamed on the test.
        // -----------------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var secretCca = ConfidentialClientApplicationBuilder
                .Create(appRegId)
                .WithAuthority(authority)
                .WithClientSecret(secret)
                .Build();
            results.Add(await OboAsync(secretCca, graphScopes, userToken, "T4_CONTROL_secret_obo_GRAPH", ct));
        }
        else
        {
            results.Add(new { test = "T4_CONTROL_secret_obo_GRAPH", ok = false, error = "no secret configured (expected on a secret-free host)" });
        }

        // -----------------------------------------------------------------------------
        // T5 — NEGATIVE CONTROL: mint the assertion for the WRONG identity (the app
        // registration's clientId instead of the UAMI's). This is the conflation hazard
        // FR-B4 exists to prevent. It MUST fail; if it succeeds, the guard is meaningless.
        // -----------------------------------------------------------------------------
        try
        {
            var wrongMia = new ManagedIdentityClientAssertion(appRegId);
            var wrongCca = ConfidentialClientApplicationBuilder
                .Create(appRegId)
                .WithAuthority(authority)
                .WithClientAssertion(opts => wrongMia.GetSignedAssertionAsync(opts))
                .Build();
            var r = await OboAsync(wrongCca, graphScopes, userToken, "T5_NEGATIVE_wrong_identity", ct);
            results.Add(r);
        }
        catch (Exception ex)
        {
            results.Add(new { test = "T5_NEGATIVE_wrong_identity", ok = false, expectedToFail = true, error = ex.GetType().Name, code = ErrCode(ex), message = Trim(ex.Message) });
        }

        log.LogInformation("Spike002 completed with {Count} results", results.Count);
        return Results.Json(new { ok = true, env, results }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task<object> OboAsync(
        IConfidentialClientApplication cca,
        string[] scopes,
        string userToken,
        string testName,
        CancellationToken ct)
    {
        try
        {
            var r = await cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken)).ExecuteAsync(ct);
            return new
            {
                test = testName,
                ok = true,
                grantedScopes = r.Scopes?.ToArray(),
                expiresOn = r.ExpiresOn,
                tokenSource = r.AuthenticationResultMetadata.TokenSource.ToString(),
                tokenClaims = DecodeClaims(r.AccessToken, "aud", "iss", "appid", "scp", "upn", "oid")
            };
        }
        catch (MsalServiceException ex)
        {
            return new { test = testName, ok = false, error = "MsalServiceException", code = ex.ErrorCode, aadsts = FirstAadsts(ex.Message), statusCode = ex.StatusCode, message = Trim(ex.Message) };
        }
        catch (Exception ex)
        {
            return new { test = testName, ok = false, error = ex.GetType().Name, code = ErrCode(ex), aadsts = FirstAadsts(ex.Message), statusCode = 0, message = Trim(ex.Message) };
        }
    }

    /// <summary>Decodes selected non-sensitive claims from a JWT. Never returns the raw token.</summary>
    private static Dictionary<string, string> DecodeClaims(string jwt, params string[] wanted)
    {
        var outp = new Dictionary<string, string>();
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return outp;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            foreach (var w in wanted)
                if (doc.RootElement.TryGetProperty(w, out var v))
                    outp[w] = v.ToString();
        }
        catch { /* introspection is best-effort evidence only */ }
        return outp;
    }

    private static string? ErrCode(Exception ex) => ex is MsalException m ? m.ErrorCode : null;

    private static string? FirstAadsts(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return null;
        var i = msg.IndexOf("AADSTS", StringComparison.Ordinal);
        if (i < 0) return null;
        var end = i;
        while (end < msg.Length && (char.IsLetterOrDigit(msg[end]))) end++;
        return msg[i..end];
    }

    private static string Trim(string? m) =>
        string.IsNullOrEmpty(m) ? "" : (m.Length > 600 ? m[..600] : m);
}
