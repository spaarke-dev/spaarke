# Pending DI Registrations — AIPL-012

> **Task**: AIPL-012 — Implement DocumentParserRouter + LlamaParseClient with Fallback
> **Created**: 2026-02-23
> **Status**: Pending — add these lines to `Program.cs` when wiring AI module

---

## Summary

These registrations are encapsulated in `AiModule.AddAiModule()` (see
`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`). Add the following
call to `Program.cs` inside the `analysisEnabled && documentIntelligenceEnabled` block
(or at the top level if the module should be active whenever the BFF starts):

```csharp
// AI Platform Foundation — DocumentParserRouter + LlamaParseClient (AIPL-012)
builder.Services.AddAiModule(builder.Configuration);
```

---

## Individual Registration Lines

If you prefer to inline rather than use the module, the equivalent registrations are:

```csharp
// --- LlamaParseClient (IHttpClientFactory pattern — ADR-010) ---
// Named HTTP client; base address from LlamaParse:BaseUrl in appsettings.
builder.Services.AddHttpClient(LlamaParseClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["LlamaParse:BaseUrl"] ?? "https://api.cloud.llamaindex.ai");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(300);
});

// LlamaParseClient singleton — resolved via IHttpClientFactory (named client above).
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.LlamaParseClient>();

// --- DocumentIntelligenceService (thin ITextExtractor wrapper — ADR-010) ---
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.DocumentIntelligenceService>();

// --- DocumentParserRouter (concrete singleton — ADR-010) ---
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.DocumentParserRouter>();
```

---

## ADR-010 DI Count Impact

| Before AIPL-012 | Registrations added | After AIPL-012 |
|-----------------|---------------------|----------------|
| 89              | +3 (LlamaParseClient, DocumentIntelligenceService, DocumentParserRouter) | 92 |

The named `AddHttpClient` call is framework infrastructure and is not counted toward
the ≤ 15 non-framework lines limit per ADR-010.

---

## Prerequisites

The following must already be registered before calling `AddAiModule`:

- `ITextExtractor` — registered in `Program.cs` when `DocumentIntelligence:Enabled = true`
- `IOptions<LlamaParseOptions>` — registered via `builder.Services.Configure<LlamaParseOptions>(...)`
  in `Program.cs` (added by task AIPL-004, line ~710)
- `IConfiguration` — registered by the ASP.NET Core host automatically
