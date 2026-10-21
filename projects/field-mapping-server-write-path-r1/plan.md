# Field Mapping → Server-Side Write Path (R1) — Implementation Plan

> **Created**: 2026-10-19 by `/project-pipeline` · **Source**: [spec.md](spec.md) (28 FRs, 8 NFRs) ·
> **Design**: [design.md](design.md) · **Evidence**: [notes/design-review-2026-10-19.md](notes/design-review-2026-10-19.md)

## 1. Executive Summary

**Purpose**: Make the BFF the single owner of create-time invariants I-3 (field mapping) and I-5 (search index). Apply
I-1, I-2, I-6, I-11, I-12 and I-16 identically on every create path. Implements ADR-002 WP-1…WP-5 and WP-7.

**Scope**: W1–W6 (see README). Forward-only. No schema change beyond reconciling the profile mode-column name.

**Estimated effort**: 35–50 working days across 43 tasks. The critical path runs through W2 (unified service) and the
sequential, one-at-a-time wizard migration in W3.

## 2. Architecture Context

**Binding constraints** (spec "Technical Constraints"):

| ADR | Constraint |
|---|---|
| ADR-002 | No plugins. One server-side owner per invariant (WP-1); client previews only (WP-2); invariant tables written via BFF (WP-3); inline (WP-4); outside-product async fix-up + reconciliation (WP-5, WP-7); fail closed (WP-6) |
| ADR-001 / ADR-008 | Minimal API; route-level authorization in endpoint filters. **Path A exception**: payload-dependent checks live in `CallerWriteAuthorization` (continuing the `OwnedChildWrite` precedent) |
| ADR-010 | One scoped `RecordCreationService` replaces the `OfficeModule.cs:73` registration; the engine stays `internal static` |
| ADR-019 | ProblemDetails with a stable `errorCode` + correlation id; Office codes preserved |
| ADR-024 | Regarding pair; pair-targeting mapping rules are refused |
| ADR-028 | App-only create under the managed identity; caller identity via WhoAmI on the caller's token |
| ADR-036 / ADR-052 | Repair pass = BFF `IScheduledJob` via `AddScheduledJob`, leased, heartbeated, A1-3 claim/marker |
| ADR-038 | Tests at KEEP paths; no `Mock<HttpMessageHandler>`, no DI-registration tests |
| ADR-012 / ADR-021 | Shared client components lose `applyFieldMappings`; wizard UI unchanged (Fluent v9 semantic tokens) |

**Technology**: .NET 10 Minimal API BFF; `Spaarke.Dataverse` Web API client; React 18 / Fluent v9 wizards in
`@spaarke/ui-components` hosted by Vite code pages; Dataverse ribbon XML + JS web resources; PowerShell `-Verify`
scripts.

**Integration points**:
- `POST /api/v1/child-records/{table}` (the browser create route; extended for Matter and Project);
- Office quick-create (`/api/office/quick-create/*`), `/api/office/todo`, Office document save;
- chat tools (`dataverse.create_record`, `email.draft`), budget inquiry, decision route cores;
- `POST /api/v1/events` and `POST /api/ai/analysis/create`; invoice extraction; system creators; `/push`.

### Discovered Resources

