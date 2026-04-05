# Anti-Patterns

> **Last Updated**: 2026-04-05
> **Applies To**: All Spaarke codebases (backend, frontend, deployment, infrastructure)

---

## Rules

1. **MUST NOT** follow any anti-pattern listed below. Each entry describes a specific mistake, explains why it fails, and provides the correct alternative.
2. **MUST** check this document before implementing new features in unfamiliar areas.
3. **SHOULD** cite this document in code reviews when an anti-pattern is detected.

---

## Anti-Pattern Reference

### Architecture

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 1 | **Using Azure Functions for async work** — creating Function App projects with `[FunctionName]` or `[ServiceBusTrigger]` bindings | Duplicates cross-cutting concerns (auth, retries, correlation) across two runtimes; complicates debugging and deployment | Use `BackgroundService` + Service Bus in the single BFF App Service | [ADR-001](.claude/adr/ADR-001-minimal-api.md) |
| 2 | **Creating a separate AI microservice** — deploying AI endpoints in a standalone service outside the BFF | Adds network hops, separate auth, deployment complexity with no isolation benefit | Extend `Sprk.Bff.Api` with AI endpoints following Minimal API patterns | [ADR-013](.claude/adr/ADR-013-ai-architecture.md) |
| 3 | **Global middleware for resource authorization** — `app.UseMiddleware<DocumentSecurityMiddleware>()` | Runs before routing completes; has no access to route values like `documentId` or request body | Use endpoint filters: `.AddEndpointFilter<DocumentAuthorizationFilter>()` | [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) |
| 4 | **Injecting GraphServiceClient directly** — `public class Controller(GraphServiceClient graph)` | Graph SDK types leak above the facade; callers depend on Microsoft.Graph internals | Route all SPE operations through `SpeFileStore` facade; expose only SDAP DTOs | [ADR-007](.claude/adr/ADR-007-spefilestore.md) |
| 5 | **Creating interfaces without a genuine seam** — `services.AddSingleton<IResourceStore, SpeFileStore>()` when only one implementation exists | Adds indirection without value; inflates DI registrations beyond the 15-line budget | Register concretes: `services.AddSingleton<SpeFileStore>()` | [ADR-010](.claude/adr/ADR-010-di-minimalism.md) |

### Dataverse Plugins

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 6 | **HTTP/Graph calls from plugins** — making remote I/O calls inside `IPlugin.Execute()` | Plugins run inside the Dataverse transaction pipeline; remote calls cause timeouts, retries fail silently, and exceed the 50ms p95 budget | Defer all external work to BFF API endpoints or BackgroundService workers | [ADR-002](.claude/adr/ADR-002-thin-plugins.md) |
| 7 | **Business logic in plugins** — implementing orchestration, multi-entity coordination, or branching logic in plugin code | Plugins are not an execution runtime; complex logic is untestable, unobservable, and unrecoverable in the transaction pipeline | Keep plugins < 200 LoC; limit to validation, invariant enforcement, audit stamping | [ADR-002](.claude/adr/ADR-002-thin-plugins.md) |

### Frontend (PCF)

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 8 | **Using `createRoot()` in PCF controls** — importing from `react-dom/client` and calling `createRoot` | Dataverse provides React 16/17 at runtime; React 18 APIs cause `Cannot create property '_updatedFibers'` or `createRoot is not a function` | Use `ReactDOM.render()` or the `ReactControl` pattern that returns `React.ReactElement` | [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) |
| 9 | **Bundling React/Fluent in PCF output** — missing `<platform-library>` declarations in the manifest | Bundle bloats to 5-8MB instead of 200-400KB; duplicates libraries already provided by the platform, causing version conflicts | Declare `<platform-library name="React" />` and `<platform-library name="Fluent" />` in manifest; create `featureconfig.json` with `"pcfReactPlatformLibraries": "on"` | [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) |
| 10 | **Creating legacy JavaScript web resources** — writing no-framework JS, jQuery, or ad hoc scripts for new UI | Unmaintainable, untestable, no type safety, no component reuse, no dark mode support | Use PCF for form-bound controls; use Code Pages for standalone dialogs | [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) |
| 11 | **Hard-coding colors in UI** — using hex (`#ffffff`), rgb, or named CSS colors instead of Fluent tokens | Breaks dark mode and high-contrast themes; creates visual inconsistency across surfaces | Use Fluent design tokens: `tokens.colorNeutralBackground1`, `tokens.colorNeutralForeground1` | [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) |
| 12 | **Using Fluent v8 (`@fluentui/react`)** — importing from the legacy Fluent package or mixing v8 and v9 | v8 components do not support v9 theming, tokens, or `makeStyles`; mixing versions causes bundle bloat and visual inconsistency | Use `@fluentui/react-components` (v9) exclusively; import icons from `@fluentui/react-icons` | [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) |
| 13 | **Async init in PCF class `init()` via `notifyOutputChanged()`** — calling auth/config in PCF `init()` then `notifyOutputChanged()` to trigger re-render | For read-only `ReactControl` with no two-way bound field, `notifyOutputChanged()` does NOT reliably trigger `updateView()` | Move async initialization into a React `useEffect` + `useState` inside the component | [pcf-deploy skill](.claude/skills/pcf-deploy/SKILL.md) |

