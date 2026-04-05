# Code Review by Module

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Applies To**: All developers and the `/code-review` skill when reviewing module-specific code

---

## When to Follow This Procedure

- Reviewing code changes before creating a pull request
- Running the `/code-review` skill on module-specific files
- Performing quality gate checks in `task-execute` Step 9.5
- Reviewing pull requests that touch multiple modules

## How to Use

Identify which module(s) the changed files belong to, then apply the corresponding checklist below **in addition to** the general checklist from `.claude/skills/code-review/SKILL.md`. The general skill covers security, performance, style, quantitative metrics, quality direction, and AI code smell detection. This document adds **module-specific constraints** derived from ADRs and operational experience.

---

## Module: BFF API (`src/server/api/Sprk.Bff.Api/`)

### Checklist

- [ ] **DI Registration (ADR-010)**: New services registered as concretes (`services.AddSingleton<MyService>()`) not interfaces, unless the service is an allowed seam (`IAccessDataSource`, `IAuthorizationRule`)
- [ ] **Endpoint Filters (ADR-008)**: Resource authorization uses endpoint filters (`.AddEndpointFilter<DocumentAuthorizationFilter>()`), NOT global middleware
- [ ] **Error Responses (ADR-019)**: All error paths return `ProblemDetails` with stable error codes, not raw exception messages
- [ ] **SpeFileStore Facade (ADR-007)**: No `GraphServiceClient` injected directly into endpoints or services outside of `SpeFileStore` and `GraphClientFactory`
- [ ] **Structured Logging**: Log calls use structured properties (`{DocumentId}`) not string interpolation (`$"{documentId}"`)
- [ ] **Endpoint Organization**: New endpoints follow the group pattern (`MapGroup("/api/...").MapGet(...)`)
- [ ] **Health Check**: If adding new dependencies, verify `/healthz` still works (ADR-001)
- [ ] **Correlation IDs**: Error responses include correlation ID for tracing
- [ ] **Constructor Parameter Count**: Flag if any service has > 5 constructor parameters (ADR-010 DI minimalism)
- [ ] **No Blocking Calls**: No `.Result`, `.Wait()`, or `Task.Run()` wrapping async calls

### Common Mistakes

| Mistake | Correct Pattern |
|---------|----------------|
| `services.AddScoped<IMyService, MyService>()` | `services.AddSingleton<MyService>()` |
| `app.UseMiddleware<AuthMiddleware>()` | `.AddEndpointFilter<AuthFilter>()` |
| `return Results.BadRequest("error message")` | `return Results.Problem(detail: "...", statusCode: 400)` |
| `_logger.LogError($"Failed for {id}")` | `_logger.LogError("Failed for {Id}", id)` |

---

## Module: AI Pipeline (`src/server/api/Sprk.Bff.Api/Services/Ai/`)

### Checklist

- [ ] **HttpContext Access**: AI services that need `HttpContext` receive it via parameter injection from the endpoint, NOT by injecting `IHttpContextAccessor` (creates scoping issues with background tasks)
- [ ] **Scope Resolution**: `ScopeResolverService` is used to resolve knowledge source IDs from playbook definitions; do not hard-code scope IDs
- [ ] **Streaming (SSE)**: Chat endpoints use Server-Sent Events via `IAsyncEnumerable`; verify the response is not buffered
- [ ] **Tool Framework (ADR-013)**: New AI tools implement `IAiToolHandler`; do not create separate services outside the tool framework
- [ ] **Entity Scoping**: RAG search uses `ParentEntityType` and `ParentEntityId` from `ChatHostContext` for entity-scoped queries; verify null-safety for backward compatibility (tenant-wide when null)
- [ ] **Knowledge Scope Isolation**: `ChatKnowledgeScope` carries explicit knowledge source IDs; search tools must pass these through, not ignore them
- [ ] **Background Job Handoff**: Long-running AI operations (analysis, indexing) must be enqueued via Service Bus, not run inline in the request
- [ ] **Token Propagation**: User tokens must flow through the AI pipeline for OBO Graph calls; verify `userAccessToken` is not lost between service layers

### Common Mistakes

