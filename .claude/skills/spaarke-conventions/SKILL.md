---
description: Apply Spaarke coding standards from CLAUDE.md - naming, structure, file organization, and technology patterns
tags: [conventions, standards, naming, code-style, patterns]
techStack: [csharp, typescript, aspnet-core, react]
appliesTo: ["**/*.cs", "**/*.ts", "**/*.tsx"]
alwaysApply: true
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# spaarke-conventions

> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: Conventions evolve with the tech stack (Fluent UI v9 supplanted v8; React 18 in Code Pages but 16 in PCF; Redis-first caching per ADR-009). A single frozen exemplar would lag the rules. The conventions themselves are the canonical reference.
> **Drift-prevention note**: This skill codifies rules from root `CLAUDE.md` Coding Standards section. When `CLAUDE.md` changes, this skill must update in lockstep. `Find-SkillReferenceDrift.ps1` (Phase 4a) will catch missed propagation.

## Purpose

Enforces Spaarke-specific coding conventions when generating or reviewing code. This skill is **always applied** to ensure consistency across the codebase. It codifies the standards from root `CLAUDE.md` into actionable rules.

## Always-Apply Rules

These conventions apply automatically to ALL code generation and review:

### File Naming

| File Type | Convention | Example |
|-----------|------------|---------|
| C# class files | PascalCase | `AuthorizationService.cs` |
| C# interface files | `I` + PascalCase | `IAuthorizationService.cs` |
| TypeScript components | PascalCase | `DataGrid.tsx` |
| TypeScript utilities | camelCase | `formatters.ts` |
| Test files | `{ClassName}.Tests.cs` | `AuthorizationService.Tests.cs` |
| ADRs | `ADR-{NNN}-{slug}.md` | `ADR-001-minimal-api-and-workers.md` |
| Config files | kebab-case + `.local.json` | `azure-config.local.json` |

### C# Naming Conventions

```csharp
// Types: PascalCase
public class DocumentService { }
public interface IDocumentService { }
public record DocumentDto { }
public enum DocumentStatus { }

// Methods: PascalCase
public async Task<Document> GetDocumentAsync(string id) { }

// Properties: PascalCase
public string DocumentId { get; set; }

// Private fields: _camelCase
private readonly GraphServiceClient _graphClient;
private int _retryCount;

// Parameters & locals: camelCase
public void Process(string documentId)
{
    var localResult = DoWork();
}

// Constants: PascalCase
public const int MaxRetryAttempts = 3;
public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
```

### TypeScript/React Naming Conventions

```typescript
// Components: PascalCase (file and export)
// File: DataGrid.tsx
export const DataGrid: React.FC<IDataGridProps> = (props) => { }

// Interfaces: IPascalCase
interface IDataGridProps {
  items: IDataItem[];
  onSelect: (item: IDataItem) => void;
}

// Types: PascalCase (no I prefix)
type ButtonVariant = 'primary' | 'secondary';

// Functions: camelCase
function formatDate(date: Date): string { }
const calculateTotal = (items: number[]) => items.reduce((a, b) => a + b, 0);

// Variables: camelCase
const selectedItems: IDataItem[] = [];
let isLoading = false;

// Constants: SCREAMING_SNAKE_CASE (module-level) or camelCase (local)
const API_BASE_URL = '/api/v1';
const maxRetries = 3;
```

### Directory Structure

```
src/
├── client/                    # Frontend code
│   ├── pcf/                   # PCF Controls
│   │   └── {ControlName}/     # One folder per control
│   │       ├── {ControlName}.tsx
│   │       ├── {ControlName}.css
│   │       └── index.ts
│   └── shared/                # Shared UI components
│       └── components/
├── server/                    # Backend code
│   ├── api/                   # Minimal API project
│   │   ├── Api/               # Endpoint groups
│   │   │   └── {Domain}Endpoints.cs
│   │   ├── Services/          # Business logic
│   │   ├── Infrastructure/    # External integrations (Graph, etc.)
│   │   └── Models/            # DTOs, domain models
│   └── shared/                # Shared .NET libraries
└── solutions/                 # Dataverse solution projects

tests/
├── unit/                      # Unit tests mirror src/ structure
├── integration/               # Integration tests
└── e2e/                       # End-to-end tests
```

## Technology-Specific Patterns

### .NET 8 Minimal API

```csharp
// ✅ DO: Use endpoint groups
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Documents");
        
        group.MapGet("/", GetAllDocuments);
        group.MapGet("/{id}", GetDocument);
        group.MapPost("/", CreateDocument);
    }
    
    private static async Task<Results<Ok<DocumentDto>, NotFound>> GetDocument(
        string id,
        DocumentService service)
    {
        var doc = await service.GetByIdAsync(id);
        return doc is null ? TypedResults.NotFound() : TypedResults.Ok(doc);
    }
}

// ✅ DO: Use endpoint filters for auth (ADR-008)
group.MapGet("/{id}", GetDocument)
    .AddEndpointFilter<DocumentAuthorizationFilter>();

// ❌ DON'T: Use global middleware for resource auth
app.UseMiddleware<AuthorizationMiddleware>();

// ✅ DO: Use concrete types unless seam needed (ADR-010)
services.AddSingleton<SpeFileStore>();

// ❌ DON'T: Create interfaces for single implementations
services.AddSingleton<ISpeFileStore, SpeFileStore>(); // unnecessary
```

