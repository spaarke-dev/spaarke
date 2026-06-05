# Data Access Decision Criteria — `Xrm.WebApi` vs BFF

> **Status**: Active (binding)
> **Created**: 2026-05-26
> **Source**: R4 spec DR-06 / backlog item C-1
> **Audience**: Anyone touching Dataverse data from a Spaarke client surface (Code Pages, PCF controls, web resources, BFF endpoints, or BFF-backed services)
> **Companion**: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (BFF-side governance — load alongside this doc when adding BFF code)

---

## TL;DR — Decision tree (≤30 seconds)

Read the question. Pick the first matching row.

| Question | Answer |
|---|---|
| Is this **server-side .NET code** (BFF, plugin, function, worker)? | **NEVER `Xrm.WebApi`.** Server uses `IDataverseClient` / `Sprk.Dataverse` SDK. Skip the rest of this table. |
| Does the path involve **AI synthesis, orchestration, or playbook execution**? | **BFF.** AI never runs from the client. |
| Does the operation **cross systems** (Dataverse + Graph + SPE; Dataverse + external API)? | **BFF.** One transaction, server-mediated. |
| Does the operation require **OBO-protected resources** (Graph on behalf of user; SPE containers; external access accounts)? | **BFF.** Token exchange happens server-side per ADR-028 §OBO. |
| Does the operation **provision infrastructure** (BU creation, SPE container creation, role assignment)? | **BFF.** Multi-step transactions with rollback semantics. |
| Does the operation need **server-side validation, business logic, or audit** beyond what a Dataverse plugin gives you? | **BFF.** |
| Is this a **bulk write** of >50 records, or a **bulk read** that needs aggregation/pagination across multiple entity types? | **BFF.** (Or a Service Bus job dispatched by the BFF.) |
| Is this a **subscription / streaming / SSE response** (chat completion, long-running analysis)? | **BFF.** Server-Sent Events / chunked responses. |
| Is this a **single-entity read or single-record write** to one Dataverse table, host-context, no cross-system coupling? | **`Xrm.WebApi`.** Client-direct, no BFF round-trip needed. |
| Is this a **simple lookup, picklist hydration, formatted-value fetch** scoped to the current user/host context? | **`Xrm.WebApi`.** |
| Default if no row above matched | **Document the ambiguity in the task notes and ask.** Do not "just pick one." |

If the answer is BFF, you must also read [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) and apply CLAUDE.md §10 BFF Hygiene (Placement Justification + publish-size verification + facade rule).

---

## Why this document exists

SpaarkeAi makes Dataverse reads/writes from **both** sides:

- **Client side** — Code Pages (`src/solutions/SpaarkeAi`, `src/solutions/LegalWorkspace`, etc.), PCF controls, web resources call `Xrm.WebApi.*` directly against the host Dataverse environment.
- **Server side** — BFF endpoints (`Sprk.Bff.Api`) call Dataverse via `Sprk.Dataverse` (`IDataverseClient`) and Microsoft Graph via `SpeFileStore` to mediate AI, cross-system writes, and OBO-protected resources.

Without explicit criteria, new code drifts toward whichever pattern the author saw last. The result is operational surprises: AI calls being attempted from the client; bulk operations running through `Xrm.WebApi` and hitting rate limits; multi-system writes spread across the client and the server with no coherent transaction boundary; cross-system reads doing N+1 fetches in JavaScript instead of one BFF aggregation.

This document settles the question **before** code is written. It pairs with the BFF-side governance in [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) and CLAUDE.md §10 BFF Hygiene — together they form the complete decision frame.

---

## Decision Criteria (the 7 dimensions)

Each criterion is a one-paragraph rule. Apply all 7 when the decision tree above does not give a clean answer.

### 1. Authentication model