### Frontend (Code Pages)

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 14 | **Using module-level constants for runtime config** — `const CLIENT_ID = getMsalClientId();` at module scope | Runtime config is not available until bootstrap completes; module-level calls throw before the app renders | Use lazy functions: `export function getMsalConfig() { return { clientId: getMsalClientId() }; }` | [CLAUDE.md](CLAUDE.md) |
| 15 | **Deploying `bundle.js` or `index.html` instead of the inlined HTML** — uploading intermediate build artifacts to Dataverse | `index.html` references `bundle.js` externally and renders a blank page in Dataverse; `bundle.js` alone is not a valid web resource | Run `build-webresource.ps1` to produce `out/sprk_{pagename}.html` (single self-contained file); deploy that file only | [code-page-deploy skill](.claude/skills/code-page-deploy/SKILL.md) |

### Deployment (BFF API)

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 16 | **Publishing BFF to `/tmp` or external directory** — running `dotnet publish -o /tmp/publish` | Packages from external directories are incomplete (~22MB vs ~61MB); nested DLLs are missing, causing endpoints to silently return 404 while `/healthz` still passes | Publish from the project directory: `dotnet publish -c Release -o ./publish` from `src/server/api/Sprk.Bff.Api/`; use `Deploy-BffApi.ps1` | [bff-deploy skill](.claude/skills/bff-deploy/SKILL.md) |
| 17 | **Assuming deployment worked because `az webapp deploy` returned success** — skipping health check and endpoint verification | Azure CLI may report success before the deployment registers; the app may still serve old code or have missing routes | Always verify with health check and test specific endpoints (expect 401, not 404, for auth-protected routes) | [bff-deploy skill](.claude/skills/bff-deploy/SKILL.md) |

### Deployment (PCF)

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 18 | **Using `Compress-Archive` to create PCF solution ZIPs** — `Compress-Archive -Path './*'` | Creates backslash path separators on Windows; Dataverse solution import rejects or silently fails with backslash entries | Use `pack.ps1` which creates ZIP entries with forward slashes via `System.IO.Compression` | [pcf-deploy skill](.claude/skills/pcf-deploy/SKILL.md) |
| 19 | **Updating only `solution.xml` version without `ControlManifest.Input.xml`** — incrementing the solution version but leaving the control manifest version unchanged | Dataverse uses the control manifest version as the cache key; same control version means Dataverse silently keeps the old bundle | Update `ControlManifest.Input.xml` version FIRST, then rebuild, then update the remaining 4 version locations | [dataverse-deploy skill](.claude/skills/dataverse-deploy/SKILL.md) |

### Deployment (Code Pages & Shared Libraries)

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| 20 | **Building without clearing Vite/Webpack cache after shared lib changes** — running `npm run build` without `rm -rf dist/ node_modules/.vite/` | Vite and Webpack cache resolved dependencies; stale cache bundles OLD shared lib code even when source files are correct; caused multiple production incidents | Clear cache before every build: `rm -rf dist/ node_modules/.vite/ .vite/` for Vite, `rm -rf out/` for Webpack; recompile shared lib `dist/` first if modified | [code-page-deploy skill](.claude/skills/code-page-deploy/SKILL.md) |

### Caching

| # | Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|---|-------------|---------------|-----------------|-----------|
| Bonus | **Adding L1 `IMemoryCache` without profiling proof** — introducing hybrid L1+L2 caching for performance | Hybrid caching adds coherence complexity with no demonstrated benefit; stale L1 data causes authorization bypass risks | Use Redis (`IDistributedCache`) for cross-request caching; `RequestCache` for within-request de-dupe; require profiling evidence before any `IMemoryCache` use | [ADR-009](.claude/adr/ADR-009-redis-caching.md) |

---

## Related

- [ADR-001](.claude/adr/ADR-001-minimal-api.md) -- Minimal API + BackgroundService (no Azure Functions)
- [ADR-002](.claude/adr/ADR-002-thin-plugins.md) -- Thin Dataverse plugins (no HTTP/Graph calls)
- [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) -- Code Pages and PCF over legacy JS
- [ADR-007](.claude/adr/ADR-007-spefilestore.md) -- SpeFileStore facade (no Graph SDK leaks)
- [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) -- Endpoint filters for authorization (no global middleware)
- [ADR-009](.claude/adr/ADR-009-redis-caching.md) -- Redis-first caching (no hybrid L1 without proof)
- [ADR-010](.claude/adr/ADR-010-di-minimalism.md) -- DI minimalism (concretes over interfaces)
- [ADR-013](.claude/adr/ADR-013-ai-architecture.md) -- AI architecture (extend BFF, no separate service)
- [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) -- Fluent UI v9 design system
- [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) -- PCF platform libraries (React 16 only)
