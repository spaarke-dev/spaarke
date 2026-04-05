# Coding Standards

> **Last Updated**: April 5, 2026
> **Applies To**: All source code — C# backend, TypeScript frontend (PCF and Code Pages), Dataverse plugins

---

## Rules

### General

1. **MUST** return `ProblemDetails` for all API error responses *(ADR-001, CLAUDE.md)*
2. **MUST** include correlation IDs for tracing in error responses *(CLAUDE.md)*
3. **MUST NOT** commit secrets to the repository; use `config/*.local.json` locally and Azure Key Vault in production *(CLAUDE.md)*

### C# — Architecture

4. **MUST** use Minimal API for all HTTP endpoints — no MVC controllers *(ADR-001)*
5. **MUST** use `BackgroundService` + Service Bus for async work — no Azure Functions *(ADR-001)*
6. **MUST** use endpoint filters for resource authorization — no global auth middleware *(ADR-008)*
7. **MUST** register concrete types unless a genuine seam exists *(ADR-010)*
8. **MUST** keep DI registrations at 15 or fewer non-framework lines; use feature module extensions *(ADR-010)*
9. **MUST** route all SPE/Graph operations through `SpeFileStore` facade *(ADR-007)*
10. **MUST NOT** inject `GraphServiceClient` outside `SpeFileStore` *(ADR-007)*
11. **MUST NOT** expose Graph SDK types in endpoint DTOs — use SDAP DTOs only *(ADR-007)*
12. **MUST NOT** create interfaces for single-implementation services *(ADR-010)*
13. **MUST** use `IDistributedCache` (Redis) for cross-request caching *(ADR-009)*
14. **MUST NOT** add L1 in-memory cache without profiling proof *(ADR-009)*

### C# — Naming

15. **MUST** use PascalCase for types, methods, properties, and constants *(spaarke-conventions)*
16. **MUST** prefix private fields with underscore: `_camelCase` *(spaarke-conventions)*
17. **MUST** use camelCase for parameters and local variables *(spaarke-conventions)*
18. **MUST** name files matching the primary type: `AuthorizationService.cs` *(spaarke-conventions)*
19. **MUST** name test files `{ClassName}.Tests.cs` *(CLAUDE.md)*
20. **MUST** suffix async methods with `Async` *(spaarke-conventions)*

### C# — Error Handling & Async

21. **MUST NOT** return raw exception messages to callers — use `ProblemDetails` *(ADR-001, CLAUDE.md)*
22. **MUST NOT** block on async code (`.Result`, `.Wait()`) — use `await` *(spaarke-conventions)*
23. **MUST** use `ConfigureAwait(false)` in library code *(spaarke-conventions)*
24. **SHOULD** return `Task` directly when no `await` is needed (elide async) *(spaarke-conventions)*

### C# — Dataverse Plugins

25. **MUST** keep plugins under 200 lines of code and under 50 ms p95 *(ADR-002)*
26. **MUST** limit plugin logic to validation, invariant enforcement, or audit stamping *(ADR-002)*
27. **MUST NOT** make HTTP, Graph, AI, or any remote I/O calls from plugins *(ADR-002)*
28. **MUST NOT** implement business logic or workflow orchestration in plugins *(ADR-002)*

### TypeScript — UI Framework

29. **MUST** use `@fluentui/react-components` (Fluent v9) exclusively — no Fluent v8 *(ADR-021)*
30. **MUST** wrap all UI in `FluentProvider` with a theme *(ADR-021)*
31. **MUST** use Fluent design tokens for colors, spacing, and typography — no hard-coded colors *(ADR-021)*
32. **MUST** use `makeStyles` (Griffel) for custom styling *(ADR-021)*
33. **MUST** support light, dark, and high-contrast modes *(ADR-021)*
34. **MUST** import icons from `@fluentui/react-icons` — no custom icon fonts *(ADR-021)*
35. **MUST NOT** import from granular `@fluentui/react-*` packages *(ADR-021)*
36. **MUST NOT** use alternative UI libraries (MUI, Ant Design, etc.) *(ADR-021)*
37. **MUST** import reusable components from `@spaarke/ui-components` *(ADR-012, CLAUDE.md)*

### TypeScript — PCF Controls (Form-Bound, React 16/17)

38. **MUST** use React 16 APIs in PCF controls (`ReactDOM.render` or `ReactControl` pattern) *(ADR-022)*
39. **MUST** declare `platform-library` entries in `ControlManifest.Input.xml` *(ADR-022)*
40. **MUST** list React as `devDependencies` in PCF `package.json` — not `dependencies` *(ADR-022)*
41. **MUST NOT** use React 18+ APIs in PCF (`createRoot`, `hydrateRoot`, concurrent features) *(ADR-022)*
42. **MUST NOT** import from `react-dom/client` in PCF controls *(ADR-022)*
43. **MUST NOT** bundle React/ReactDOM into PCF output — keep bundle under 5 MB *(ADR-022)*

### TypeScript — Code Pages (Standalone Dialogs, React 19)

44. **MUST** use Code Page for all new standalone dialogs, wizards, and full-page UI *(ADR-006)*
45. **MUST** use React 19 `createRoot()` entry point *(ADR-006, ADR-021)*
46. **MUST** bundle React 19 and Fluent v9 in Code Page output *(ADR-021)*
47. **MUST** read parameters from `URLSearchParams`, not PCF context *(ADR-021)*
48. **MUST NOT** use a PCF + custom page wrapper when a Code Page achieves the same result *(ADR-006)*

### TypeScript — Naming

