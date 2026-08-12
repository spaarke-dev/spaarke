# Task 072 — Tier-1 module entitlement (Option B) — deviations & verification

> 2026-08-11 · FULL rigor · opus @ high · directional. BFF resolver + `/me/entitlements` endpoint +
> external-spa widget tab sets. Branch `work/spaarke-SPA-external-access-platform-r2`. BFF deployed to
> spaarke-bff-dev from worktree.

## Headline scope-reality finding (surfaced to owner, §6.5)

The POML framed 072 as "amends tasks 021 (resolver) + 022 (/me)". **Empirical check found 021/022 were
never built** — there was no entitlement resolver, `sprk_approlemodulemap` had zero code references, and
the only `/me` (`ExternalUserContextEndpoint`) returns Tier-2 project access with **no `entitlements[]`
field**. The client `me-client.ts` was returning a **client-side mock** with a `TODO(task-022)` to call
a not-yet-existing `/api/v1/external/me/entitlements`. So **072 is the PRIMARY implementation** of Tier-1
entitlement, not an amend. The owner was shown this and agreed to the scope below (2026-08-11).

## What shipped (owner-agreed scope)

### BFF (new)
- **`ModuleEntitlementResolver`** (`Infrastructure/ExternalAccess/`) — Option B per-plane resolution:
  - **Workforce (internal)**: the caller's Entra **App-Role** claims (`roles`) ∩ active
    `sprk_approlemodulemap` rows (`sprk_approlename → sprk_modulecode`) → de-duplicated module codes.
    **No Contact record involved** (the NFR). Adding a role→module mapping later is one data row (FR-08).
  - **CIAM (external)**: BLANKET-entitled to the outside-counsel set (`['assigned-work']`) — no
    per-Contact entitlement table; record visibility stays Tier-2 (028/070/071).
  - Typed `HttpClient` (app-only Dataverse read, broker-only) + 60s Redis map cache (ADR-009 — DATA, not
    a decision); concrete DI registration (ADR-010). Pure resolution core (`ResolveWorkforceEntitlements`,
    `ExtractAppRoles`) is directly unit-tested.
- **`GET /api/v1/external/me/entitlements`** (`MeEntitlementsEndpoint`) returning `MeEntitlementsResponse
  { displayName, email, plane, entitlements[] }` — the exact shape the client contract already declares.
  Slots into the existing dual-scheme `/api/v1/external` group (inherits `CallerPrincipalAuthorizationFilter`).

### external-spa (owner tab sets)
- **CIAM widgets** (Projects/Matters/Work Assignments/Documents/Invoices): dropped
  `requiredEntitlement:'assigned-work'` → blanket-visible to all CIAM (Option B).
- **Internal defaults** now = Service Requests + Policy Library (+ Quick Start pinned, rendered by
  `QuickStartPane`, not the registry). `my-requests` + `inventions` set to `defaultForRoles: []` (still
  entitled via `legal-front-door`, just not default tabs); `messages` dropped from the workforce default
  (kept as an admin default).
- **`me-client.ts`**: values already matched the server (workforce `['legal-front-door','policy-library']`
  = seeded map; ciam `['assigned-work']` = blanket set) — updated the TODO to note the endpoint now exists.

## Deviation D-1 — client mock→real flip deferred to task 073 (owner-agreed)

`me-client.ts` still returns its mock; the swap to
`bffApiCall('/api/v1/external/me/entitlements')` is **deferred to task 073**, co-located with the deploy +
**dual-plane UAT** that a live auth call needs (a real workforce token WITH the `FrontDoorUser` App-Role,
and a real CIAM token). The mock is deliberately value-aligned with the server, so 073's flip is a
one-line change. Rationale: don't make the working SPA depend on an unverified live auth path mid-wave.
Owner agreed 2026-08-11.

## Verification

| Check | Result |
|---|---|
| `ModuleEntitlementResolver` unit tests | **12 pass** — (1) FrontDoorUser→{legal-front-door,policy-library}; (2) FR-08 add-a-row→+admin, no code change; (3) CIAM blanket=['assigned-work'] + never reads the map; (4) negative: unmapped role→∅, no-roles→∅; App-Role claim extraction (roles + ClaimTypes.Role, dedup) |
| External-access BFF suite | **239 pass / 0 fail** (no regressions) |
| external-spa `vite build` | clean (2515 modules; widgetRegistry + me-client typecheck) |
| Seeded map (live, MCP) | `FrontDoorUser → {legal-front-door, policy-library}` (2 active rows) — matches the workforce unit expectation |
| Publish size | **48.46 MB** compressed (baseline 48.45 → **+0.01 MB**); ≤60 ceiling; no new NuGet/CVE |
| BFF deploy (worktree) | Deployed to spaarke-bff-dev; `/healthz` passed; 4 critical files SHA-256 verified |
| Endpoint live | `GET /me/entitlements` → **401** unauthenticated (registered + auth-gated); sibling nonexistent route → 404 (proves real registration) |

## ADR / hygiene compliance
ADR-001 (Minimal API) · ADR-008 (group policy + `CallerPrincipalAuthorizationFilter`, no global
middleware) · ADR-009 (caches the small global map as DATA, 60s TTL, never a decision) · ADR-010
(concrete resolver, no interface) · ADR-028 (app-only Dataverse read, no OBO, no token props) · §10
Placement Justification: external-access corner (not AI), reuses the `ExternalParticipationService` typed-
HttpClient pattern, no new endpoint group/package, tests added · §11: extends the existing `/me` surface +
the single owner-created Option-B table; no new abstraction.

## Owner-pending (task 073 both-plane UAT)
1. Flip `me-client.ts` → live `/api/v1/external/me/entitlements`.
2. Live positive check: a **workforce** token with the `FrontDoorUser` App-Role → `/me/entitlements`
   returns `['legal-front-door','policy-library']`; the SPA shows Quick Start + Service Requests + Policy
   Library (no my-requests/inventions/messages as defaults). A **CIAM** token → `['assigned-work']`; the
   SPA shows the 5 outside-counsel tabs.
3. FR-08 live: seed a new `sprk_approlemodulemap` row → the role's `/me` entitlements change with no deploy.
