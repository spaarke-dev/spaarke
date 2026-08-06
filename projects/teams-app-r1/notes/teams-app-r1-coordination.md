# R2 → teams-app-r1 Coordination: Proceed with Option A (it's foundational, not throwaway)

> **From**: spaarke-SPA-external-access-platform-r2 (platform foundation)
> **To**: teams-app-r1
> **Date**: 2026-07-21
> **Re**: your handoff `spaarke-SPA-external-access-platform-r1/notes/spa-v2-handoff-workforce-endpoint-gap.md` — the "principal-agnostic vs plane-partitioned" endpoint decision

---

## TL;DR — Decision

**Proceed with Option A now** (dual-scheme the `/api/v1/external/*` collaboration read + download endpoints; resolve either principal; preserve CIAM; rebuild + redeploy the BFF to finish live E2E).

R2 has adopted **principal-agnostic module endpoints** as the canonical platform pattern (R2 spec **FR-22**). **Option A is that pattern realized on the first module (collaboration / "Assigned Work") — it is R2 Phase-P1 work done early, NOT throwaway.** Option B (parallel `/collab` data set + host-aware client) is the throwaway path — **do not build it**; R2 would tear it out.

Build it to the guardrails below and it becomes a foundation stone R2 builds directly on. Send us the coordination notes (last section) when it lands.

---

## Why Option A advances R2 (and B does not)

R2's FR-22: *each module's data endpoints accept the scheme(s) for the plane(s) they serve and resolve a **plane-agnostic caller** via a `CallerPrincipalResolver` (CIAM contact by oid OR workforce user via your `WorkforcePrincipalResolver`) → a common **accessible-record-set** → the module's **Tier-2 record predicate** filters it.* One endpoint set per module, N planes.

- Your Option A **is** this pattern on the collaboration module. The `CallerPrincipalResolver` + dual-schemed handlers you build are the exact components R2 keeps.
- Option B duplicates the data surface per plane. Across R2's module set (Assigned Work, Front Door NDA/Policy, E-billing…) that's a combinatorial explosion R2 explicitly rejects. Anything you invest in a NEW `/collab` data set is sunk.

Net: **A = R2 P1 delivered early + your E2E unblocked. B = effort R2 discards.**

---

## Build guardrails (so what you build IS the R2 foundation, not a Teams patch)

1. **Resolver as a reusable abstraction, not inline branching.** Build a single `CallerPrincipalResolver` (one interface, two strategies: `CiamContactPrincipalStrategy` by `sprk_externalobjectid`/oid, `WorkforcePrincipalStrategy` = your existing resolver). Handlers depend on the abstraction, never on `if (isCiam) … else …`. This resolver is FR-22's core component — R2 will lift it into the module framework as-is.
2. **Handlers become principal-agnostic.** A handler resolves caller → `{ principal, accessibleRecordSet }` and applies the module's **Tier-2 predicate** to that set. No CIAM-contact assumptions baked into handler bodies.
3. **Preserve CIAM behavior EXACTLY.** The existing external CIAM path must be byte-for-byte unchanged in shape/behavior. Add a **regression test** asserting CIAM `/me` + `/projects` + `/documents` + download responses are identical pre/post. This is non-negotiable (R1 FR-15 parity).
4. **Keep the `/api/v1/external` path for now — don't rename, don't fork.** R2 may later relocate these under a module-oriented path, but that's a mechanical move; the resolver + dual-scheme logic carries forward regardless. Do **not** invest in new URL surface.
5. **Consolidate `/api/v1/collab`.** Fold its `/me` + download into the principal-agnostic `/external` surface (or explicitly mark `/collab` transitional and slated for removal). We must not end up maintaining two workforce entry points.
6. **Workforce path stays broker-only / no-OBO.** The workforce token authenticates to the BFF only; downstream SPE/Dataverse stays app-only, same invariant as the CIAM path (ADR-028 A1 / R2 NFR-01). Do not exchange the workforce token downstream.
7. **Confirm the workforce scheme accepts the v1 token.** Your app issues v1 access tokens (`requestedAccessTokenVersion` = null, audience `api://1e40baad-…`). Verify the BFF **workforce** JwtBearer scheme validates that audience + v1 issuer (distinct from the CIAM v2/GUID-audience path). Note it in your coordination notes.

---

## The one thing we most need you to get right (and document): the workforce Tier-2 record-scope

Your handoff says the `WorkforcePrincipalResolver` "composes the accessible-record-set (already built)." **This is the most important thing for R2.** A workforce SPA user must **NOT** see all secure projects just because they authenticated — module entitlement (can you open Assigned Work) is separate from record scope (which projects), per R2 **NFR-08 (two-tier authorization)**.

So: **what predicate determines a workforce user's accessible projects?** (e.g. internal project team membership? a `sprk_externalrecordaccess`-equivalent grant? BU/ownership? assignment?) Whatever it is, it must be a real, enforced Tier-2 predicate — and you must **document it explicitly** in your coordination notes so R2 incorporates it into the principal-agnostic model rather than re-deriving it. If today it is "any authenticated workforce user → all projects," flag that loudly — R2 will need to tighten it, and we'd rather know now.

---

## Do NOT (throwaway / anti-patterns)

- ❌ Build Option B (parallel `/collab` data endpoints + host-aware client base-path switching). R2 rejects plane-partitioning.
- ❌ Inline `if (ciam) … else (workforce)` in each handler instead of the resolver abstraction.
- ❌ Rename/relocate the endpoint group or invest in new URL surface for its own sake.
- ❌ Grant any authenticated workforce user access to all records (violates NFR-08 Tier-2).
- ❌ Exchange the workforce token downstream (OBO) — breaks broker-only.

---

## Coordination notes to send back when it lands

Please return a short note (append to your handoff or a new `notes/` file) with:

1. **Files touched** — endpoint group(s), the resolver, DI registration, tests.
2. **`CallerPrincipalResolver` shape** — the interface + the two strategy classes, and where a third plane would plug in.
3. **Workforce Tier-2 record-scope predicate** — exactly how a workforce user's accessible project set is computed + enforced (the item above). Include the code path.
4. **CIAM regression result** — evidence the external CIAM path is unchanged (test names + pass).
5. **Auth/token detail** — confirmation the workforce scheme accepts the v1 `api://1e40baad-…` audience; any scheme/config changes.
6. **Entra config applied** — the recipe items you set (multitenant, `access_as_user`, pre-authorized Teams client app-ids, `brk-multihub://{host}` SPA redirect, CSP framing) for whichever env.
7. **`/collab` disposition** — folded into `/external` or left transitional (and removal plan).
8. **Deviations** — anything you did differently from these guardrails, and why.

R2 will then: generalize the resolver into the module framework (F1/FR-22), register "Assigned Work" as the first module over these endpoints, layer the module-entitlement + admin UI (F3/F5) on top, and add the Legal Front Door modules — all reusing your resolver + dual-scheme work unchanged.

---

## R2 references
- Spec: `projects/spaarke-SPA-external-access-platform-r2/spec.md` — FR-22 (principal-agnostic endpoints), NFR-08 (two-tier authz), FR-04 (Teams host adopts your prior art), Dependencies → "Entra/Teams config recipe."
- Design: `projects/spaarke-SPA-external-access-platform-r2/design.md`.
- Code synopsis: `projects/spaarke-SPA-external-access-platform-r2/notes/external-access-capability-synopsis.md`.