49. **MUST** use PascalCase for component files and exports: `DataGrid.tsx` *(spaarke-conventions)*
50. **MUST** use camelCase for utility files: `formatters.ts` *(spaarke-conventions)*
51. **MUST** prefix interfaces with `I`: `IDataGridProps` *(spaarke-conventions)*
52. **MUST** use PascalCase for types (no `I` prefix): `ButtonVariant` *(spaarke-conventions)*
53. **MUST** use `SCREAMING_SNAKE_CASE` for module-level constants *(spaarke-conventions)*
54. **MUST NOT** use `any` type without documented justification *(spaarke-conventions)*

### TypeScript — Anti-Legacy

55. **MUST NOT** create legacy JavaScript web resources (no-framework JS, jQuery) *(ADR-006)*
56. **MUST NOT** add business logic to ribbon/command bar scripts — invocation only *(ADR-006)*

---

## Examples

### Rule 6: Endpoint filters for authorization

```csharp
// Correct
var docs = app.MapGroup("/api/documents").RequireAuthorization();
docs.MapGet("/{id}", GetDocument)
    .AddEndpointFilter<DocumentAuthorizationFilter>();

// Wrong — global middleware cannot access route values
app.UseMiddleware<DocumentSecurityMiddleware>();
```
*Source: ADR-008*

### Rule 7: Concrete DI registration

```csharp
// Correct
services.AddSingleton<SpeFileStore>();

// Wrong — interface without genuine seam
services.AddSingleton<ISpeFileStore, SpeFileStore>();
```
*Source: ADR-010*

### Rule 9: SpeFileStore facade

```csharp
// Correct — use facade, returns SDAP DTOs
public class DocumentEndpoints(SpeFileStore store)
{
    public Task<FileHandleDto> Get(string id) => store.GetFileAsync(id);
}

// Wrong — Graph SDK leaks above facade
public class DocumentEndpoints(GraphServiceClient graph) { }
```
*Source: ADR-007*

### Rule 22: No sync-over-async

```csharp
// Correct
var doc = await _store.GetFileAsync(id);

// Wrong — can deadlock
var doc = _store.GetFileAsync(id).Result;
```
*Source: spaarke-conventions*

### Rule 29: Fluent v9 only

```typescript
// Correct
import { Button, tokens } from "@fluentui/react-components";

// Wrong — Fluent v8
import { PrimaryButton } from "@fluentui/react";
```
*Source: ADR-021*

### Rule 31: Design tokens, not hard-coded colors

```typescript
// Correct
const useStyles = makeStyles({
    root: { backgroundColor: tokens.colorNeutralBackground1 },
});

// Wrong
const useStyles = makeStyles({
    root: { backgroundColor: "#ffffff" },
});
```
*Source: ADR-021*

### Rule 38: PCF uses React 16 API

```typescript
// Correct — ReactControl pattern (React 16)
public updateView(context): React.ReactElement {
    return React.createElement(FluentProvider, { theme },
        React.createElement(MyComponent, { context }));
}

// Wrong — React 18 API in PCF
import { createRoot } from "react-dom/client";
```
*Source: ADR-022*

### Rule 45: Code Page uses React 19

```typescript
// Correct — Code Page entry point
import { createRoot } from "react-dom/client";
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={theme}><App /></FluentProvider>
);
```
*Source: ADR-006, ADR-021*

---

## Anti-Pattern Reference

| Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|-------------|---------------|-----------------|-----------|
| Azure Functions for async work | Duplicate cross-cutting concerns, split runtime | `BackgroundService` + Service Bus | ADR-001 |
| Global auth middleware | Runs before routing; no access to route values | Endpoint filters | ADR-008 |
| Interface for single implementation | Adds indirection without value; DI sprawl | Register concrete type | ADR-010 |
| `GraphServiceClient` injected into endpoints | Leaks Graph SDK types above facade boundary | Use `SpeFileStore` facade | ADR-007 |
| HTTP/Graph calls in Dataverse plugins | Plugins must complete in <50 ms; no external I/O | Defer to BFF API or workers | ADR-002 |
| Fluent UI v8 imports | Deprecated; breaks theming consistency | Use `@fluentui/react-components` (v9) | ADR-021 |
| Hard-coded colors (`#fff`, `rgb()`) | Breaks dark mode and high-contrast | Use `tokens.color*` design tokens | ADR-021 |
| `createRoot` in PCF control | Platform provides React 16/17; React 18 API crashes | Use `ReactDOM.render` or `ReactControl` | ADR-022 |
| React bundled in PCF output (>5 MB) | Platform already provides React; duplicates cause conflicts | Declare `platform-library` in manifest | ADR-022 |
| Legacy JS web resource (jQuery, no-framework) | Unmaintainable; no type safety; no component reuse | PCF for form-bound; Code Page for standalone | ADR-006 |
| Business logic in ribbon scripts | Untestable; no DI or error handling | Ribbon script calls `navigateTo` only; logic in Code Page | ADR-006 |
| `.Result` / `.Wait()` on async | Deadlocks in ASP.NET request pipeline | Use `async`/`await` | spaarke-conventions |
| `IMemoryCache` without profiling proof | Hybrid L1+L2 adds coherence issues without demonstrated need | Use `IDistributedCache` (Redis) | ADR-009 |

---

## Related

- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API + BackgroundService; no Azure Functions
- [ADR-002](../../.claude/adr/ADR-002-thin-plugins.md) — Thin Dataverse plugins; no remote I/O
- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) — Code Pages as default UI surface; no legacy JS
- [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) — SpeFileStore facade; no Graph SDK leakage
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint filters for authorization
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism; concrete registrations
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 design system
- [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — PCF platform libraries (React 16/17)
- [spaarke-conventions skill](../../.claude/skills/spaarke-conventions/SKILL.md) — Naming and code patterns
