# Project Plan: Spaarke Multi-Container Multi-Index Routing

> **Last Updated**: 2026-06-07
> **Status**: Complete (with deferred Invoice wizard + 3 UAT pending in-browser)
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)
> **Wrap-up**: see `notes/lessons-learned.md` + `notes/handoffs/post-uat-fixes-and-indexer-finding.md`

---

## 1. Executive Summary

**Purpose**: Extend Spaarke create wizards, BFF resolver, SemanticSearchControl PCF, and SemanticSearch code page to support **record-scoped routing** of containers and Azure AI Search indexes, enabling per-BU defaults with per-record overrides for the new "Protected Matter" requirement.

**Scope**:
- 5 Spaarke create wizards + DocumentUploadWizard set `sprk_searchindexname` (Phase A) — and fix latent `sprk_containerid` gap in `CreateProjectWizard`
- BFF `IKnowledgeDeploymentService` resolver gains optional `indexName` with allow-list validation (Phase B)
- SemanticSearchControl PCF v1.1.74: new bound property + filter-parity navigateTo envelope (Phase D + D.1)
- SemanticSearch code page consumes all envelope params end-to-end (Phase E)
- One-time PowerShell backfill: parent-record + document + drift-audit (Phase F)
- Operator runbook + architecture-doc update (Phase G)
- Coordinated deploy A.5 → B → A → D → E → F (Phase H)

**Timeline**: 3 weeks | **Estimated Effort**: ~80–100 hours (8 phases, ~45 tasks)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001** — Minimal API pattern for BFF endpoint changes
- **ADR-006** — PCF over web-resources; Code Page as full-page Power Apps surface
- **ADR-008** — Endpoint-filter authorization (existing BFF auth filter unchanged)
- **ADR-010** — DI minimalism (BFF resolver registration)
- **ADR-012** — Shared component library `@spaarke/ui-components` (wizards live there)
- **ADR-013** — AI architecture / semantic search
- **ADR-019** — ProblemDetails on BFF errors (`400 INDEX_NOT_ALLOWED`)
- **ADR-021** — Fluent UI v9 + dark mode + `tokens.*` (no hex, no `var(--…)`, no rgb literals)
- **ADR-022** — React version boundaries (PCF: 16, code page: 18)
- **ADR-026** — Full-page Code Page standard (`sprk_semanticsearch`)
- **ADR-028** — Spaarke Auth v2 (`@spaarke/auth.authenticatedFetch`, `initAuth`, `useAuth`)
- **ADR-029** — BFF publish hygiene (framework-dependent, transitive CVE override, ≤60 MB compressed)