| Mistake | Correct Pattern |
|---------|----------------|
| `IHttpContextAccessor` in AI service | Pass `HttpContext` or token from endpoint |
| Hard-coded knowledge source IDs | Use `ScopeResolverService` |
| Inline AI processing in endpoint | Enqueue via `ServiceBusJobProcessor` |
| `RagService` called without entity scope | Pass `ParentEntityType`/`ParentEntityId` from host context |

---

## Module: PCF Controls (`src/client/pcf/`)

### Checklist

- [ ] **React Version (ADR-022)**: Uses React 16 APIs only; no `createRoot()`, no React 18 hooks (`useId`, `useSyncExternalStore`, `useTransition`)
- [ ] **Fluent UI v9 (ADR-021)**: All UI uses `@fluentui/react-components` (v9); no imports from `@fluentui/react` (v8)
- [ ] **Theme Tokens**: Colors use semantic tokens (`tokens.colorNeutralBackground1`), not hard-coded hex values; dark mode must work
- [ ] **FluentProvider Wrapper**: Root component wrapped in `<FluentProvider theme={webLightTheme}>` (or auto-detected theme)
- [ ] **Version Footer**: Control displays version in UI footer (`v{X.Y.Z} - Built {date}`)
- [ ] **Version Bump (4 locations)**: If releasing, version updated in: ControlManifest.Input.xml, UI footer, solution.xml, solution ControlManifest.xml
- [ ] **Shared Component Library (ADR-012)**: Reusable components imported from `@spaarke/ui-components`, not duplicated locally
- [ ] **No `any` Types**: TypeScript strict mode; no `any` without explicit justification comment
- [ ] **Destroy Cleanup**: `destroy()` method calls `ReactDOM.unmountComponentAtNode(container)` and removes event listeners
- [ ] **MSAL Singleton**: `MsalAuthProvider` uses singleton pattern; no multiple MSAL instances
- [ ] **Icon Accessibility**: Icon-only buttons have `aria-label` attributes
- [ ] **PCF Entry Point**: Uses `ReactDOM.render()` (React 16), not `createRoot()` (React 18)

### Common Mistakes

| Mistake | Correct Pattern |
|---------|----------------|
| `import { createRoot } from 'react-dom/client'` | `ReactDOM.render(element, container)` |
| `import { DefaultButton } from '@fluentui/react'` | `import { Button } from '@fluentui/react-components'` |
| `style={{ color: '#0078D4' }}` | `style={{ color: tokens.colorBrandForeground1 }}` |
| Missing `destroy()` cleanup | `ReactDOM.unmountComponentAtNode(this.container)` |
| `@xyflow/react` (React 18 required) | `react-flow-renderer` v10 (React 16 compatible) |

---

## Module: Code Pages (`src/client/code-pages/`)

### Checklist

- [ ] **React 18+ (ADR-022)**: Uses `createRoot()` from `react-dom/client`; React 18/19 APIs are allowed
- [ ] **Auth Bootstrap**: Follows bootstrap sequence: `resolveRuntimeConfig()` -> `setRuntimeConfig()` -> `ensureAuthInitialized()` -> render
- [ ] **No Module-Level Config Calls**: Runtime config getters must be called inside lazy functions, not at module scope (throws before bootstrap completes)
- [ ] **Fluent UI v9 (ADR-021)**: Same as PCF -- semantic tokens, dark mode, FluentProvider wrapper
- [ ] **Webpack Bundle Size**: Check bundle output; flag if significantly larger than existing code pages
- [ ] **URL Parameters**: Uses `URLSearchParams` for parameter extraction from `window.location.search`
- [ ] **Shared Components (ADR-012)**: Uses `WizardDialog`, `SidePanel`, or other components from `@spaarke/ui-components` where applicable
- [ ] **No PCF Dependencies**: Code pages must NOT import `ComponentFramework` types or PCF-specific APIs

### Common Mistakes

| Mistake | Correct Pattern |
|---------|----------------|
| `const CLIENT_ID = getMsalClientId()` at module scope | `function getMsalConfig() { return { clientId: getMsalClientId() } }` |
| `ReactDOM.render()` (React 16) | `createRoot(document.getElementById("root")!).render(...)` |
| Importing `@types/powerapps-component-framework` | Only for PCF controls, not code pages |

---

## Module: Dataverse Plugins (`src/dataverse/plugins/`)