### TypeScript/PCF Controls

```typescript
// ✅ DO: Import from Fluent UI v9
import { Button, Input, DataGrid } from "@fluentui/react-components";

// ❌ DON'T: Import from Fluent UI v8
import { PrimaryButton } from "@fluentui/react"; // WRONG

// ✅ DO: Import from shared library (ADR-012)
import { DataGrid, StatusBadge } from "@spaarke/ui-components";

// ✅ DO: Use proper TypeScript types
interface IProps {
  items: IDataItem[];
  onSelect: (item: IDataItem) => void;
}

// ❌ DON'T: Use any type
const handleClick = (e: any) => { }; // WRONG

// ✅ DO: Use React.FC with props interface
export const MyComponent: React.FC<IProps> = ({ items, onSelect }) => {
  return <div>...</div>;
};
```

### Auth v2 client contract (ADR-028)

```typescript
// ✅ DO: Bootstrap once, consume via useAuth() or authenticatedFetch
import { initAuth, useAuth, authenticatedFetch } from '@spaarke/auth';

// Top-level (main.tsx / index.tsx) — call ONCE before render
await initAuth({ clientId, tenantId, bffBaseUrl, bffApiScope });

// In React components — use the hook
const { getAccessToken } = useAuth();

// For BFF calls — use authenticatedFetch (handles bearer + 401 retry automatically)
const response = await authenticatedFetch('/api/...', { method: 'POST', body });

// ❌ DON'T: Raw fetch with manual Authorization header
const r = await fetch(url, { headers: { Authorization: `Bearer ${token}` } }); // WRONG

// ❌ DON'T: Pass accessToken as a typed prop or constructor arg
<MyComponent accessToken={token} />  // WRONG (use authenticatedFetch)
new ApiClient(baseUrl, getAccessToken)  // WRONG (factory should accept authenticatedFetch)

// ❌ DON'T: Instantiate PublicClientApplication directly outside @spaarke/auth
const msal = new PublicClientApplication(config);  // WRONG (violates INV-7)

// ❌ DON'T: Reference retired token-transport symbols
window.__SPAARKE_BFF_TOKEN__  // WRONG — deleted in Phase A
import { tokenBridge } from '...';  // WRONG — deleted in Phase A
new BridgeStrategy() / new XrmStrategy() / new MsalSilentStrategy()  // WRONG — deleted in Phase A
```

**Canonical reference**: [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../adr/ADR-028-spaarke-auth-architecture.md), [`.claude/patterns/auth/spaarke-sso-binding.md`](../../patterns/auth/spaarke-sso-binding.md), [`.claude/constraints/auth.md`](../../constraints/auth.md).

### Dataverse Plugins

```csharp
// ✅ DO: Keep plugins thin (<200 LoC, <50ms)
public class ValidateDocumentPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var target = context.InputParameters["Target"] as Entity;
        
        ValidateRequiredFields(target);
        // That's it - no HTTP calls, no external services
    }
    
    private void ValidateRequiredFields(Entity entity)
    {
        if (!entity.Contains("sp_name"))
            throw new InvalidPluginExecutionException("Name is required");
    }
}

// ❌ DON'T: Make HTTP/Graph calls from plugins (ADR-002)
public class BadPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        using var client = new HttpClient(); // WRONG
        var result = client.GetAsync("...").Result; // WRONG
    }
}
```

## Error Handling Patterns

### API Responses

```csharp
// ✅ DO: Use ProblemDetails for errors
app.MapGet("/api/documents/{id}", async (string id, DocumentService service) =>
{
    try
    {
        var doc = await service.GetByIdAsync(id);
        return doc is null 
            ? Results.Problem(
                statusCode: 404,
                title: "Document not found",
                detail: $"No document exists with ID '{id}'")
            : Results.Ok(doc);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Problem(
            statusCode: 403,
            title: "Access denied",
            detail: "You do not have permission to access this document");
    }
});

// ❌ DON'T: Return raw exceptions
catch (Exception ex)
{
    return Results.BadRequest(ex.Message); // Leaks internal details
}
```

### PCF Controls

```typescript
// ✅ DO: Show user-friendly errors
try {
  const data = await fetchData();
  setItems(data);
} catch (error) {
  setError("Unable to load documents. Please try again.");
  console.error("Fetch failed:", error); // Log for debugging
}

// ❌ DON'T: Show technical errors to users
catch (error) {
  setError(error.stack); // WRONG - users don't need stack traces
}
```