**From Spec — invariants** (INV-1 through INV-8 in design.md §3):
- Two inheritance mechanisms (OOB attributemaps + `sprk_fieldmappingprofile`) MUST stay untouched within their existing scopes
- Document container field is `sprk_graphdriveid` only — never populate `sprk_containerid` on `sprk_document`
- BU-change does NOT fan out to existing records — coexistence is the design (INV-3)
- Explicit overrides are sacred — backfill + wizards MUST never overwrite a non-null value (INV-5)
- BFF MUST validate `searchIndexName` against `appsettings.AiSearch.AllowedIndexes` (no silent default)
- Backfill MUST halt loud on unmapped containers (no silent fallback)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Backward-compatible BFF signature extension (`string? indexName = null`) | Existing callers continue to work; phased deploy without coordination headaches | Zero modifications to existing BFF test suite (NFR-02) |
| Backfill derives container from child documents (mode of `sprk_graphdriveid`) rather than from BU | BU has been changed; evidence is more reliable than current BU value | Per-record audit log + halt-on-unmapped behavior |
| Allow-list in static appsettings (not dynamic config entity) | Tighter blast radius; INFO-logged at startup; ops simpler | Adding a new index requires appsettings update + redeploy |
| PCF v1.1.74 strictly succeeds v1.1.73 (PR #363) — no parallel branches on PCF | Spec.md §Implementation Notes; avoids manifest merge hell | Sequencing requirement: PR #363 must land first |

### Discovered Resources

**Applicable Skills** (auto-loaded by task-execute via tag mapping):
- `.claude/skills/pcf-deploy/` — PCF build + 5-location version bump + solution import (Phase D)
- `.claude/skills/code-page-deploy/` — Vite/React code-page build + Dataverse inline deploy (Phase E)
- `.claude/skills/bff-deploy/` — BFF publish + Azure App Service deploy (Phase B)
- `.claude/skills/dataverse-mcp-usage/` — MCP queries for BU + record verification (Phase A.5, all UAT)
- `.claude/skills/task-execute/` — mandatory protocol for every task
- `.claude/skills/adr-aware/` — auto-loads ADR constraints into task context
- `.claude/skills/ui-test/` — browser-based UI tests for PCF + code-page tasks
- `.claude/skills/dataverse-create-schema/` — only if schema gaps are discovered (none expected; spec verified)

**Knowledge Files** (`.claude/`):
- `.claude/constraints/bff-extensions.md` — **binding** pre-merge checklist for any BFF addition (Placement Justification, asymmetric-registration rules, publish-size gate)
- `.claude/constraints/api.md` — endpoint definition conventions
- `.claude/constraints/pcf.md` — PCF coding constraints
- `.claude/constraints/auth.md` — Spaarke Auth v2 client contract
- `.claude/patterns/pcf/control-initialization.md` — PCF init lifecycle pointer
- `.claude/patterns/pcf/theme-management.md` — ADR-021 dark-mode token usage
- `.claude/patterns/api/endpoint-definition.md` — Minimal-API endpoint pattern
- `.claude/patterns/api/endpoint-filters.md` — endpoint-filter authorization pattern
- `.claude/patterns/auth/spaarke-sso-binding.md` — canonical Spaarke Auth v2 binding

**Codebase patterns to follow** (verbatim references):
- Wizard sets container on create payload — [`matterService.ts:216`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L216)
- Container resolution chain (parent → user-BU fallback) — [`AssociateToStep.tsx:147-163`](../../src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx#L147-L163)
- Document create payload — [`DocumentRecordService.ts:268-293`](../../src/client/shared/Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts#L268-L293)
- BFF index resolver — [`IKnowledgeDeploymentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs)
- BFF OData filter (no container clause exists today) — [`SearchFilterBuilder.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SearchFilterBuilder.cs)

**Saved lessons in scope**:
- `feedback_stale-shared-lib-dist-poisons-codepage-bundle` — mandatory clean rebuild of `@spaarke/auth` + `@spaarke/ui-components` `dist/` BEFORE PCF + code-page builds (NFR-10, NFR-11)
- `feedback_deploy-asks-follow-skill-no-openended-questions` — invoke matching deploy skill verbatim with file paths; no open-ended questions

**Docs to update / create**:
- `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` (new — FR-DOC-01)
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` (update — FR-DOC-02)

---

## 3. Implementation Approach

### Phase Structure

```
Phase A.5: Operator BU value setup           (Week 1)
└─ Pre-deploy MCP-driven population of `businessunit.sprk_searchindexname`
└─ Verification of two known BUs (Spaarke Demo, Spaarke)

Phase B:  BFF resolver extension              (Week 1)
└─ Interface signature + impl + thread-through (3 services) + DTOs + appsettings + endpoint
└─ Unit + integration tests; backward-compat verification

Phase A:  Wizards (5 parent + DocumentUploadWizard) (Week 1-2)
└─ Shared-lib changes first, then per-wizard payload updates
└─ INV-5 (explicit-override-preserved) unit tests

Phase D:  PCF SemanticSearchControl v1.1.74    (Week 2)
└─ Manifest: new bound `searchIndexName` property
└─ Service: send `searchIndexName` in BFF request body
└─ NavigationService: include `searchIndexName` in navigateTo envelope
└─ 5-location version bump per `/pcf-deploy`

Phase D.1: Filter-parity in navigateTo envelope (Week 2 — folded into D)
└─ Extend NavigationService to send ALL filter state (query, scope, entityId, threshold, mode, fileTypes, dateFrom, dateTo, tags, associatedOnly)

Phase E:  SemanticSearch code page             (Week 2)
└─ parseUrlParams extension + types.ts
└─ App.tsx: stop discarding initialScope/initialEntityId; seed filter state
└─ Hooks (useSemanticSearch + useRecordSearch) include `searchIndexName`

Phase F:  PowerShell backfill                   (Week 2-3)
└─ Backfill-MultiContainerMultiIndex-ParentRecords.ps1
└─ Backfill-MultiContainerMultiIndex-Documents.ps1
└─ Audit-MultiContainerMultiIndex-Drift.ps1
└─ All scripts: idempotent + resumable + paged + INV-5-safe

Phase G:  Operator runbook + arch doc update    (Week 3)
└─ docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md (new)
└─ docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md (update)

Phase H:  Coordinated deploy + UAT              (Week 3)
└─ Strict order: A.5 → B → A → D → E → F
└─ Smoke + Protected-Matter walkthrough + parity test
```

### Critical Path

**Blocking dependencies**:
- Phase A.5 BLOCKS Phase A deploy (wizards have nothing to inherit until BU values are set)
- Phase B BLOCKS Phase D deploy (PCF needs BFF to accept `searchIndexName` in request body)
- Phase D BLOCKS Phase D.1 deploy (D.1 is folded into D)
- Phases A, D, E, F can be developed in parallel branches, but MUST deploy in the H sequence

**High-risk items**:
- Stale shared-lib `dist/` poisoning the bundles — mitigated by `feedback_stale-shared-lib-dist-poisons-codepage-bundle` (mandatory clean rebuild documented in NFR-10/11 + every PCF/code-page deploy task).
- Unmapped SPE container in production backfill — mitigated by halt-loud behavior (FR-BF-01/02) + operator runbook section.
- PR #363 (v1.1.73) sequencing — mitigated by spec.md §Implementation Notes explicit acknowledgement; this project's PR succeeds #363.

---

## 4. Phase Breakdown

### Phase A.5: Operator BU Value Setup (Week 1)

**Objectives**:
1. Populate `businessunit.sprk_searchindexname` for known BUs per design §5.0.
2. Document the "how to add a new BU value" path for operators.

**Deliverables**:
- [ ] PowerShell or MCP-driven update of Spaarke Demo BU → `spaarke-knowledge-index-v2`
- [ ] PowerShell or MCP-driven update of Spaarke BU → `spaarke-file-index`
- [ ] MCP verification query confirms values BEFORE Phase A deploy
- [ ] Brief note in operator runbook (Phase G) for adding new BU values

**Critical Tasks**: Verification MCP query is the gate that unblocks Phase A.

**Inputs**: Maker-portal access to Dataverse `businessunit` table; design.md §5.0 value table.
**Outputs**: BU records with populated `sprk_searchindexname`.

---

### Phase B: BFF Resolver Extension (Week 1)

**Objectives**:
1. Extend `IKnowledgeDeploymentService.GetSearchClientAsync` with optional `indexName` + allow-list validation.
2. Thread `SearchIndexName` from request DTOs through `SemanticSearchService`, `RagService`, `RecordSearchService` to the resolver.
3. Add `AiSearch.AllowedIndexes` to appsettings.

**Deliverables**:
- [ ] `IKnowledgeDeploymentService.GetSearchClientAsync(string? indexName = null)` signature (FR-BFF-01)
- [ ] Allow-list validation → `400 INDEX_NOT_ALLOWED` ProblemDetails on miss (FR-BFF-02, NFR-08)
- [ ] Resolver returns `SearchClient` bound to validated index name (FR-BFF-03)
- [ ] Fall-through to existing 2-tier chain when `indexName` empty (FR-BFF-04)
- [ ] Request DTOs (`SemanticSearchRequest`, `RagSearchRequest`, `RecordSearchRequest`) have `string? SearchIndexName { get; init; }` (FR-BFF-05)
- [ ] `appsettings.AiSearch.AllowedIndexes` populated + startup INFO log (FR-BFF-06)
- [ ] Endpoint threads `SearchIndexName` from request DTO into resolver (FR-BFF-07)
- [ ] Unit tests for each FR; existing BFF test suite passes unmodified (NFR-02)

**Critical Tasks**: Interface signature change (FR-BFF-01) MUST land first — blocks all subsequent BFF work in this phase.

**Inputs**: `Sprk.Bff.Api/Services/Ai/` source; `appsettings.template.json`.
**Outputs**: Updated BFF compiled + deployed; existing tests pass; new tests pass.

**Placement Justification** (binding per `.claude/constraints/bff-extensions.md`):
This is a CRUD-resolver extension to existing AI search code. **In-BFF placement is correct** per the decision criteria — it's tightly coupled to the existing `IKnowledgeDeploymentService` tenant-routing chain (an internal AI orchestration concern) and doesn't introduce new cross-cutting concerns. No facade needed (CRUD code doesn't consume AI internals here; AI internals expose a richer resolver).

**Publish-size check** (NFR-01, mandatory per CLAUDE.md §10 bullet 4): After Phase B build, measure `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` compressed output. Baseline ~45.65 MB (post-Phase 5 Outcome A, 2026-05-26). Expected delta: minimal (~no NuGet additions). Hard ceiling: 60 MB.

---

### Phase A: Wizards — 5 Parent + DocumentUploadWizard (Week 1-2)

**Objectives**:
1. Each create wizard sets `sprk_searchindexname` (and `sprk_containerid` where latent) from the user's BU on create payload.
2. `DocumentUploadWizard` resolves `sprk_searchindexname` via the parent → BU → empty chain.
3. Honor INV-5 (explicit overrides preserved).

**Deliverables**:
- [ ] CreateMatterWizard sets `sprk_searchindexname` (FR-WIZ-01)
- [ ] CreateProjectWizard sets BOTH `sprk_containerid` AND `sprk_searchindexname` — fixes latent G2 (FR-WIZ-02)
- [ ] CreateInvoiceWizard sets both (verify-first-then-fix gap if needed) (FR-WIZ-03)
- [ ] CreateWorkAssignmentWizard sets both (verify-first-then-fix gap if needed) (FR-WIZ-04)
- [ ] CreateEventWizard sets both (verify-first-then-fix gap if needed) (FR-WIZ-05)
- [ ] `DocumentUploadWizard.AssociateToStep`: new `resolveSearchIndexNameForRecord(xrm, entityLogicalName, recordId)` (FR-WIZ-06)
- [ ] `DocumentRecordService.buildRecordPayload` includes `sprk_searchindexname` (FR-WIZ-07)
- [ ] INV-5 unit tests for every wizard (FR-WIZ-08)
- [ ] `EntityCreationService.ts` accepts the new field if mapped through shared service layer

**Critical Tasks**: Shared-lib changes in `Spaarke.UI.Components/src/services/EntityCreationService.ts` and per-wizard service files MUST land before per-wizard payload updates — they are the contract.

**Inputs**: Phase A.5 BU values; `matterService.ts:216` canonical reference; `AssociateToStep.tsx:147-163` pattern.
**Outputs**: 6 updated code-page projects (5 parent wizards + DocumentUploadWizard); INV-5 unit tests green.

---

### Phase D: PCF SemanticSearchControl v1.1.74 (Week 2)

**Objectives**:
1. Add bound `searchIndexName` property to PCF manifest (FR-PCF-01).
2. Send `searchIndexName` in BFF request body when non-empty (FR-PCF-02).
3. Include `searchIndexName` in `navigateTo` envelope (FR-PCF-03).
4. 5-location version bump to v1.1.74 (FR-PCF-04 + NFR-10).

**Deliverables**:
- [ ] `ControlManifest.Input.xml` new bound property + version updates
- [ ] `SemanticSearchControl.tsx` footer string `v1.1.74 • Built {DATE}`
- [ ] `SemanticSearchApiService.search()` includes `searchIndexName`
- [ ] `NavigationService.openSemanticSearchPage()` includes `searchIndexName`
- [ ] `Solution/solution.xml` `<Version>1.1.74</Version>`
- [ ] `Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` version
- [ ] `Solution/pack.ps1` `$version = "1.1.74"`
- [ ] `npm run build:prod` bundle ≤ 1 MB (NFR-03 — current v1.1.73 is ~754 KB)
- [ ] UI tests: control renders, sends correct payload, dark-mode compliant (ADR-021)

**Critical Tasks**: Clean-rebuild `@spaarke/auth` + `@spaarke/ui-components` `dist/` BEFORE PCF build (NFR-10 + saved lesson).

**Inputs**: Phase B BFF accepts `searchIndexName`; v1.1.73 baseline (PR #363).
**Outputs**: PCF solution ZIP v1.1.74; imports cleanly; UI tests pass.

---

### Phase D.1: Filter-Parity in `navigateTo` Envelope (Week 2 — folded into D)

**Objectives**:
1. Extend `NavigationService.openSemanticSearchPage()` to send ALL PCF filter state (FR-PARITY-01).
2. Acceptance: code-page result set matches PCF result set for the same time-point (FR-PARITY-02 — UAT walkthrough).

**Deliverables**:
- [ ] `query`, `scope`, `entityId`, `threshold`, `searchMode`, `fileTypes`, `dateFrom`, `dateTo`, `tags`, `associatedOnly`, `theme`, `searchIndexName` all sent in envelope (empty/default values omitted to keep URL short)

**Critical Tasks**: Decoded-envelope assertion test.

**Inputs**: PCF current filter state object (already exists in v1.1.73).
**Outputs**: NavigationService extension complete; envelope decoder test passes.

---

### Phase E: SemanticSearch Code Page (Week 2)

**Objectives**:
1. `parseUrlParams` reads all envelope params (FR-CP-01).
2. `App.tsx` stops discarding `initialScope`/`initialEntityId`; wires hooks; seeds filter state from URL (FR-CP-02 + FR-CP-03).
3. Hooks include `searchIndexName` in BFF request bodies (FR-CP-04).

**Deliverables**:
- [ ] `parseUrlParams.ts` extended with all envelope keys + unit tests (present/absent/malformed)
- [ ] `App.tsx`: void-discards removed; hooks receive scope+entity at construction time
- [ ] `App.tsx`: filter state (fileTypes, dateRange, threshold, searchMode, associatedOnly) AND `selectedTags` seeded from URL BEFORE auto-search effect fires
- [ ] `useSemanticSearch.ts` includes `searchIndexName` in request body
- [ ] `useRecordSearch.ts` includes `searchIndexName` in request body
- [ ] `types/index.ts` `AppUrlParams` extended

**Critical Tasks**: Clean-rebuild shared libs + Vite cache clear before code-page build (NFR-11).

**Inputs**: Phase D PCF sends the envelope; Phase B BFF accepts `searchIndexName`.
**Outputs**: Code page built + inline-deployed; UAT walkthrough confirms identical PCF-vs-code-page result set.

---

### Phase F: PowerShell Backfill (Week 2-3)

**Objectives**:
1. Backfill empty parent-record `sprk_containerid` + `sprk_searchindexname` from child-document evidence.
2. Backfill empty `sprk_document.sprk_searchindexname` from `sprk_graphdriveid` map.
3. Generate drift audit (informational, no writes).

**Deliverables**:
- [ ] `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1` (FR-BF-01)
- [ ] `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-Documents.ps1` (FR-BF-02)
- [ ] `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1` (FR-BF-03)
- [ ] All scripts: idempotent + resumable + paged + INV-5-safe + halt-loud on unmapped (FR-BF-04)
- [ ] Test-environment dry-run audit log shows expected fills + no overwrites + halt behavior
- [ ] §5.1 hardcoded container→index map embedded in each script with operator-extend-here marker

**Critical Tasks**: Must run AFTER all client-side phases deploy (so new records have correct values first).

**Inputs**: §5.1 container → index map (design.md); MCP for record queries; existing-data evidence (each Document's `sprk_graphdriveid`).
**Outputs**: Three PowerShell scripts; per-run audit logs; drift-audit CSV/Markdown report.

---

### Phase G: Operator Runbook + Architecture Update (Week 3)

**Objectives**:
1. Comprehensive operator runbook for ongoing operations.
2. Architecture doc updated to mention per-BU routing.

**Deliverables**:
- [ ] `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` covers all 7 bullets from design §6 (FR-DOC-01)
- [ ] `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` mentions per-BU container/index routing (FR-DOC-02)

**Critical Tasks**: Cross-link to design.md invariants (INV-1 through INV-8).

**Inputs**: design.md §6 + spec.md.
**Outputs**: Runbook + architecture-doc update committed.

---

### Phase H: Coordinated Deploy + UAT (Week 3)

**Objectives**:
1. Coordinated deploy in strict dependency order.
2. Smoke + parity + Protected-Matter walkthrough on test environment.

**Deliverables**:
- [ ] Deploy order: A.5 → B → A → D → E → F (per spec §Dependencies + design §11)
- [ ] BFF smoke test: `400 INDEX_NOT_ALLOWED` on bad index; valid request routes correctly
- [ ] Wizard smoke test: create one of each (Matter, Project, Invoice, WorkAssignment, Event) — MCP-verify both fields populated
- [ ] Document upload smoke test: upload under a Matter; MCP-verify `sprk_searchindexname` set from parent
- [ ] PCF smoke test: Protected Matter routes to `spaarke-file-index`; verified via BFF log
- [ ] Filter-parity UAT walkthrough: PCF vs code-page side-by-side identical result set
- [ ] BU-change coexistence proof: change BU value; existing records keep theirs; new records use new value

**Critical Tasks**: Each phase smoke must pass before next phase deploys.

**Inputs**: All prior phases shipped.
**Outputs**: UAT sign-off; lessons learned captured by wrap-up task.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Search index `spaarke-knowledge-index-v2` | GA | Low | Already provisioned per spec |
| Azure AI Search index `spaarke-file-index` | GA | Low | Already provisioned per spec |
| SPE container `b!yLRd…` | GA | Low | Already provisioned |
| SPE container `b!vzGD…` | GA | Low | Already provisioned |
| Dataverse `businessunit.sprk_searchindexname` field | GA | Low | MCP-verified ✓ |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `Spaarke.UI.Components` (`@spaarke/ui-components`) | `src/client/shared/Spaarke.UI.Components/` | Production |
| `@spaarke/auth` (Spaarke Auth v2) | `src/client/shared/spaarke-auth/` | Production (ADR-028) |
| `Sprk.Bff.Api` | `src/server/api/Sprk.Bff.Api/` | Production |
| SemanticSearchControl PCF (v1.1.73) | `src/client/pcf/SemanticSearchControl/` | Production (PR #363) |
| SemanticSearch code page | `src/client/code-pages/SemanticSearch/` | Production |
| DocumentUploadWizard | `src/solutions/DocumentUploadWizard/` | Production |
| `sprk_aiknowledgedeployment` Dataverse entity | Dataverse | Production (tenant fallback) |

---

## 6. Testing Strategy

**Unit Tests** (line-level coverage of new code):
- Wizards: INV-5 (explicit override preserved) — one test per wizard
- DocumentUploadWizard: `resolveSearchIndexNameForRecord` — 3 chain steps tested
- BFF: allow-list validation (valid / invalid / null / empty); ProblemDetails shape; resolver fall-through
- BFF: request-DTO forward-compat (request without `SearchIndexName` parses)
- PCF: `SemanticSearchApiService.search()` body shape (with/without `searchIndexName`)
- PCF: `NavigationService.openSemanticSearchPage()` envelope shape (every parity field)
- Code page: `parseUrlParams` per-param (present / absent / malformed)
- Backfill: idempotency (re-run produces same state); halt-on-unmapped behavior; INV-5 (no overwrite)

**Integration Tests**:
- BFF end-to-end: client sends `searchIndexName` → server log shows resolved Azure Search URL
- Wizard end-to-end via existing test patterns

**E2E / UI Tests** (via `ui-test` skill — `<ui-tests>` in PCF + code-page tasks):
- PCF renders without console errors (ADR-021 dark-mode check)
- PCF "Open in Semantic Search" launches modal with seeded filter state
- Code-page result set identical to PCF (filter parity)

**Acceptance / UAT** (Phase H):
- 6 top-level criteria from README "Graduation Criteria"
- BU-change coexistence proof
- Backfill audit-log review

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase A.5**:
- [ ] MCP query confirms `businessunit.sprk_searchindexname` set on Spaarke Demo + Spaarke BUs

**Phase B**:
- [ ] All FR-BFF-01..07 acceptances pass
- [ ] Existing BFF test suite passes unmodified (NFR-02)
- [ ] `dotnet publish` compressed output ≤ 60 MB (NFR-01 baseline 45.65 MB)

**Phase A**:
- [ ] All FR-WIZ-01..08 acceptances pass
- [ ] Each wizard creates a record with both `sprk_containerid` AND `sprk_searchindexname` populated (MCP-verified)

**Phase D + D.1**:
- [ ] FR-PCF-01..04 + FR-PARITY-01..02 acceptances pass
- [ ] PCF bundle ≤ 1 MB (NFR-03)
- [ ] PCF passes ADR-021 dark-mode UI test
- [ ] Footer shows `v1.1.74`

**Phase E**:
- [ ] FR-CP-01..04 acceptances pass
- [ ] Code-page result set matches PCF result set in UAT walkthrough

**Phase F**:
- [ ] FR-BF-01..04 acceptances pass
- [ ] Audit-log review shows no INV-5 violations

**Phase G**:
- [ ] FR-DOC-01..02 acceptances pass

**Phase H**:
- [ ] All 6 top-level acceptance items in README pass

### Business Acceptance

- [ ] Protected Matter walkthrough end-to-end on test environment
- [ ] Operator sign-off on runbook + architecture-doc update

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Stale shared-lib `dist/` poisons PCF/code-page bundles | Medium | High | Saved lesson `feedback_stale-shared-lib-dist-poisons-codepage-bundle`; mandatory clean rebuild in every PCF/code-page deploy task |
| R2 | Unmapped SPE container appears in backfill | Low | Medium | Halt-loud behavior in backfill (FR-BF-01/02); operator runbook section for §5.1 map extension |
| R3 | BFF allow-list misconfigured per environment | Low | High | Startup INFO log (FR-BFF-06); operator runbook covers per-env config |
| R4 | Wizards deployed before BFF accepts new field → prod 400s | Low | Medium | Strict deploy order H.A.5 → B → A → D → E → F |
| R5 | Filter-parity drift between PCF and code page in UAT | Medium | Medium | FR-PARITY-01/02 enumerate every param; UAT walkthrough is an explicit acceptance gate |
| R6 | Backfill killed mid-run, state diverges | Low | Low | Idempotent + resumable + paged (FR-BF-04); checkpoint per 100 records |
| R7 | PR #363 (v1.1.73) merge-conflict with this project's PCF changes | Medium | Medium | Explicit sequencing: this PR succeeds #363 (spec.md §Implementation Notes); rebase post-#363-merge |
| R8 | BFF publish size exceeds 60 MB ceiling (NFR-01) | Low | High | No new NuGet additions expected; check after Phase B build per CLAUDE.md §10 bullet 4 |

---

## 9. Next Steps

1. **Review this plan.md** for completeness against spec.md (already aligned — generated by `/project-pipeline`)
2. **Verify TASK-INDEX.md** generated by Step 3 of the pipeline (~45 tasks across 8 phases)
3. **Begin Phase A.5** by saying "work on task 001" — task-execute will pick up the first task

---

**Status**: Ready for Tasks
**Next Action**: Pipeline Step 3 — task decomposition (TASK-INDEX + ~45 POML files)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. The single source of truth for invariants is design.md; this plan derives concrete WBS from spec.md.*