### Checklist

- [ ] **Execution Time (ADR-002)**: Plugin code path must complete in <50ms p95; no complex logic, no loops over collections
- [ ] **No HTTP Calls (ADR-002)**: Absolutely no `HttpClient`, `WebClient`, `HttpWebRequest`, or any network calls
- [ ] **No Graph API Calls (ADR-002)**: No Microsoft Graph SDK usage; all Graph operations belong in BFF API
- [ ] **Code Size**: Plugin class should be <200 lines of code
- [ ] **Assembly Size**: Built DLL must be <1MB (validated in CI pipeline)
- [ ] **Thin Validation Only**: Plugins should only perform field validation, simple calculations, or data projection
- [ ] **No Business Logic**: Complex business logic belongs in the BFF API, not plugins
- [ ] **.NET Framework 4.8**: Plugin projects target .NET Framework 4.8 (Dataverse requirement)
- [ ] **No External Dependencies**: Plugins cannot add NuGet packages beyond CrmSdk; assemblies must be self-contained
- [ ] **IPlugin Interface**: Implements `IPlugin.Execute(IServiceProvider)` directly

### Common Mistakes

| Mistake | Correct Pattern |
|---------|----------------|
| `new HttpClient().GetAsync(...)` | Move to BFF API endpoint |
| Complex orchestration in plugin | Enqueue to Service Bus, BFF handles orchestration |
| `using Microsoft.Graph;` | Only in BFF API, behind SpeFileStore facade |
| Plugin > 200 LoC | Extract into BFF API service |

---

## Module: Shared Libraries (`src/server/shared/`, `src/client/shared/`)

### Checklist

- [ ] **No Circular Dependencies**: `Spaarke.Core` has no dependencies on other Spaarke libraries; `Spaarke.Dataverse` can depend on `Spaarke.Core`; both are consumed by `Sprk.Bff.Api`
- [ ] **Context-Agnostic Components (ADR-012)**: Shared UI components must not reference PCF `ComponentFramework` types or Dataverse-specific APIs
- [ ] **React Compatibility**: `@spaarke/ui-components` peer dependencies require React >=16.14.0 to work in both PCF (React 16) and code pages (React 18)
- [ ] **Fluent UI v9 Only**: Shared components use only Fluent UI v9; no v8 imports
- [ ] **Barrel Exports**: New components added to both `components/index.ts` and root `src/index.ts`
- [ ] **JSDoc Documentation**: Public component props documented with JSDoc
- [ ] **Unit Tests**: New shared components have test coverage

---

## Cross-Module Review (When Changes Span Modules)

When a changeset touches multiple modules, also check:

- [ ] **Consistent Error Handling**: BFF endpoint returns `ProblemDetails` -> PCF/code page handles error gracefully with user-friendly message
- [ ] **Auth Token Flow**: Token acquired in frontend -> passed to BFF -> used for OBO -> reaches downstream services
- [ ] **Shared Type Alignment**: If frontend and backend share a contract (e.g., request/response shape), verify they match
- [ ] **Deployment Order**: If changes require coordinated deployment (e.g., new BFF endpoint consumed by updated PCF), document the required order

## Automation

| Step | Automated By | Manual? |
|------|-------------|---------|
| ADR compliance (structural) | `Spaarke.ArchTests` NetArchTest suite in CI | No |
| Fluent UI version mixing | ESLint rules | Partially |
| Plugin size validation | `sdap-ci.yml` code-quality job | No |
| Formatting (C#) | `dotnet format --verify-no-changes` in CI | No |
| Formatting (TS) | Prettier check in CI | No |
| ESLint (PCF) | `eslint . --max-warnings 0` in CI | No |
| Module-specific checklist | `/code-review` skill loads this document | No (skill-driven) |

## Related

- [code-review skill](../../.claude/skills/code-review/SKILL.md) -- General review procedure with quantitative metrics and AI smell detection
- [adr-check skill](../../.claude/skills/adr-check/SKILL.md) -- Deep ADR compliance validation
- [PCF CLAUDE.md](../../src/client/pcf/CLAUDE.md) -- PCF-specific development guidelines
- [BFF API CLAUDE.md](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) -- BFF API constraints and patterns
