# Task 028 — Polymorphic Tier-2 scoping — notes & deviations

> Date: 2026-08-10 · Rigor FULL (opus @ xhigh) · BFF-touching (bff-api) + auth + frontend
> Binding design: `notes/external-access-polymorphic-scoping-design.md`. Supersedes the partial
> documents-by-project fix (`bff7e82e5`); amends completed tasks 015 + 016.

## /conflict-check result

SAFE (silent pass). Branch `work/spaarke-SPA-external-access-platform-r2`, synced to `origin/master`
(merged 12 SpaarkeAi/Compose commits first — **zero overlap** with `Api/ExternalAccess/**` or
`Infrastructure/ExternalAccess/**`). No open PR touches the external-access surface (only dependabot
`.csproj`/workflow bumps + unrelated SpaarkeAi/Compose/docs PRs). Merged master before deploy so the
worktree redeploy of the shared dev BFF does not regress the merged Compose/Assistant work.

## Escalation trigger — evaluated, DID NOT fire

The POML armed an escalation for "a required access source cannot be read from existing schema." All
sources were verified live via MCP `describe` before any code was written — the trigger did not fire:

| Needed | Verified live |
|---|---|
| Grant table typed root lookups | `sprk_externalrecordaccess` has `sprk_project`, `sprk_matter`, `sprk_workassignment` (+ `sprk_invoice`, `sprk_recordtype`) — the typed lookups self-disambiguate, so no `sprk_recordtype_ref` GUID map is needed for reads |
| Document parent lookups | `sprk_document.sprk_project` / `.sprk_matter` / `.sprk_workassignment` all present |
| Invoice parent lookups | `sprk_invoice.sprk_matter` / `.sprk_project` present |
| Service-Request submitter | `sprk_servicerequest.sprk_requestedby` (lookup → contact) present |

**No Dataverse schema change** — exactly as the design predicted.

## §10 BFF Hygiene — Placement Justification

- **Where**: all BFF changes are in the isolated external-access corner — 7 existing files under
  `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Infrastructure/DI/ExternalAccessModule.cs`.
  No new file, no new endpoint, no new route, no new package.
- **No new AI dependency**; no `IOpenAiClient`/`IPlaybookService`/AI-internal references.
- **Feature-module DI (ADR-010)**: modules registered via the existing `AddExternalModule` extension;
  `ScopeDimension`/`ExternalGrantSet` are data classes (no new interface).
- **Authz (ADR-008)**: unchanged — modules ride the inherited group filter; no route/filter/handler change.
- **Broker-only (ADR-028)**: the new `GetGrantSetAsync` uses the same app-only token path as the prior
  project-only query; no OBO, no Graph SDK types, no client-contract change.
- **Publish size**: Release, compressed incl PDBs (Compress-Archive Optimal, same tool as task 016) =
  **48.44 MB** vs 48.29 MB baseline → **+0.15 MB** (≤60 MB ceiling; +5 MB single-task and 55 MB
  cumulative thresholds NOT hit). `dotnet list package --vulnerable --include-transitive` → **no
  vulnerable packages** (no package reference changed).
- **Tests (ADR-038)**: existing KEEP-path contract/seam tests updated for the widened seam; new
  domain/contract tests added (OR injector, multi-dimension descriptor, matter/WA grant composition).
  No banned patterns introduced. Full unit suite **10259 passed / 0 failed / 101 skipped**.

## §11 Component Justification (new surface)

| New component | Existing? | Extend? | Cost of doing nothing (concrete) |
|---|---|---|---|
| `ScopeDimension` (data class) | none (descriptor had a single attr) | descriptor generalized in place | matter/WA-linked children stay hidden — the concrete over-hide this task fixes |
| `ExternalGrantSet` (data class) | `ExternalParticipation` (project-only) | project-only shape can't carry matter/WA grants | partner matter/WA grants unreadable → those tabs empty for granted partners |
| `service-requests` module | none | n/a (new entity surface) | no server-side internal SR read; partner could otherwise reach SRs |
| `ServiceRequestsWidget` (client) | 5 grid widgets exist | thin `createGridWidgetBody` binding — reuses them | no internal SR read tab |

## Deviations

### D-028-1 — Matters is now a REAL root predicate (supersedes D-016-1 Path-A stub)
Task 016 registered `matters` with an always-empty accessible set (documented Path-A exception, because
no Contact→Organization resolver existed). Task 028 makes matter access an explicit
`sprk_externalrecordaccess` grant of `recordtype=Matter` — exactly like project access — so the matter
predicate is `sprk_matterid ∈ M` (CIAM: matter grants; workforce: membership ∪ matter grants). **No
Contact→Organization resolver was needed** (the D-016-1 blocker is dissolved, not worked around). Grid
config `emptyStateMessage` updated from "coming soon" to a real empty message.