- **`Xrm.WebApi`** runs in the **host Dataverse session** — the user is already authenticated via the Power Platform host, and `Xrm.WebApi` operates within that session's permissions. No token is exchanged; no MSAL flow is involved. ADR-028 invariants (token snapshots, function-based auth) do **not** apply because no Bearer token is being acquired.
- **BFF** is reached via **Spaarke Auth v2** (ADR-028) using `authenticatedFetch` from `@spaarke/auth`. The auth surface acquires a Bearer token through MSAL (`acquireTokenSilent` → `ssoSilent` → redirect fallback), the BFF validates it, and OBO exchange happens server-side to reach downstream resources (Graph, SPE, Dataverse-as-server). NO token snapshotting in client props/state — pass `authenticatedFetch` as a function dependency, not the token itself.

**Rule**: If you need OBO to a downstream resource (Graph, SPE, external API), you MUST go through the BFF — `Xrm.WebApi` cannot do OBO. If the only resource is the host Dataverse and the user is in-session, `Xrm.WebApi` is sufficient.

### 2. Data complexity

- **`Xrm.WebApi`** is correct for **single-entity queries** (one logical entity per call, even with related-table `$expand`), **simple OData filters**, and **single-record CRUD** (`createRecord` / `updateRecord` / `deleteRecord`). Up to ~5 logical entities in a single user-facing view.
- **BFF** is correct for **cross-entity aggregations** (e.g., "portfolio health" combining matters + projects + events + invoices), **server-side joins** that would otherwise become N+1 in JavaScript, and **server-side projections** that pre-shape data for a specific UI surface. Aggregations belong on the server; one BFF call is cheaper than 5 `Xrm.WebApi` calls + client-side combination.

**Rule**: If the user-facing view would require more than 3 round-trips from the client, fold them into a single BFF endpoint.

### 3. AI involvement

- **`Xrm.WebApi`** MUST NOT be used for any operation that involves AI synthesis, retrieval, playbook execution, embedding generation, or orchestration. AI does not run from the client surface.
- **BFF** is the only path for AI. The BFF mediates the LLM call (Azure OpenAI), applies safety/audit/feature flags, manages the single-LLM-call-per-turn invariant (D-01), and persists session/audit state in the same request lifecycle.

**Rule**: The moment AI enters the path, the call is BFF. Period.

### 4. Audit trail requirements

- **`Xrm.WebApi`** writes are audited by Dataverse itself (the standard audit log on the entity). If the only required audit is "what record was changed by whom and when," Dataverse's built-in audit is sufficient.
- **BFF** is required when audit needs to capture **cross-system context** — who initiated the write, which AI orchestration produced the change, what session correlation ID applies, what feature-flag scope was in effect. The BFF writes structured audit events (`AuditEvent`, `AiAuditEvent`) that ride alongside the Dataverse write.

**Rule**: If audit needs to record more than "user X wrote field Y," use the BFF so the audit event can capture the surrounding orchestration.

### 5. Error handling and retry semantics

