# ADR-044: Dataverse GUID Canonicalization at System Boundaries (Concise)

> **Status**: Accepted
> **Domain**: Dataverse Access / Client-Server Contracts / Data Integrity
> **Last Updated**: 2026-07-10
> **Source**: `fix/wizard-guid-normalization` (PR #603) + barrel export (PR #609); codifies the prevention named in FAILURE-MODES **AP-3** (GUID case → AI Search) and **AP-6** (GUID braces → `@odata.bind`).
> **Cross-references**: reinforces ADR-007 (facade handles lookup complexity — callers pass GUIDs); relates to ADR-034 (identity normalization). Pattern: [`.claude/patterns/dataverse/relationship-navigation.md`](../patterns/dataverse/relationship-navigation.md).

---

## Decision

**Dataverse GUIDs MUST be canonicalized to a single form — bare (no braces) and lowercase — at every boundary where they cross a system** (Xrm host ↔ client component/service state ↔ BFF ↔ Dataverse Web API ↔ Azure AI Search). Xrm APIs emit GUIDs in **registry format** (brace-wrapped, frequently UPPERCASE); different downstream consumers reject or mismatch non-canonical forms:

- **OData `@odata.bind` key predicate** (`/entityset(<guid>)`) — a braced GUID is rejected with HTTP **400 "Bad Request - Error in query syntax"** (a URL-parse failure that names no property).
- **Azure AI Search `Edm.String eq`** — case-sensitive, so `{UPPERCASE}` vs `lowercase` document IDs silently miss (AP-3).

The canonical normalizer is the shared **`cleanGuid`** exported from `@spaarke/ui-components` (client/TS); the BFF normalizes at its single indexing convergence point (`FileIndexingService`, per AP-3 fix `fbbaee29`).

This is a *codification of already-shipped, owner-approved behavior*, elevated to an ADR because the same root cause (Xrm registry-format GUIDs) has now caused two distinct production failures.

---

## Constraints

### ✅ MUST

- **MUST** normalize any GUID to bare-lowercase **before** each of these boundaries:
  1. Building an `@odata.bind` value or any `/entityset(guid)` reference URL (client create/update payloads, PCF write handlers).
  2. Writing/filtering an AI Search document key (`documentId` and any `Edm.String` `eq` filter).
  3. Storing an Xrm-sourced GUID into component/service state (ingestion) — so braces never propagate downstream.
- **MUST** use the shared **`cleanGuid`** for client/TS code — `import { cleanGuid } from '@spaarke/ui-components'`. It strips braces/whitespace + lowercases and is a **no-op on already-bare GUIDs**, so it is always safe to apply uniformly.
- **MUST** normalize at the boundary **you own** — do not assume the caller passed a canonical GUID (per ADR-007, the facade/consumer owns lookup complexity).
- **MUST** normalize at the platform-adapter boundary where Xrm hands a GUID into shared code (`xrmNavigationServiceAdapter.openLookup`, `xrmDataServiceAdapter.createRecord` return, `DataverseLookupField.onChange`) so *every* consumer inherits bare GUIDs.

### ❌ MUST NOT

- **MUST NOT** hand-roll per-file normalizers (`id.replace(/[{}]/g, '')`, ad-hoc `.toLowerCase()`). Scattered local copies are precisely how one wizard shipped un-normalized (AP-6). Reuse `cleanGuid`.
- **MUST NOT** interpolate a raw, un-normalized GUID directly into an `@odata.bind`/reference URL, even when "it looks clean" — the source (native picker vs typeahead vs pre-fill) determines the format, and that is easy to get wrong.
- **MUST NOT** rely on Dataverse to tolerate braces or case in a key predicate — it does not for `@odata.bind`; AI Search does not for `Edm.String eq`.

---

## Key patterns

```ts
import { cleanGuid } from '@spaarke/ui-components';

// Building a lookup bind on a create/update payload:
payload[`${navProp}@odata.bind`] = `/${entitySet}(${cleanGuid(lookupId)})`;

// Normalizing at ingestion (an Xrm-sourced GUID entering state):
const userId = cleanGuid(ctx.userSettings.userId);        // {ABC-...} -> abc-...
```

**Deep-import fallback** — for a consumer pinned to a stale `@spaarke/ui-components` that predates the barrel export (e.g. a tarball-pinned PCF):
```ts
import { cleanGuid } from '@spaarke/ui-components/dist/services/PolymorphicResolverService';
```

**BFF (C#)** — normalize at the single convergence point; do not push per-caller:
```csharp
documentId = documentId.Replace("{", "").Replace("}", "").ToLowerInvariant();
```

---

## Rationale

Two production failures, one root cause: AP-3 (case) broke Find-Similar via case-sensitive AI Search filters; AP-6 (braces) broke Create Matter/Project via `@odata.bind` 400s. Both trace to Xrm returning registry-format GUIDs and code that trusted the caller. AP-3's own prevention note already said "normalize at every boundary" — this ADR makes that a MUST with a single shared implementation, so the invariant is enforceable (`adr-check`, code review) rather than tribal knowledge that gets lost.

## Consequences

- New client code that builds binds/keys must route GUIDs through `cleanGuid` (cheap, no-op on clean input).
- Solution-local code that builds its own binds outside the shared services (e.g. SmartTodo's `DataverseService`) must normalize at its own GUID source — SmartTodo already does via `getUserId()`; EventDetailSidePane is a known audit target.
- Long-term (non-binding suggestion): branded ID types (TS) / `record struct` (C#) would make the invariant type-enforced rather than convention-enforced.