### D-028-2 — Work Assignments re-scoped from `sprk_regardingproject` to its OWN id
Task 016 scoped WA by `sprk_regardingproject ∈ P`, which hid any WA not tied to an accessible project —
including standalone grant-only WAs (the owner's "WA with docs, no project/matter" workflow). Task 028
scopes WA by `sprk_workassignmentid ∈ W` (its own id ∈ the caller's accessible WA set), making WA a
first-class grantable root. The WA grid config already projects `sprk_workassignmentid`, so no config
change was needed.

### D-028-3 — `service-requests` distinct from the P3 `my-requests` placeholder
The client already has a `my-requests` placeholder (P3 Legal Front Door aggregate). Task 028 adds a
distinct data-backed `service-requests` widget (the concrete `sprk_servicerequest` read). They may
reconcile in P3 (my-requests may absorb service-requests); for now both are workforce defaults. This is
the minimal correct delivery of the owner's "internal Service Requests tab" without pre-empting the P3
Legal Front Door design.

### D-028-4 — Cache version bumped 1→2
`ExternalParticipationService` cache shape widened from project-only participations to the full grant
set. `CacheVersion` bumped 1→2 (orphaned v1 entries expire on their 60s TTL). The two tests that mirror
the version constant (`ExternalParticipationServiceInvalidationTests`, `StandingGrantRuntimeUnionSeamTests`)
were updated to 2.

### D-028-5 — Test-double seam moved to `GetGrantSetAsync`
`GetGrantSetAsync` is the new virtual grant loader; `GetParticipationsAsync` delegates to it (project
slice) for back-compat. The 3 `ExternalParticipationService` subclasses + 1 Moq mock were moved to
override/setup `GetGrantSetAsync`. One prior test asserting "matter grants are skipped (project-scoped)"
was rewritten to the task-028 contract (matter grants ARE consulted; project grants do NOT leak into a
matter query).

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors** (23 pre-existing warnings, none in touched files).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/`: **10259 passed / 0 failed / 101 skipped** (incl. the
  external-access unit + seam + contract suites, 189 external-access tests green).
- `npx tsc --noEmit` (external-spa): **clean**. `npm run build` (Vite): **succeeded** (2515 modules).
- Publish 48.44 MB (+0.15 vs baseline); no vulnerable packages.
- Quality gates (code-review + adr-check, Step 9.5): **no Critical/Warning**; 1 doc-drift suggestion
  fixed (stale `TryGetRecordId` header comment → `TryGetAttributeId` + task-028 note). ADR scan clean
  (ADR-010 no new interface; ADR-008 inherited filter; ADR-028 broker-only + no client-contract change;
  ADR-013 no AI types).
- **Deployed**: `spaarke-bff-dev` via `scripts/Deploy-BffApi.ps1` (from worktree, NOT CI) — health check
  passed, SHA-256 verified 4 critical files match local build. Grid configs updated live via MCP
  (Documents +matter+workassignment; Invoices +matter; Matters real empty-state; new Service Requests
  config `403e5d37-cb94-f111-b8db-00224835447a`).

## Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | Partner sees project/matter/WA-linked docs in ONE Documents tab across all accessible roots | **Met (code + unit-verified)** — Documents module OR's [project|matter|workassignment]; multi-dim ScopeRows tests prove roll-up; live both-plane UAT owner-pending |
| 2 | Invoices for accessible matters OR projects; Matters + WA tabs populated | **Met (code)** — Invoices OR [matter|project]; Matters by M; WA by W |
| 3 | SR tab ABSENT on partner SPA; internal shows only `sprk_requestedby == caller contact` | **Met** — client `planes:['workforce']` + server fail-closed for non-workforce |
| 4 | Negative (partner grant-only): no grant of a type → no roots/children; all-empty → 0 rows no query | **Met** — CIAM grant-only; endpoint short-circuits all-empty; `ScopeRows` fail-closed (tests) |
| 5 | Negative (over-read): child with all parents outside accessible sets never appears | **Met** — OR injector + `ScopeRows` (tests) |
| 6 | Matter-linked doc previously hidden by bff7e82e5 now appears for matter-granted caller | **Met** — explicit superseding test `ScopeRows_Documents_MatterLinkedDoc_VisibleToMatterGrantedCaller` |
| 7 | Publish ≤60 MB; dotnet test green; no new HIGH CVE; conflict-check clean | **Met** — 48.44 MB; 10259 pass; no CVE; conflict-check clean |

## Owner action pending
**Live both-plane UAT** (log in as workforce + CIAM partner, confirm row counts on Documents/Matters/
Invoices/Work Assignments + internal-only Service Requests) — the empirical App Insights `[EXT-MODULE]`
verification requires a live SPA session, same as the task-019 "live auth E2E owner-pending" pattern.
The code is deployed + health-verified; unit/seam/contract coverage proves the scoping logic.