- **`Xrm.WebApi`** errors surface as JavaScript exceptions; the client wraps them (see `IResult<T>` / `tryCatch` in `LegalWorkspace`'s `DataverseService.ts`). Retry semantics are the caller's responsibility — there is no built-in retry layer. 401/403 means the user lost session; the host handles re-auth.
- **BFF** returns RFC 7807 `Problem` responses; `authenticatedFetch` handles 401 retry automatically (invalidates `InMemoryCache`, re-acquires via the auth strategy, retries once). Server-side, the BFF can apply per-endpoint rate limiting, exponential backoff for transient downstream errors (Graph throttling, OpenAI 429), and idempotency tokens.

**Rule**: If you need automatic 401 retry, RFC 7807 error shapes for the UI, or server-mediated backoff on a transient downstream, use the BFF.

### 6. Retries, concurrency, and rate limits

- **`Xrm.WebApi`** shares the host's Dataverse rate-limit allocation. Bulk operations from the client (e.g., updating 200 records in a `for` loop) consume the user's per-session budget and can throttle the entire Power Platform host. There is no `If-Match` ETag support in `Xrm.WebApi` for optimistic concurrency.
- **BFF** has its own service principal (where applicable) plus the user's delegated allocation under OBO. It applies named rate-limit policies (`graph-read`, `graph-write`, `ai-stream`, `ai-batch`). It supports `If-Match` ETag headers for concurrency (see R4 B-5). For bulk writes, the BFF dispatches a Service Bus job; the client gets an immediate 202 and polls or subscribes for completion.

**Rule**: Anything that touches >50 records, or anything that requires optimistic concurrency, goes through the BFF.

### 7. Streaming, subscriptions, long-running

- **`Xrm.WebApi`** is request/response only. There is no SSE, no WebSocket, no chunked transfer.
- **BFF** supports SSE for chat completion and long-running AI streams; supports chunked transfer for large payloads; can dispatch background jobs and stream progress.

**Rule**: If the response is streamed (chat, analysis), or the operation is long-running with progress events, use the BFF.

---

## Worked example — `Xrm.WebApi` (LegalWorkspace `DataverseService.ts`)

**Surface**: LegalWorkspace Code Page (and other host-context callers).
**Need**: Fetch the current user's top-5 most recent matters for a left-hand summary card.

```ts
// src/solutions/LegalWorkspace/src/services/DataverseService.ts (excerpt)

async getMattersByUser(
  userId: string,
  options: { top?: number } = {}
): Promise<IResult<IMatter[]>> {
  const top = options.top ?? 5;
  const query = buildMattersQuery(userId, top);

  return tryCatch(async () => {
    const result = await this._webApi.retrieveMultipleRecords('sprk_matter', query, top);
    return toTypedArray<IMatter>(mapMatterFormattedValues(result.entities));
  }, 'MATTERS_FETCH_ERROR');
}
```

**Why `Xrm.WebApi` is correct here** (mapped to the criteria above):

1. **Auth** — host session is sufficient; the user is already authenticated in the Power Platform host and the query runs under their permissions. No OBO needed.
2. **Complexity** — single entity (`sprk_matter`), one OData query, returns ≤5 records.
3. **AI** — none.
4. **Audit** — read-only operation; Dataverse audit (if enabled) covers the read.
5. **Errors** — `tryCatch` wraps the call into `IResult<T>`; the UI surfaces a friendly error code (`MATTERS_FETCH_ERROR`).
6. **Concurrency / rate limits** — single small read, well under any threshold.
7. **Streaming** — no.

This is exactly the right shape: **host-context, single-entity, read-only, no AI, no OBO.** A BFF round-trip here would add latency, an MSAL token acquisition, and a server hop for zero benefit.

**Companion pattern in the same file**: `getEventsFeed`, `getProjectsByUser`, `getInvoicesByMatter`, `getDocumentsByMatter` — all simple host-context single-entity queries. See `DataverseService.ts` lines 164–390. The file's header comment codifies the rule: *"Simple entity queries use `Xrm.WebApi`, NOT BFF endpoints; complex aggregations go to BFF; AI features go to BFF."*

---

## Worked example — BFF (LegalWorkspace `provisioningService.ts`)

**Surface**: LegalWorkspace "Create Project" wizard.
**Need**: Provision a Secure Project — create a child Business Unit, create an SPE container, create an External Access Account, store all references on the project record. Four discrete steps; if any fails, the others must be reconciled.

```ts
// src/solutions/LegalWorkspace/src/components/CreateProject/provisioningService.ts (excerpt)

import { getBffBaseUrl } from '../../config/runtimeConfig';
import { authenticatedFetch } from '../../services/authInit';

export async function provisionSecureProject(
  request: IProvisionProjectRequest
): Promise<IProvisionProjectResult> {
  const url = `${getBffBaseUrl()}/api/v1/external-access/provision-project`;
  const response = await authenticatedFetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  // ... RFC 7807 error handling ...
}
```

**Why BFF is correct here** (mapped to the criteria above):

1. **Auth** — provisioning the BU + SPE container + external-access account requires OBO to Graph and to the External Access service. `Xrm.WebApi` cannot do OBO; the BFF mediates the token exchange per ADR-028.
2. **Complexity** — four discrete writes across three systems (Dataverse BU, SPE container API, External Access service, then back to Dataverse to store references). One server-side transaction; client gets a single result.
3. **AI** — none in this specific call, but the surrounding wizard step (Create Project closure) does involve AI; consistency keeps the whole flow server-mediated.
4. **Audit** — the BFF writes structured audit events capturing which user initiated the provision, what BU was created, what SPE container ID came back. Dataverse audit alone would not capture the cross-system context.
5. **Errors** — RFC 7807 errors; `authenticatedFetch` retries 401 once; the BFF handles transient Graph throttling internally with backoff.
6. **Concurrency / rate limits** — provisioning is throttled by the BFF rate-limit policy; concurrent BU creations across users are serialized server-side.
7. **Streaming** — no, but the wizard shows step-by-step progress via the `PROVISIONING_STEPS` UI indicator while the single BFF call runs.

This is exactly the right shape: **cross-system, OBO-protected, multi-step transaction, requires server-side reconciliation.** Doing this from the client with chained `Xrm.WebApi` calls would (a) require Graph access from the client which Spaarke does not allow, (b) leave the system in an inconsistent state if any step fails, (c) bypass the structured audit.

**Companion pattern**: `workspaceLayoutMutations.ts` (SpaarkeAi) — uses BFF for layout writes because they require server-side validation, concurrency safety (B-5 PATCH + ETag), and audit. The file's header comment explicitly cites CLAUDE.md §10 BFF Hygiene as the standard.

---

## Anti-patterns

These are the failure modes this document exists to prevent.

### 1. **Do not call `Xrm.WebApi` from server-side .NET code**

`Xrm.WebApi` is a **browser API**. It exists only in the Power Platform host (model-driven app, Code Page, PCF). It is not callable from a BFF endpoint, a plugin, a function, or a worker. Server-side .NET uses `IDataverseClient` (`Sprk.Dataverse`) or the Dataverse Web API via the .NET SDK. If you find yourself searching for "how do I call Xrm.WebApi from C#," stop — you have the wrong abstraction. Use `IDataverseClient`.

### 2. **Do not double-write via both paths**

Never write the same record from the client (`Xrm.WebApi.updateRecord`) AND from the BFF in the same operation. Pick one. If the BFF writes are authoritative (because they include audit/AI/cross-system context), do NOT mirror the write from the client — the client should refetch after the BFF call, not write its own copy. Double-writes cause race conditions (which write wins?), audit gaps (which event is the truth?), and confused operators.

### 3. **Do not bypass the BFF for AI even if "it would be faster"**

Even if Azure OpenAI accepts direct calls from a browser with an API key, the client MUST NEVER call AI directly. The BFF enforces safety policies, applies feature flags, manages the single-LLM-call-per-turn invariant, writes structured audit events, and (critically) is the only place the Azure OpenAI key/credential lives. Direct browser → OpenAI is a security and audit failure.

### 4. **Do not snapshot tokens in props/state to "save round trips"**

ADR-028 invariants forbid token snapshots. Always pass `authenticatedFetch` (a function) as a dependency, not the raw token string. If a hook needs to make a BFF call, accept `authenticatedFetch` as a parameter (see `useWorkspaceLayouts` in SpaarkeAi for the canonical shape). The `@spaarke/auth` package handles cache invalidation and refresh — opaque to callers, intentional.

### 5. **Do not invent new BFF endpoints when an existing one fits**

Per CLAUDE.md §10 BFF Hygiene, the BFF already has ~120 endpoints. Before adding a new one, search the existing surface (`Sprk.Bff.Api/Endpoints/`). The R4 `workspaceLayoutMutations.ts` task explicitly notes: *"No new BFF endpoints were added — this is purely a client-side convenience layer."* That is the right instinct.

### 6. **Do not put AI access behind `Xrm.WebApi` by routing through a Dataverse plugin**

If a Dataverse plugin makes an outbound HTTP call to Azure OpenAI (or to anything AI-related), that is the wrong place for the AI logic. AI orchestration belongs in the BFF, not in a plugin handler. Plugins are for synchronous data integrity rules and cascading writes — not for LLM calls or playbook execution.

---

## Cross-system invariants (the lines this doc does not redraw)

This document focuses on the **client-side decision** (`Xrm.WebApi` vs BFF call). The **BFF-side decision** (where in the BFF the work goes, whether it justifies a new endpoint or a new service, what packages it pulls in, whether it stays under the publish-size budget) is governed separately:

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for any BFF addition. Load whenever the answer above is "BFF."
- **CLAUDE.md §10 BFF Hygiene** — Placement Justification (every BFF-touching task must state and justify the placement decision), publish-size verification (≤60 MB compressed baseline), facade rule (no new direct `IOpenAiClient`/`IPlaybookService` injections outside `Services/Ai/`).
- **ADR-028** ([`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)) — Spaarke Auth v2 invariants; defines `authenticatedFetch` and the function-based auth contract.
- **ADR-001** Minimal API and Workers — endpoint patterns.
- **ADR-013 (refined 2026-05-20)** AI architecture — placement criteria for AI work; default is BFF, exceptions are explicit.
- [`docs/standards/INTEGRATION-CONTRACTS.md`](INTEGRATION-CONTRACTS.md) Seam 1 — full Frontend ↔ BFF contract (RFC 7807 errors, 401 retry semantics, rate-limit policies).

---

## Verification — 3 scenarios

The three checks below validate the criteria reach the right answer:

### Scenario A — A user opens a record form and the form script needs to fetch the user's preferences for a related entity.

- Host-context? Yes.
- AI? No.
- Cross-system? No.
- OBO needed? No.
- Bulk? No.
- Streaming? No.

**Verdict: `Xrm.WebApi.retrieveRecord('sprk_userpreference', ...)`.** Direct client call. Matches the `DataverseService.getUserPreferences` pattern.

### Scenario B — The SpaarkeAi chat panel needs to send a 12 MB PDF as an attachment, have it extracted and embedded, and have the response stream back to the user.

- AI? Yes (embedding, chat completion).
- Streaming? Yes (SSE).
- OBO? Yes (SPE container access).
- Cross-system? Yes (Dataverse audit + Azure OpenAI + SPE).

**Verdict: BFF** (`/api/chat/completion` SSE endpoint). The R4 A-4 attachment cap task targets exactly this path. Anti-pattern #3 makes this unambiguous.

### Scenario C — A wizard step needs to update 200 child records of a project to set a new owner.

- Bulk write? Yes (200 records).
- Cross-system? No.
- AI? No.
- OBO? No.
- Audit? Standard Dataverse audit may be sufficient.

**Verdict: BFF** (criterion #6 — bulk > 50 records, criterion #2 — N+1 in JS is wasteful). The BFF dispatches a Service Bus job (`IJobHandler<UpdateOwnersJob>`); the wizard shows a progress UI and polls or subscribes for completion. If the project later needs cross-system audit (AI orchestration produced the new owner assignment), criterion #4 also pushes to BFF.

The criteria reach the right answer in all three scenarios.

---

## Cross-links

- **BFF-side governance** — [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)
- **BFF Hygiene §10** — root CLAUDE.md §10 (Placement Justification + publish-size verification)
- **Spaarke Auth v2** — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- **Frontend ↔ BFF Seam contract** — [`docs/standards/INTEGRATION-CONTRACTS.md`](INTEGRATION-CONTRACTS.md) Seam 1
- **Anti-patterns catalog** — [`docs/standards/ANTI-PATTERNS.md`](ANTI-PATTERNS.md)
- **AI architecture** — [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md)

---

*Maintained by the project owner. Updates that change the decision tree or add a new criterion MUST add a row to [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md).*
