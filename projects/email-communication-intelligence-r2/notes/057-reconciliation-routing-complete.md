# Task 057 — Reconciliation routing (category→team + per-team views, FR-E7) — COMPLETE (2026-08-10)

**Rigor**: authored STANDARD → **overridden UP to FULL** (modifies `.cs` + `.tsx`; BFF touch → §10). Sonnet-tier work.
**Result**: BFF build 0 errors; Communication seam **150/150**; package jest **31 suites / 222 tests**; §10 verified; Step 9.5 + /conflict-check clean. No new entity (ADR-045).

## What shipped — two halves, no new entity

### (a) Backend — category→team assignment at triage time (ADR-018 config)
- **`Configuration/CategoryRoutingOptions.cs`** — clones the `AutoFileOptions` shape: `Enabled` (opt-in, default false) + `CategoryToTeam` (category-name → team-name, case-insensitive) + per-tenant `Tenants` override. Bound from `Communication:CategoryRouting`, consumed via `IOptionsMonitor` → an operator adds/removes a mapping or flips routing off **with no redeploy** (the ADR-018 guarantee).
- **`Services/Communication/Engine/CategoryRoutingGate.cs`** — clones `AutoFileGate`: pure, side-effect-free resolution of category-name → owning-team-name (or null when disabled/blank/unmapped), honoring the per-tenant override. Concrete class, registered unconditionally (ADR-010; no Null-Object peer needed).
- **`CommunicationEnrichmentService.PersistTriageResultAsync`** — after resolving the triage category, `AssignOwningTeamAsync` asks the gate for the owning team, resolves the team id (same name-lookup read path as the category resolver — no new query mechanism), and sets `fields["ownerid"] = new EntityReference("team", teamId)` on the **SAME additive triage `UpdateAsync`** (ADR-024 — an ownership set, **not** a second regarding/write path). Wrapped in its own try/catch so a routing failure (unknown team / query error) **never drops the triage fields or fails capture** (NFR-04). Unmapped category / disabled routing / unknown team → `ownerid` untouched (default/unassigned view; never a forced mis-assignment).
- **DI** (`CommunicationModule`): `Configure<CategoryRoutingOptions>` + `AddSingleton<CategoryRoutingGate>` (both unconditional).

### (b) Frontend — per-team FILTERED grid views (framework mechanism, not a fork)
- **`per-team.gridconfiguration.json`** — the needs-review view + `behavior.membershipFilter: { roles: ["owner"], identityTypes: ["team"] }` (and `ownerid` added to the fetch). The shared DataGrid framework resolves the caller's TEAM ownership and overlays an `IN(ids)` condition → each team sees only its reconciliations. `ReconciliationGrid` **already forwards** `membershipResolver` to `<DataGrid />` (task 050 left the seam wired), so no `.tsx` change was needed.
- The default **`needs-review`** config deliberately has NO `membershipFilter` (the everyone/default-unassigned queue) — an unmapped/unassigned email still surfaces there (AC #5).

## §11 / component justification
`CategoryRoutingOptions` + `CategoryRoutingGate` are new but justified: no category→team routing existed; they clone the blessed `AutoFileOptions`/`AutoFileGate` ADR-018 pattern (no engine fork), and the assignment extends the existing triage-persist `UpdateAsync`. Cost-of-doing-nothing: FR-E7 routing has no config, no assignment, no per-team view.

## Notable design points / documented posture
- **`ownerid` = team** on a plain `UpdateAsync` performs a Dataverse assign (standard). The seam test verifies the `fields` dict carries the team `ownerid`; the live assign is an integration/deploy concern.
- **Per-tenant** override is bound + unit-tested at the gate, but the triage path calls `ResolveTeamName(category, tenantKey: null)` (global map) — tenant-scoped resolution activates when a tenant key is plumbed (matches `AutoFileOptions`' single-org-default posture). Documented in the config remarks + `AssignOwningTeamAsync`.
- **`assigned-team` vs `assigned-user`**: this ships team-owner assignment (the common "route to a team" case). A user-owner variant is a config extension (map to a systemuser) — not needed for FR-E7.

## Tests
- **Triage seam** (`EmailTriageSeamTests`, +2): routing enabled + mapped category → `EnrichAsync` sets `ownerid` = the mapped team on the triage update (full slice: gate → team lookup → ownerid); routing enabled + unmapped category → the triage update runs WITHOUT `ownerid`. (Every other triage/enrichment seam test now constructs the service with `TestRoutingGate.Disabled()` — proving disabled = no-op, backward-compatible.)
- **Gate unit** (`tests/unit/domain/Communication/CategoryRoutingGateTests`, 6): enabled+mapped→team; disabled→null; unmapped/blank/whitespace/null→null; per-tenant override→tenant team; per-tenant disable→null.
- **Frontend** (`reconciliation-routing.test.tsx`, 3): per-team config is a valid DataGrid config with `membershipFilter` roles=owner/identityTypes=team (framework mechanism); needs-review has no `membershipFilter` (default queue); `ReconciliationGrid` forwards `membershipResolver` + `configId` to `<DataGrid />`.

## Test-infra change (7 seam files)
Adding the `CategoryRoutingGate` ctor param to `CommunicationEnrichmentService` touched 7 seam test construction sites. Added a shared `TestRoutingGate` helper (`Disabled()` / `From(options)`); 6 files insert `TestRoutingGate.Disabled()`, `EmailTriageSeamTests.CreateService` takes an optional gate for the routing tests. All 150 Communication seam tests green.

## §10 / hygiene
- Publish size **47.07 MB compressed incl PDBs** (≤60 ceiling; ≈ baseline, no meaningful delta — no packages/assemblies added). No vulnerable packages. ADR-013 facade untouched (routing uses `IGenericEntityService`, no AI type). ArchTests **4/24 pre-existing** (no new — `CategoryRoutingGate` is concrete).
- /conflict-check clean (contended DataGrid `membershipFilter` + Communication config; no open-PR overlap on these files).

## Remaining Pillar E
058 (r5 coordination contract — COORD-058-01 staged), 059 (deploy — gated; seeds the per-team `sprk_gridconfiguration` record + the `CategoryRouting` app setting + updates `NEEDS_REVIEW_CONFIG_ID`).
