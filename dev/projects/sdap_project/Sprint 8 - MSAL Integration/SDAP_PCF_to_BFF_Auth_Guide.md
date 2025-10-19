# SDAP PCF to BFF Authentication Guide

This guide documents how a Power Apps PCF control securely calls the Spe.Bff.Api backend in the SDAP architecture, and how the backend uses On-Behalf-Of (OBO) and app-only access to Microsoft Graph for SharePoint Embedded (SPE). It is designed for engineers and for AI code agents such as Claude Code.

## Summary

For SDAP, the PCF control must **call the BFF (Spe.Bff.Api)** with a user access token issued for the BFF’s audience. The BFF validates the token, enforces Dataverse Unified Access Control (UAC), and then either:
- Obtains a **user-delegated Graph token** via OBO for read operations, or
- Uses an **app-only Graph client** for privileged mutations and container lifecycle.

Direct PCF to Graph calls are **not** used in SDAP because they bypass UAC, leak elevated permissions to the browser, and weaken audit and resilience.

## Why the BFF is required

The BFF is the policy and safety boundary for SDAP. It is responsible for:
- Enforcing Dataverse UAC and per-document authorization with `AuthorizationService` and endpoint filters.
- Applying least privilege through dual authentication. Read paths can use OBO; write/admin paths use app-only Graph scopes.
- Resolving business document identifiers to the correct SPE container and drive.
- Centralizing audit, correlation, error shaping (ProblemDetails), and throttling with retries and backoff.
- Issuing short-lived handles for downloads and orchestrating background work (OCR, indexing) via Service Bus.

## Token flow overview

PCF acquires a user token for the BFF audience and calls the BFF. The BFF then performs OBO or uses app-only credentials as needed.

PCF (user) → Bearer token (aud = BFF) → Spe.Bff.Api  
Spe.Bff.Api → AuthorizationService (UAC) → SpeFileStore  
SpeFileStore → either OBO Graph call or app-only Graph call

## Prerequisites and configuration

You need two Azure AD app registrations.

Backend API (BFF):
- Expose API scope, for example `api://<BFF_APP_ID>/SDAP.Access`.
- Configure JWT audience to this API.
- Optional: define app roles if your auth model requires them.

SPA client (PCF host):
- Platform type must be “Single-page application”.
- Redirect URIs must include your Dynamics environment, for example `https://<org>.crm.dynamics.com/main.aspx`.
- API permissions must include delegated access to the BFF scope. Ensure tenant admin consent.

CORS in BFF:
- Allow the host origins of the model driven environment.

## PCF control token acquisition and BFF calls

The PCF control acquires a **user token for the BFF scope**, not a Graph token. It then calls BFF endpoints with `Authorization: Bearer <token>`.

```ts
// TypeScript (PCF control) using @azure/msal-browser
import { PublicClientApplication, InteractionRequiredAuthError } from "@azure/msal-browser";

const msal = new PublicClientApplication({
  auth: {
    clientId: "<SPA_CLIENT_APP_ID>",
    authority: "https://login.microsoftonline.com/<TENANT_ID>",
    navigateToLoginRequestUrl: false
  },
  cache: { cacheLocation: "sessionStorage" }
});

const bffScopes = ["api://<BFF_APP_ID>/SDAP.Access"];
const bffBaseUrl = "<https://your-bff-host>";

async function getBffToken(loginHint?: string): Promise<string> {
  try {
    const account = msal.getAllAccounts()[0];
    if (!account && loginHint) {
      const res = await msal.ssoSilent({ loginHint, scopes: bffScopes });
      return res.accessToken;
    }
    const res = await msal.acquireTokenSilent({ account: account ?? undefined, scopes: bffScopes });
    return res.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      await msal.acquireTokenRedirect({ scopes: bffScopes, loginHint });
      return "";
    }
    throw err;
  }
}

export async function callBff(route: string, method: string = "GET", body?: unknown) {
  const token = await getBffToken(/* supply model-driven username as loginHint if available */);
  const res = await fetch(`${bffBaseUrl}${route}`, {
    method,
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json"
    },
    body: body ? JSON.stringify(body) : undefined
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text);
  }
  return res.headers.get("content-type")?.includes("application/json") ? res.json() : res.text();
}
```

Notes:
- The control never requests a Graph token.
- For model-driven apps, `sessionStorage` and redirect flows reduce issues with iframes and third-party cookies.
- Handle ProblemDetails responses and render user-friendly messages.

## BFF configuration for JWT validation and downstream Graph

In Spe.Bff.Api, validate the BFF audience token, set up OBO for user-delegated Graph, and configure an app-only Graph client.