## Async Patterns

```csharp
// ✅ DO: Use async/await properly
public async Task<Document> GetDocumentAsync(string id)
{
    return await _graphClient.GetAsync(id);
}

// ❌ DON'T: Block on async (sync-over-async)
public Document GetDocument(string id)
{
    return _graphClient.GetAsync(id).Result; // WRONG - can deadlock
}

// ✅ DO: Use ConfigureAwait(false) in library code
public async Task<Document> GetDocumentAsync(string id)
{
    return await _graphClient.GetAsync(id).ConfigureAwait(false);
}

// ✅ DO: Return Task directly when no await needed
public Task<Document> GetDocumentAsync(string id)
{
    return _graphClient.GetAsync(id); // No await, just pass through
}
```

## Documentation Standards

### C# XML Documentation

```csharp
/// <summary>
/// Retrieves a document by its unique identifier.
/// </summary>
/// <param name="id">The document's unique identifier.</param>
/// <returns>The document if found; otherwise, null.</returns>
/// <exception cref="UnauthorizedAccessException">
/// Thrown when the user doesn't have access to the document.
/// </exception>
public async Task<Document?> GetDocumentAsync(string id)
```

### TypeScript JSDoc

```typescript
/**
 * Formats a date for display in the UI.
 * @param date - The date to format
 * @param locale - Optional locale string (default: 'en-US')
 * @returns Formatted date string
 */
function formatDate(date: Date, locale = 'en-US'): string {
  return date.toLocaleDateString(locale);
}
```

## Quick Validation Rules

When generating or reviewing code, automatically check:

| Rule | Check | Severity |
|------|-------|----------|
| File naming | Matches conventions above | Warning |
| Import source | Fluent UI v9, not v8 | Warning |
| Interface necessity | Single implementation? | Info |
| Async pattern | No `.Result` or `.Wait()` | Warning |
| Error handling | Uses ProblemDetails | Warning |
| Plugin size | <200 LoC | Warning |
| Type safety | No `any` without justification | Warning |

## Resources

- Root `CLAUDE.md` - Full coding standards reference
- `docs/architecture/` - Architecture decisions and constraints
- `docs/adr/` - Decision rationale (if needed)

## Related Skills

- **code-review**: Uses these conventions for review checklist
- **adr-check**: Deeper architectural validation
- **project-init**: Ensures new projects follow structure

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| New code mixes Fluent UI v8 and v9 imports | Author copied snippet from older code or external example | v8 imports are an instant ADR-021 violation. Use `import { ... } from "@fluentui/react-components"` (v9) exclusively. `/adr-check` catches this. |
| PCF control attempts `createRoot()` from React 18 | Author treated PCF like a Code Page — they're different per ADR-022 | PCF (field-bound) uses React 16 APIs from `ComponentFramework.ReactControl`. Code Pages (standalone dialogs) use React 18 `createRoot`. NEVER mix. The error you'll see: `createRoot is not a function`. |
| BFF API uses global authorization middleware (`app.UseMiddleware<AuthorizationMiddleware>()`) | Author followed generic ASP.NET tutorial | Per ADR-008: use endpoint filters (`AddEndpointFilter<DocumentAuthorizationFilter>()`), never global middleware for resource authorization. |
| Code injects `GraphServiceClient` directly into controller | Author bypassed the SpeFileStore facade | Per ADR-007: never let Graph SDK types leak above SpeFileStore. Inject the facade, not the underlying client. |
| New webresource (`.js` or `.html`) created instead of PCF/Code Page | Author followed legacy Dataverse customization pattern | Per ADR-006: field-bound controls → PCF (`pcf-deploy`); standalone dialogs → React Code Pages (`code-page-deploy`). No new legacy JS webresources. |
| Project-scoped CLAUDE.md and root CLAUDE.md disagree on a convention | Both updated separately; drift introduced | Root CLAUDE.md is canonical for cross-cutting standards. Project CLAUDE.md may NARROW (add stricter rules) but not RELAX or CONTRADICT. |
| Raw `fetch(url, { headers: { Authorization: 'Bearer ...' } })` or `window.__SPAARKE_BFF_TOKEN__` / `tokenBridge` regression | Author copied snippet from pre-v2 code or external tutorial | Per ADR-028 (Spaarke Auth v2): use `authenticatedFetch` from `@spaarke/auth`. Tokens are managed by `useAuth()` hook. Never snapshot. `/adr-check` catches raw fetch patterns and retired symbols. |
| Per-PCF `MsalAuthProvider.ts` / direct `new PublicClientApplication(...)` | Author bypassed `@spaarke/auth` singleton | Per ADR-028 INV-7: all consumers share ONE PCA via `@spaarke/auth`. The only remaining pre-v2 PCF is `UniversalQuickCreate` (V3 cleanup target — do NOT pattern new PCFs on it). |
