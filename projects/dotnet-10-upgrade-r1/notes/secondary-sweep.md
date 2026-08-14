# H6 + Secondary Hit-Site Sweep (task 013, FR-10)

> **Date**: 2026-08-12 · **Task**: 013 (P1 hit-site remediation) · **Rigor**: FULL · **Model**: opus (main session; POML tier sonnet/xhigh)
> **Verdict**: ✅ **10/10 items assessed — ALL n/a. ZERO code changes. BFF builds green (0 errors, 21 warnings = unchanged post-012 baseline).** No behavior regression. No escalation trigger fired.

This is the CLOSED secondary-audit checklist from design §5 + spec FR-10, plus the two P1-sweep items assigned by the task-001 re-scrape follow-up #2 (`notes/breaking-changes-rescrape.md`). Each item has a grep result and an explicit **n/a / fixed** verdict. Like H1 (task 010), the codebase already sits on the safe side of every one of these .NET 9/10/C# 14 changes.

---

## Method

- Greps run against `src/server/**` (and solution-wide `*.csproj` where the item is package-scoped).
- Definitive compiler checks via `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release` on net10 — **Build succeeded, 0 Error(s), 21 Warning(s)**, and critically **0× CS9258/CS9259** (the `field`-keyword breaking-change diagnostics) and **0** overload-resolution warnings.
- App Service / infra items audited against `infra/**` + `.github/**` (config/precedence items coordinate with task 050 evidence-gathering).

---

## Closed checklist (design §5 / FR-10)