```csharp
// Program.cs (excerpt)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd")); // API app registration

// On-Behalf-Of (user-delegated Graph calls)
builder.Services.AddTokenAcquisition()
    .AddDownstreamApi("Graph", builder.Configuration.GetSection("Graph"));

// App-only Graph client for privileged operations
builder.Services.AddSingleton(sp =>
{
    var opt = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
    var credential = new ClientSecretCredential(opt.TenantId, opt.ClientId, opt.ClientSecret);
    return new GraphServiceClient(credential);
});
```

Use endpoint filters to enforce UAC before touching storage, and keep Graph code confined to the `SpeFileStore` facade.

## SpeFileStore usage of OBO and app-only

```csharp
public sealed class SpeFileStore
{
    private readonly GraphServiceClient _graphApp; // app-only
    private readonly IDownstreamApi _graphObo;     // OBO
    private readonly ILogger<SpeFileStore> _log;

    public SpeFileStore(GraphServiceClient graphApp, IDownstreamApi graphObo, ILogger<SpeFileStore> log)
    {
        _graphApp = graphApp;
        _graphObo = graphObo;
        _log = log;
    }

    public async Task<Stream> DownloadAsync(ResourceRef resource, CancellationToken ct)
    {
        // Typically OBO for reads
        using var response = await _graphObo.CallGraphApiForUserAsync("GET", $"v1.0/drives/.../items/{resource.Id}/content", ct);
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(ResourceRef resource, CancellationToken ct)
    {
        // App-only for privileged actions
        await _graphApp.Drives["..."].Items[resource.Id.ToString()].Request().DeleteAsync(ct);
    }
}
```

The facade hides Graph SDK types from the rest of the application and implements retry, correlation, and request ID logging in one place.

## Minimal API endpoint contracts for the PCF

The PCF should call BFF endpoints. These are examples you can adapt.

- POST /api/documents to create a document record and initiate storage mapping.
- GET /api/documents/{id:guid} to get document metadata.
- POST /api/documents/{id:guid}/upload to start an upload session or upload directly.
- GET /api/documents/{id:guid}/download to return a short-lived handle or stream content.
- GET /api/documents/{id:guid}/versions to list versions.
- DELETE /api/documents/{id:guid} to delete a document.

All endpoints must apply per-endpoint authorization filters that call `AuthorizationService`.

## Security guardrails

- Never expose app-only credentials or elevated Graph scopes in the browser.
- Keep Graph SDK usage in `SpeFileStore`. Do not leak Graph types above the facade.
- Enforce UAC via endpoint filters and `AuthorizationService`, not in the client.
- Use Redis as the cross-request cache. A small per-request cache can avoid duplicate loads within one request.
- Return ProblemDetails for errors and include correlation/request IDs for troubleshooting.

## Operational notes

- Configure CORS in the BFF to allow your Dynamics environment hosts.
- Use short-lived download handles and time-box upload sessions.
- Centralize throttling and retries in the facade, not in the PCF.

## Acceptance checks

- The PCF sends Authorization: Bearer <BFF token> and never requests Graph tokens.
- Spe.Bff.Api validates JWTs, performs UAC checks, and then calls Graph using OBO or app-only as appropriate.
- No Graph types or tokens are present in the PCF.
- Endpoints return 401/403 consistently and include stable reason codes on denies.
- Background workers start successfully and health endpoint responds.

## Claude Code one-shot prompt

Use the prompt below with paths adjusted for your environment if necessary. Claude Code can open files, run PowerShell, and produce diffs.

```
You are Claude Code, acting as a senior C# and Microsoft full-stack engineer for SDAP. Use the local workspace; do not request uploads.

1) Read these files:
- .\docs\adr\ADR-001-*.md .. ADR-010-*.md
- .\docs\guides\SDAP_Architecture_Simplification_Guide.md
- .\docs\guides\SDAP_Refactor_Playbook_v2.md
- .\docs\specs\*.docx
- .\docs\snippets\*.cs

2) Confirm the PCF→BFF pattern described in "SDAP PCF to BFF Authentication Guide":
- PCF acquires a user token for the BFF scope (not Graph) and calls BFF endpoints.
- Spe.Bff.Api validates JWT, enforces UAC via endpoint filters, then uses OBO for reads or app-only Graph for privileged writes via SpeFileStore.

3) Implement or verify:
- MSAL in PCF to acquire BFF token (Typescript snippet) and fetch to /api/documents endpoints.
- Program.cs configuration for MicrosoftIdentityWebApi, TokenAcquisition (OBO), and app-only GraphServiceClient.
- SpeFileStore facade with OBO and app-only methods.
- Endpoint filters for resource authorization calling AuthorizationService.

4) Output:
- Updated code snippets or diffs for PCF and Spe.Bff.Api files.
- A short verification checklist: obtain token in PCF, call BFF, validate 401/403, OBO read works, app-only delete works.
```
