# Xrm.WebApi vs BFF API Authentication

> **Purpose**: Decide when MSAL/@spaarke/auth bootstrap is needed vs when Xrm.WebApi suffices
> **Source ADRs**: ADR-001 (Minimal API), ADR-008 (endpoint filters)

---

## Decision Guide

| Need | API | Auth Mechanism | MSAL Required? | Bootstrap? |
|------|-----|---------------|----------------|------------|
| Read/write Dataverse records | `Xrm.WebApi` | Session cookie (automatic) | **NO** | **NO** |
| Call BFF API endpoints (`/api/*`) | `authenticatedFetch` | Bearer token (MSAL OBO) | **YES** | **YES** |
| SharePoint/Graph via OBO | BFF API | Bearer token (MSAL OBO) | **YES** | **YES** |
| AI operations (OpenAI, Doc Intel) | BFF API | Bearer token (MSAL OBO) | **YES** | **YES** |
| Direct Dataverse REST (edge cases) | `fetch` + session cookie | Session cookie | **NO** | **NO** |

---

## Pattern 1: Xrm.WebApi Only (No Auth Setup)

**Used by**: SmartTodo, TodoDetailSidePane, CalendarSidePane

These Code Pages only read/write Dataverse records. No BFF calls, no MSAL.

```typescript
// main.tsx — simple, no bootstrap
import { createRoot } from "react-dom/client";
import { App } from "./App";

createRoot(document.getElementById("root")!).render(<App />);
```

```typescript
// services/DataverseService.ts — Xrm.WebApi handles auth automatically
export class DataverseService {
  constructor(private webApi: IWebApi) {}

  async getTodos(userId: string) {
    return this.webApi.retrieveMultipleRecords("sprk_eventtodo", `?$filter=...`);
  }
}
```

**Why no MSAL?** `Xrm.WebApi` runs inside the Dataverse iframe and uses the platform's session cookie. Authentication is handled by the host — the Code Page never sees a token.

---

## Pattern 2: BFF API (Full Auth Bootstrap)

**Used by**: LegalWorkspace, AnalysisWorkspace, wizard Code Pages

These Code Pages call BFF endpoints that require Bearer tokens.

```typescript
// main.tsx — async bootstrap required
import { resolveRuntimeConfig } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";

async function bootstrap() {
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);
  await ensureAuthInitialized();
  createRoot(document.getElementById("root")!).render(<App />);
}
bootstrap();
```

```typescript
// hooks/useWorkspaceLayouts.ts — authenticatedFetch adds Bearer token
import { authenticatedFetch } from "../services/authInit";

const response = await authenticatedFetch(`${bffBaseUrl}/api/workspace/layouts`);
```

**Why MSAL?** BFF API endpoints use `.RequireAuthorization()` (ADR-008). The request must include a Bearer token acquired via MSAL silent auth / SSO.

---

## Pattern 3: Both (Xrm.WebApi + BFF)

**Used by**: LegalWorkspace

Some Code Pages use Xrm.WebApi for entity queries AND BFF for specialized operations.

```typescript
// Xrm.WebApi — no auth setup needed (section components)
const records = await webApi.retrieveMultipleRecords("sprk_eventtodo", query);

// BFF API — authenticatedFetch (workspace layout API)
const layouts = await authenticatedFetch(`${bffBaseUrl}/api/workspace/layouts`);
```

The auth bootstrap is for BFF only. Xrm.WebApi works regardless of whether MSAL is initialized.

---

## MUST Rules

- **MUST** add `@spaarke/auth` bootstrap if ANY BFF endpoint is called
- **MUST NOT** add `@spaarke/auth` bootstrap to Code Pages that only use `Xrm.WebApi` — unnecessary overhead, adds ~200ms to startup
- **MUST NOT** use raw `fetch()` for BFF calls — use `authenticatedFetch()` from `authInit.ts`
- **MUST** use `Xrm.WebApi` (not BFF) for simple Dataverse CRUD — it's faster (no token acquisition) and uses the platform session
- **MUST** use BFF API (not `Xrm.WebApi`) for SharePoint, Graph, AI, or cross-service operations that require OBO tokens

---

## Quick Reference: Existing Code Pages

| Code Page | Auth Pattern | Reason |
|-----------|-------------|--------|
| LegalWorkspace | BFF + Xrm.WebApi | Layouts API, AI, documents (BFF) + entity queries (Xrm) |
| AnalysisWorkspace | BFF + Xrm.WebApi | AI analysis (BFF) + entity queries (Xrm) |
| SmartTodo | Xrm.WebApi only | Todo CRUD — all Dataverse, no BFF |
| TodoDetailSidePane | Xrm.WebApi only | Todo detail — all Dataverse, no BFF |
| CreateEventWizard | BFF | Entity creation via BFF service |
| WorkspaceLayoutWizard | BFF | Layout CRUD via BFF |

---

## References

- Bootstrap pattern: `.claude/patterns/auth/spaarke-auth-initialization.md`
- Full auth patterns: `docs/architecture/sdap-auth-patterns.md`
- BFF endpoint auth: `.claude/adr/ADR-008-endpoint-filters.md`