| Kind | Resource | Use |
|---|---|---|
| ADR (concise) | `.claude/adr/ADR-002-thin-plugins.md`, `ADR-001-minimal-api.md`, `ADR-008-endpoint-filters.md`, `ADR-010-di-minimalism.md`, `ADR-019-problemdetails.md`, `ADR-024-polymorphic-resolver-pattern.md`, `ADR-028-spaarke-auth-architecture.md`, `ADR-036-background-job-infrastructure.md`, `ADR-052-workload-placement.md`, `ADR-012-shared-components.md`, `ADR-021-fluent-design-system.md` | Per-task constraints |
| ADR (full) | `docs/adr/ADR-038-testing-strategy.md` | Test placement |
| Constraints | `.claude/constraints/bff-extensions.md`, `azure-deployment.md` (publish-size rule), `testing.md`, `data.md`, `jobs.md`, `webresource.md`, `auth.md` | BFF hygiene, size, tests, jobs, ribbon JS |
| Patterns | `.claude/patterns/dataverse/entity-operations.md`, `polymorphic-resolver.md`, `web-api-client.md`, `relationship-navigation.md` | Dataverse I/O |
| Architecture | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` | Invariant registry; mapping framework |
| Guides | `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md` | Maker guide (rewritten in W6) |
| Skills | `bff-deploy`, `code-page-deploy`, `dataverse-deploy`, `ribbon-edit`, `conflict-check`, `code-review`, `adr-check` | Deploy + gates |
| Canonical code | `Services/Dataverse/OwnedChildWrite.cs` (create + caller checks), `Services/Dataverse/RecordOwnershipResolver.cs`, `Api/Filters/QuickCreateSourceAccessFilter.cs`, `Services/Office/CreateTimeFieldMapping.cs`, `Services/Office/RecordCreationService.cs` (`ApplyFieldMappingsAsync`), `Services/Dataverse/CoreAncestorStampReconciliationJob.cs`, `Services/Access/SecureChildReconciliationJob.cs`, `src/client/webresources/js/sprk_secure_child_ribbon.js`, `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`, `scripts/Backfill-RecordOwnership.ps1` (the `-Verify` pattern) | Reference implementations |

**Schema** (validated in design review 2026-10-19): `sprk_fieldmappingprofile`, `sprk_fieldmappingrule` (incl.
`sprk_syncmode`, `sprk_executionorder`) exist. The only schema-adjacent work is the profile mode-column name drift
(FR-05a). No new tables or columns.

## 3. Implementation Approach

**Phasing**: W1 ∥ W2 → W3 (Matter first) ∥ W4 → W5 → W6. W6 doc and guard items land with the workstream they
describe; the 06x tasks consolidate them.

**Critical path**: 010 → 011 → 013 → 014 → 015 → 017 → 019 → 020 → 021 (Matter) → 022 … 029 (wizards, sequential) →
035 → 061 → 090.

**Gates**:
- **010** (W2 coordination gate): UAC-r2 sign-off on the `OwnedChildWrite` refactor and census changes; ontology-r1
  awareness for the chat, decision and Signal/Decision writer re-pointing. `/conflict-check` before each W2 task
  (NFR-08).
- **019** (W2 live gate): deployed to dev, adversarial review (security), publish size measured.
- Each wizard task is verified live on dev before the next starts (FR-16).
- **050** (W5 open questions) resolves the repair marker and the live quick-create flags before 051 and 052.

## 4. WBS

### Phase 1 — W1 Canonical engine and configuration (001–006)

- **Objectives**: one engine with written semantics; fill-only; source resolution; configuration cleanup.
- **Deliverables**:
  - `Services/Dataverse/FieldMapping/` (engine, fill-only applier, source resolver);
  - unit tests;
  - seeder fix;
  - `sprk_syncmode` migration script;
  - `EntityMaps.xml` cleanup.
- **Inputs**: master code.
- **Outputs**: a mapping pipeline component that W2 composes.
- **Dependencies**: none; can start immediately (parallel with 010).

### Phase 2 — W2 Unified `RecordCreationService` (010–019)

- **Objectives**: one create service with Create and Repair modes.
- **Deliverables**:
  - `Services/RecordCreation/`;
  - `Services/Dataverse/CallerWriteAuthorization.cs`;
  - the per-table rules registry;
  - the typed refusal map;
  - re-pointed callers;
  - the Office adapter;
  - the old Office service deleted.
- **Inputs**: W1 engine (for 014); UAC-r2 sign-off (010).
- **Outputs**: a deployed service on dev with every existing core caller on it.
- **Dependencies**: UAC-r2 (hot file `OwnedChildWrite.cs`; PRs #2110/#2078 open), ontology-r1.

### Phase 3 — W3 Product creators onto the service (020–036)

- **Objectives**: every product creator creates through the service; the client engine is deleted.
- **Deliverables**:
  - route extension (Matter/Project, `mappingSource`);
  - 10 wizard migrations, one per task (029 covers two document wizards);
  - Analysis email mapping at send;
  - Office paths;
  - typed routes;
  - invoice extraction;
  - system creators;
  - client engine deletion.
- **Inputs**: Phase 2 deployed.
- **Outputs**: mapped, team-owned records from every surface.
- **Dependencies**: Phase 2; PR #1501 (`CreateAnalysisWizardWidget`) and #2104 (`CreateProjectWizard`) overlap.

### Phase 4 — W4 `/push` (040)

- **Objectives**: push on the canonical Copy logic, selected by `sprk_syncmode`; fixed error text.
- **Inputs**: 001, 005.
- **Outputs**: parity with create-time Copy.
- **Dependencies**: can run in parallel with Phase 3.

### Phase 5 — W5 Records created outside the product (050–053)

- **Objectives**: redirect native UI; forward-only repair pass.
- **Deliverables**:
  - resolved open questions (050);
  - ribbon/quick-create/Assign changes + `-Verify` script (051);
  - repair `IScheduledJob` + on-demand trigger (052);
  - live check (053).
- **Inputs**: Repair mode (016); wizards migrated (FR-24 redirects to them).
- **Dependencies**: Phase 3 for the redirect targets; UAC-r2 for re-owning.

### Phase 6 — W6 Standard, guards, docs, retirement (060–063)

- **Objectives**: ownership matrix; census and client guards; docs; dead code; issue hygiene.
- **Inputs**: Phases 2–5.
- **Outputs**: published standard; guards with negative controls; docs merged.
- **Dependencies**: 062 edits root `CLAUDE.md` + `.claude/CHANGELOG.md` (main session only; sub-agents cannot write
  `.claude/`).

### Wrap-up (090)

Test diet, lessons learned, README status, worktree sync.

## 5. Dependencies

- **External**:
  - dev Dataverse `spaarkedev1`;
  - platform autonumber on Matter (task 076 `-Verify`);
  - deploy skills (`bff-deploy`, `code-page-deploy`, `dataverse-deploy`, `ribbon-edit`).
- **Internal**:
  - UAC-r2 (`OwnedChildWrite.cs`, census and route-authorization guards, roles);
  - ontology-r1 (chat and decision cores, Signal/Decision writers);
  - external-access-r3 (exception only);
  - word-add-in-r1 (merged; Office paths).

## 6. Testing Strategy

ADR-038 KEEP paths throughout:

- **Unit** (`tests/unit/Sprk.Bff.Api.Tests/`): engine semantics (each mapping type × target type), fill-only, source
  resolution, the rules registry, the refusal map.
- **Seam** (`tests/integration/seam/**`): the service step order and a single create call; caller vs system parity.
- **Integration / data-mutation** (`tests/integration/data-mutation/RecordOwnership/**`): owner per surface; mapped
  lookup authorization (#1614/#1617); repair idempotency; forward-only.
- **Contract**: Office quick-create codes unchanged; child-records route; push response shape; #1437.
- **Arch** (`tests/Spaarke.ArchTests/`): census (server and client creates), no client mapping, registry completeness.
  Every guard has a negative control.
- **Client** (jest): per-wizard payload builders send only user-entered values.
- **Live** (dev): per-wizard check; Matter→Event mapping; 5-row import repair; native-surface `-Verify`.

## 7. Acceptance Criteria

Same as the README Graduation Criteria (spec Success Criteria 1–10), each with its verification method.

## 8. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| UAC-r2 concurrently edits `OwnedChildWrite.cs` and the census guards (PR #2110 open) | High | High | 010 gate; `/conflict-check` per W2 task; extract `CallerWriteAuthorization` as a pure move first (011) |
| Mapped lookups become an AppendTo bypass or an existence oracle | Medium | High | FR-04 Read-as-caller probe; FR-08 merged-payload checks; adversarial review at 019 and 052 |
| Owner change surprises (Office Project → Matter's BU) | Medium | Medium | Release note; cross-surface owner test |
| Wizard migration regresses a wizard | Medium | Medium | One wizard per task, live-verified; rollback = redeploy the previous bundle |
| Matter wizard latency grows (server profile + source reads) | Low | Medium | NFR-02 measure before/after; escalate at +500 ms p95; caching only if needed |
| The repair pass rewrites rows it shouldn't | Low | High | Forward-only (`createdon` ≥ go-live); `createdby` marker verified in 050; fill-only; idempotency marker |
| Publish size creep | Low | Low | No new packages; fresh-master delta per task |

## 9. Next Steps

1. Run task 001 (W1, startable) and task 010 (W2 coordination gate) in parallel.
2. W2 implementation (011+) starts only after 010 records UAC-r2 sign-off.
3. Execute every task via `task-execute` (root CLAUDE.md §4).
