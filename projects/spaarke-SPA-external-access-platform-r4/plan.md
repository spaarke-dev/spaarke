# Project Plan: Spaarke External Access Platform R4

> **Last Updated**: 2026-11-10
> **Status**: Ready for Tasks (execution waits for owner review of plan and tasks)
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Make the external SPA's record view a config-driven template where users act on what they can read, by each record's rights. Also close the R3 and ebilling backlog handed to R4.

**Scope**:
- P1: record template for six types, shared `RecordCommandBar` + icon map, per-record access, two BFF event routes, delete-409 ProblemDetails, Submit invoice action
- P2: invoice Review detail + held badge, invoice Files, inline preview, documents without file, Documents on the shared DataGrid, shared EmptyState
- P3: Information fields (allow-list, UAC review), grid persistence, Money sort/export, messages refresh + threads, matter-event task type/assignee
- P4: workforce-contact tabs, sign-in fixes, binder-fault 503, carry-over hygiene (#2275, #2499, #2578), Teams mobile
- Closing: standards proposals, wrap-up

**Timeline**: four staged phases, each ending in a dev deploy and E2E | **Estimated effort**: 90–120 hours (34 tasks)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001 / ADR-019**: Minimal API; ProblemDetails with a stable `errorCode` on every new or changed route.
- **ADR-007**: SPE only through `SpeFileStore`; preview bytes from the existing content route, no Graph, no signed URL.
- **ADR-008**: endpoint filters on the new event routes, copied from the project route.
- **ADR-010**: no new DI registration or interface.
- **ADR-012**: `RecordCommandBar` and DataGrid options in the shared library: Fluent v9, tokens, no Xrm/entity literals, exported types, tests.
- **ADR-021**: Fluent v9, `@fluentui/react-icons`, semantic tokens, WCAG 2.1 AA, dark/high-contrast.
- **ADR-028**: A1 CIAM plane is app-only, access enforced server-side via `sprk_externalrecordaccess`; an indeterminate result is a retryable 503, never a 403.
- **ADR-038**: KEEP test paths, banned mocks.
- **ADR-050**: SprkModal and its presets, Cancel on the left.
- **ADR-052**: no new background work; #2578 changes an existing job's logging/skip logic only.

**From Spec**:
- No action the UI offers may answer 403 (NFR-04).
- Review detail renders only when `statuscode` = 1; unknown values blank (FR-11).
- BFF deploys only through uac-r2, with the owner's go per exact SHA (NFR-06).
- No live Dataverse/Azure reads or changes without the owner first seeing the commands (FR-31).

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| One `RecordTemplateConfig` map drives the modal | Six types share one layout; adding a type = config + renderers | R3 stacked modal retired |
| Shared `RecordCommandBar` composes `HeaderToolbar` | ADR-012 "core UX pattern" (Path C); avoids per-type visibility drift | New shared component with tests |
| Hide, don't disable | Owner 2026-11-08 | A View-only caller sees only Refresh, Copy link, Download |
| Event routes copy `POST /projects/{id}/events` | Same filters, DTO, service; no new surface beyond the routes | UAC review before deploy request |
| Grid settings in browser `localStorage` | Owner scope; cross-device out of scope | Opt-in DataGrid props |
| Binder faults → 503 + 3 retries (1/2/4 s) | ADR-028 + owner 2026-11-09 | `me-client.ts` retry branch |

### Discovered Resources

**Applicable skills**:
- `.claude/skills/task-execute/`: every task runs through it
- `.claude/skills/fluent-v9-component/`: `RecordCommandBar`, template, sections, DataGrid options
- `.claude/skills/adr-check/` and `.claude/skills/code-review/`: required gates (shared-lib Path C sign-off)
- `.claude/skills/conflict-check/`: before every BFF PR (shared surface with uac-r2, ebilling-r1)
- `.claude/skills/ui-test/`: UI verification on frontend tasks
- `.claude/skills/bff-deploy/`: **not run by R4**; uac-r2 runs it (dev-bff-deploy-coordination)
- `.claude/skills/project-defer-issue-tracking/`: deferred work → notes + GitHub issue
- `.claude/skills/test-diet/`: wrap-up

**Knowledge and procedures**:
- `docs/procedures/dev-bff-deploy-coordination.md`: dev BFF deploy rules (uac-r2 orchestrates)
- `.claude/constraints/bff-extensions.md`: BFF hygiene, publish-size delta
- `.claude/constraints/auth.md` + `.claude/adr/ADR-028-spaarke-auth-architecture.md`: external plane
- `docs/standards/MODAL-DESIGN-SYSTEM.md`, `docs/standards/MODAL-DECISION-CRITERIA.md`, `.claude/patterns/ui/record-modal-selection.md`: modal rules
- `.claude/patterns/ui/fluent-v9-component-authoring.md`, `.claude/patterns/ui/fluent-v9-host-visual-fit.md`
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`: DataGrid options
- `docs/standards/TEST-ARCHITECTURE.md`, `docs/adr/ADR-038-testing-strategy.md`
- `projects/spaarke-SPA-external-access-platform-r3/CLAUDE.md`: inherited gotchas (delete routes, schema, test load)

**Reusable code** (canonical references):
- Event route: `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (`POST /projects/{id}/events`, ~`:456`)
- ProblemDetails 409 superset: `src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs:157-174`
- Per-record access in the SPA: `src/client/external-spa/src/components/DocumentLibrary.tsx` (`writeAccessPerRecord` ~`:701`, `canCreate` ~`:748-766`)
- Modal presets: `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/*`
- Toolbar slots + tooltip/aria rule: `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`
- Submit invoice gating: `src/client/external-spa/src/widgets/InvoicesWidget.tsx:66`
- Sanitizer: `sanitizeEmailHtml` (shared); `TruncatedListNotice` (SPA)

**Schema check (pipeline Step 2.7)**: not run live. The project rule forbids live Dataverse reads until the owner has seen the commands. Task 030 runs it under owner approval. Known from R3: `sprk_servicerequest` has no `sprk_createdbyperson` and no request-type column, except the one task 020 of R3 added. `sprk_reviewdetail` is ebilling's, and is not on master yet.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Record template and actions (001–018)
└─ Template + RecordCommandBar + per-record access + event routes + delete-409 + Submit invoice
└─ Deploy: BFF via uac-r2, SPA to dev; first-E2E inherited checks

Phase 2: Invoices and documents (020–028)
└─ Review detail, held badge, invoice Files, preview, hasFile, DataGrid documents, EmptyState
└─ Deploy + E2E (FR-11/FR-13 only once ebilling's BFF changes are live)

Phase 3: Grids, information, messages (030–038)
└─ Live-metadata check → allow-list + Information; DataGrid persistence + Money; messages; matter events
└─ Deploy (BFF allow-list via uac-r2) + E2E

Phase 4: Access, sign-in, carry-overs, Teams (040–058)
└─ Workforce tabs, first sign-in, email texts, 503 retry, #2275/#2499/#2578, Teams mobile
└─ Deploy + E2E

Closing (080, 090): standards proposals, wrap-up
```

### Critical Path

- 001 → 003 → 005 → 006 → 018 (P1 deploy) gates every later phase's template work.
- 002 (BFF event routes) → 006 (integration tests for matter/WA events) → 018.
- 030 (owner-approved live-metadata check) → 031 (allow-list + Information).
- 046 (owner-approved acme checks + data fix) is independent of 047 (the logging code change); either can land first.
- Each phase deploy (018, 028, 038, 058) blocks the next phase's deploy, not its coding.

**High-risk items:**
- Shared BFF surface (`Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `ExternalAccessModule.cs`) with uac-r2 and ebilling-r1 → `/conflict-check` before each BFF PR; `parallel-safe:false` across projects.
- FR-11/FR-13 depend on ebilling deploy timing → P2 ships without them if they slip.
- FR-23 depends on ontology D-199 → task 036 is gated, with an escalation trigger.

---

## 4. Phase Breakdown

### Phase 1: Record template and actions

**Objectives:** one config-driven template for six types; act-where-you-read; event routes for matters and WAs.

**Deliverables:**
- [ ] 001 `RecordCommandBar` + icon map in `@spaarke/ui-components` (FR-04, FR-05)
- [ ] 002 BFF `POST /matters|workassignments/{id}/events` + delete-409 ProblemDetails (FR-08, FR-09)
- [ ] 003 `RecordTemplateConfig` + template shell: layout, states, deep links (FR-01, FR-02)
- [ ] 005 Section renderers for six types; R3 stacked modal retired (FR-01)
- [ ] 004 Child dialogs inside the parent SprkModal (FR-03)
- [ ] 006 Per-record access, hide-don't-disable, "Your access" (FR-06, FR-07)
- [ ] 007 Submit invoice record action (FR-10)
- [ ] 018 P1 deploy (BFF via uac-r2, SPA dev) + E2E + inherited first-E2E checks (FR-33)

**Inputs**: spec FR-01–FR-10, design §3/§6 prototype. **Outputs**: new `RecordTemplate/` folder in the SPA, `RecordCommandBar/` in the shared lib, 2 BFF routes, tests.

### Phase 2: Invoices and documents

**Deliverables:**
- [ ] 020 Review detail at four render points + held-Accepted badge (FR-11, FR-12)
- [ ] 022 Inline preview (FR-14)
- [ ] 021 Invoice Files section; invoice upload/versions removed (FR-13)
- [ ] 023 Documents without a file + Documents on the shared DataGrid (FR-15, FR-16)
- [ ] 024 Shared EmptyState everywhere (FR-17)
- [ ] 028 P2 deploy + E2E

**Inputs**: ebilling 082c-2 deploy + files routes (gated FRs). **Outputs**: invoice/document sections, tests.

### Phase 3: Grids, information, messages

**Deliverables:**
- [ ] 030 Live-metadata check of §5 candidate columns (owner-approved read) (FR-18)
- [ ] 031 Allow-list columns (UAC review) + Information section (FR-18)
- [ ] 032 DataGrid persisted widths/sort + Reset columns (FR-19)
- [ ] 033 DataGrid Money sort + formatted export (FR-20)
- [ ] 034 Messages refresh + new-message indicator (FR-21)
- [ ] 035 Message threads (FR-22)
- [ ] 036 Matter events: task type + assignee (FR-23; gated on D-199)
- [ ] 038 P3 deploy + E2E

### Phase 4: Access, sign-in, carry-overs, Teams

**Deliverables:**
- [ ] 040 Workforce-contact tabs scoped by grants (FR-24)
- [ ] 041 Licensed user's first sign-in skips the profile form (FR-25)
- [ ] 042 Email-missing text + profile/registration email alignment (FR-26, FR-27)
- [ ] 043 Binder faults → 503 + SPA retry; ProjectPage 404 fold (FR-28)
- [ ] 044 `no-mock-identity` test warm-up (FR-29)
- [ ] 045 Contact store log labels + test double + gate fix (FR-30)
- [ ] 046 acme fault: read-only checks + owner-approved data fix (FR-31 a/b)
- [ ] 047 acme fault: Warning/cooldown for foreign-environment 403 (FR-31 c)
- [ ] 048 Teams mobile layout (FR-32)
- [ ] 058 P4 deploy + E2E

### Closing

- [ ] 080 Standards proposals (FR-34), main session, owner decision recorded
- [ ] 090 Wrap-up: issue checklist (SC-7), `/test-diet`, lessons learned, README status

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| ebilling-r1 082c-2 deploy (`sprk_reviewdetail` readable) | Pending | Med | FR-11 code ships hidden-blank; live check moves to a later deploy |
| ebilling-r1 `/invoices/{id}/files` routes | Not on master | Med | FR-13 deploys only after them, back to back |
| ebilling-r1 task-130 batch (stale grant → 403) | On `work/ebilling-r1` | Low | 043 folds the ProjectPage 404 branch once on master |
| unified-access-control-r2 | Active | Med | Runs every dev BFF deploy; UAC reviews FR-08/18/24/28/31 |
| spaarke-ontology-platform-r1 | Active | Med | Agreement on `AssignTaskRecipientGate.cs` (045); D-199 (036) |
| Dataset-grid framework owners | — | Low | Agree DataGrid option API (032/033) |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| SprkModal + presets | `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/` | Production |
| HeaderToolbar, DataGrid, EmptyState, StatusBadge, CommunicationTimeline | `src/client/shared/Spaarke.UI.Components/src/components/` | Production |
| External project endpoints | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/` | Production |

---

## 6. Testing Strategy

**Unit tests**: `RecordCommandBar` (visibility, overflow, keyboard, a11y); DataGrid options (persistence, Money sort, export); SPA template/section/access logic (vitest); resolver 503 mapping; log-label assertions.

**Integration tests** (`tests/integration/**`, KEEP paths): event routes must-fire/must-not-fire (201/403/404/401); delete-409 body; allow-list refusal regression; binder-fault 503 vs not-invited 403; acme own-environment fail-closed.

**E2E (dev, per phase)**: each record type in the template at both access levels; held-invoice Review detail; preview; grid persistence across reload; workforce tabs with and without grants; Teams mobile at 375px.

---

## 7. Acceptance Criteria

See spec §Success Criteria (SC-1–SC-9). Per phase:
- **P1:** six types open in the template; write vs View-only behaviour proven; no offered action answers 403.
- **P2:** Review detail must-not-fire test passes; preview ≤ 25 MB; invoice Files render (when ebilling is live).
- **P3:** unlisted column still refused; widths/sort survive reload; remote images stripped in threads.
- **P4:** 503 retry path proven; acme faults gone or at Warning; Teams mobile has no horizontal scroll.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Merge friction on the shared external-access BFF surface | Med | High | `/conflict-check` per BFF PR; small BFF tasks; deploy via uac-r2 |
| R2 | ebilling timing slips FR-11/FR-13 | Med | Med | Ship P2 without them; later deploy |
| R3 | D-199 makes #2445's source a Signal | Med | Med | Named external projection with uac-r2 review, or defer via `/project-defer-issue-tracking` |
| R4 | Template rewrite regresses R3 modal states | Low | High | Keep R3 state tests; port before retiring the old path |
| R5 | Running the full BFF suite under machine load gives false failures | High | Low | Re-run failures in isolation (R3 gotcha) |

---

## 9. Next Steps

1. **Owner reviews** this plan and `tasks/TASK-INDEX.md` (requested 2026-11-10: no execution before that review).
2. On approval, start Phase 1 wave P1-A (001, 002) via task-execute.

---

**Status**: Ready for Tasks
**Next Action**: Owner review of plan and tasks

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