| # | Item (.NET version) | Grep / evidence | Verdict |
|---|---|---|---|
| 1 | **H6** — `HttpClientFactory` `SocketsHttpHandler` primary-handler cast + named-client header/query log redaction (.NET 9) | `ConfigurePrimaryHttpMessageHandler` in `src/server` → **0 matches** | **n/a** |
| 2 | **System.Linq.Async** package ref → CS0121 ambiguity (`AsyncEnumerable` moved inbox, .NET 10) | `System.Linq.Async` in solution `*.csproj` → **0 matches** | **n/a** |
| 3 | **`field` C# 14 contextual keyword** inside property accessors | `\bfield\b` in `src/server/**/*.cs` → only comments/strings ("5-field", "Primary field") + method-body locals (`foreach (var field ...)`, `var field = tokens[0]` in `DataverseServiceClientImpl.cs`); **none inside a property get/set/init accessor**. Definitive: net10 build **0× CS9258/CS9259** | **n/a** |
| 4 | **`IPNetwork` / `ForwardedHeadersOptions.KnownNetworks` → `KnownIPNetworks`** (.NET 9 obsolete) | `IPNetwork\|KnownNetworks` in `src/server` → **0 matches** (no forwarded-headers KnownNetworks config in code) | **n/a** |
| 5 | **`IExceptionHandler.TryHandleAsync` returning `true` now suppresses error-log + diagnostics metrics** (.NET 10) | No `IExceptionHandler` implementation / `AddExceptionHandler<T>` exists. Only lambda-based `app.UseExceptionHandler(...)` with an **explicit** `logger.LogError(...)` at [`MiddlewarePipelineExtensions.cs:29-60`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs#L29). The single `IExceptionHandlerFeature` hit (line 33) is the feature accessor inside that lambda, **not** the `IExceptionHandler` interface the change targets. | **n/a** — logging fully app-controlled + explicit; `ExceptionHandlerOptions.SuppressDiagnosticsCallback` opt-out **not needed** |
| 6 | **Configuration `null` now preserved (not coerced to `""`); empty arrays bind empty** (.NET 10) | 2 literal nulls, BOTH in `appsettings.template.json` (`Redis:ConnectionString`, `Graph:ManagedIdentity:ClientId`) which is `CopyToPublishDirectory="Never"` ([csproj:197](../../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L197)) → **not runtime-loaded** (it's the deploy-time token-substitution source). `appsettings.Testing.json` → 0 nulls. Both keys' consumers are null-tolerant: `CacheModule.cs:59-62` (`?? ` + `IsNullOrWhiteSpace`), `GraphClientFactory.cs:118` / `GraphOptionsValidator.cs:15` / `ManagedIdentityCredentialFactory.cs:31-37` / `StartupValidationService.cs:86` (all `IsNullOrWhiteSpace` / `?? ""`). `IsNullOrWhiteSpace` treats `null` and `""` identically. | **n/a** — behavior-preserving; no code relies on null→`""` coercion |
| 7 | **`DOTNET_*` App Service app settings now override runtimeconfig** (.NET 9 precedence change) | `infra/**` → **0** `DOTNET_*` app settings. `.github/**` hits are CI-runner env vars (`DOTNET_NOLOGO`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`, `DOTNET_CLI_TELEMETRY_OPTOUT`) + `DOTNET_VERSION:'8.x'` (a CI `setup-dotnet` var, **task 040's** scope). No App Service application setting shadows `runtimeconfig`. | **n/a (runtime)** — see coordination note below |
| 8 | **`MailAddress` now rejects consecutive dots** (.NET 10) | 2 sites, both **outbound-recipient validators** using round-trip equality: [`DailyBriefingEndpoints.cs:389`](../../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs#L389) `IsValidEmailAddress` (`MailAddress.TryCreate` + `parsed.Address == value`), [`EmailExportService.cs:423`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Export/EmailExportService.cs#L417) `IsValidEmail` (`new MailAddress(email)` in try/catch + `addr.Address == email`). **No inbound sender-address parsing via `MailAddress`.** Stricter parsing only makes RFC-invalid consecutive-dot addresses fail these defensive guards — the guards' intended purpose. | **n/a** — escalation trigger did **not** fire (no real inbound address path breaks) |

## Closed checklist (re-scrape follow-up #2 — assigned to this task)

| # | Item | Grep / evidence | Verdict |
|---|---|---|---|
| 9 | **C# 14 overload resolution with `Span`/`ReadOnlySpan` + array overloads** — silent bind shift | net10 build compiles clean; **0** overload-resolution warnings; no ambiguity surfaced at encoding/stream/string call sites | **n/a (assessed-low)** |
| 10 | **`XmlSerializer` no longer ignores `[Obsolete]` properties** — spot-check `ComposeService.cs` | `XmlSerializer` in `src/server` → **0 matches**. Re-scrape's "only `ComposeService.cs` uses `XmlSerializer`" is **stale** (ComposeService no longer uses it as of 2026-08-12). Concern cannot apply. | **n/a** |

---

## Documented behavior notes (no fix, recorded for future debuggers)

- **H6 log redaction** (item 1): on .NET 9+, `HttpClient` named-client request logging redacts header + query values to `*` by default. Anyone debugging outbound Graph/Dataverse HTTP via those logs will see redacted values — this is framework default, not a Spaarke config. No action.
- **MailAddress** (item 8): both validators are now marginally stricter (reject consecutive-dot local parts, which are RFC-5321-invalid unquoted). This is a validation *tightening* on outbound recipients only; acceptable and arguably more correct. If a future real recipient with a quoted consecutive-dot local part is ever needed, revisit — not a current path.

## Coordination / downstream (NOT task-013 fixes)

- **Task 050 (P5 evidence)**: confirm `spaarke-bff-dev` App Service has **no** `DOTNET_*` application settings that would override `runtimeconfig` (item 7 runtime confirmation on the live env). Code/infra are clean; only a live-env spot-check remains.
- **Task 040 (CI)**: `DOTNET_VERSION:'8.x'` in `deploy-bff-api.yml:47` + `deploy-promote.yml:58` → bump with the `setup-dotnet` 8.x→10.x work. Out of scope here.
- **Task 031/032 (hygiene)**: 2× residual **NU1510** on the BFF (`Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Caching.Abstractions` — "will not be pruned / likely unnecessary"). Pre-existing post-004 (H4/P0 pin hygiene), **not** a behavioral secondary-sweep item. Noted for the re-baseline/CVE pass to decide on removal. 013 does not touch package refs.
- **Task 051 (P5 smoke)**: `ActivitySource` sampling + W3C trace-context propagator (re-scrape #1) — the only genuinely-new production-path telemetry change; deferred to slot smoke to be **proven**, correctly out of this reasoning-only sweep.

---

## Acceptance criteria (FR-10) — all met

- ✅ All ~8 design-§5 items + 2 re-scrape items assessed (10/10), each with a grep result and explicit n/a/fixed verdict (closed set — no illustrative subset).
- ✅ Every verdict is n/a → zero fixes → nothing to regress; BFF builds green (0 errors).
- ✅ `IExceptionHandler` diagnostics-suppression + named-client log-redaction behaviors explicitly assessed and documented.
- ✅ Negative: no item left unassessed.

**Quality gates (Step 9.5)**: N/A — task produced **zero source/test changes** (only this notes doc). code-review + adr-check have no code delta to evaluate (same disposition as task 010/H1). No ADR surface touched.
