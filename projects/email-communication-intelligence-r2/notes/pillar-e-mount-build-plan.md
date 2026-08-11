# Pillar E — Reconciliation Surface: Mount & Deploy Plan

> **Created**: 2026-08-10 · **Owner-requested** structured approach for the remaining Pillar E work.
> **Sibling docs**: `pillar-e-reconciliation-ux-requirements.md` (UX source of truth) · task 059 (deploy, to be superseded/completed here).
> **Type**: Follow-on mini-plan to `email-communication-intelligence-r2` (components exist; this is assembly + wiring + deploy).

---

## 1. Problem statement

The Pillar E reconciliation **components are built, tested, and exported** (`@spaarke/communication-components`, tasks 050–058), and the **BFF backend is deployed** (2026-08-10 — `dismiss`/`create-task`/`apply`/routing all live). But **no host mounts the components** — there is no reconciliation code page and no SpaarkeAi widget. Task 050's "live dual-mount (code page + SpaarkeAi widget)" was deferred and never built; r5 (which was to mount them) closed without doing so.

The prototype (`…/email-communication-intelligence-r2-uat`, live at `localhost:5176`) validated the UX but is a self-contained mock — it is not production code.

**Goal**: compose the production components into two hosts (code page + SpaarkeAi widget), honor the UX requirements (incl. the 2026-08-10 owner items A1–A6), seed config, and deploy both.

---

## 2. Scope & non-goals

**In scope**
- A production **composition component** (`ReconciliationWorkspace`) that wires `ReconciliationGrid` → `ReconciliationBrowseShell` → reconcile tabs, honoring UX reqs A1–A6 + B1–B13 + the behavioral invariants.
- **Two hosts**: a new code page (`sprk_communicationreconciliation`) + a SpaarkeAi widget.
- **Config seed** (needs-review + per-team `sprk_gridconfiguration`) + `NEEDS_REVIEW_CONFIG_ID` wiring.
- **Deploy** both hosts (`code-page-deploy`) + `Deploy-AllDataGridConsumers`.
- **Prototype refinement** to reflect A4/A5/A6 for accurate owner re-review (optional but recommended).

**Non-goals**
- No new reconciliation *feature* logic — components are done.
- No BFF changes (backend already deployed).
- No new entity/schema (ADR-045; gridconfig records are data, not schema).

---

## 3. Reuse ledger (§11 — every new surface justified)

| New surface | Why not reuse / extend | Reuse it leans on |
|---|---|---|
| `ReconciliationWorkspace` composition component | The grid, shell, and tabs exist but nothing composes them; the prototype's `App.tsx` orchestration (openBrowse, tab state, save/undo, 50/50 layout) is the missing glue. Cost-of-doing-nothing: no page can mount the surface without re-writing this orchestration per host. | `ReconciliationGrid`, `ReconciliationBrowseShell`, `FieldUpdateReconcileTab`, `TaskReconcileTab`, `SprkModal`/`BrowseModal`, `PanelSplitter`, `ModalScrollArea`, `EmailConnectionsReview` |
| Code page `CommunicationReconciliation` | No existing code page hosts the reconciliation grid (`EmailPage` hosts `EmailWorkspace`, a different surface). | Mirrors `EmailPage` structure (Vite + `initAuth` + `IDataverseClient`) |
| SpaarkeAi reconciliation widget | SpaarkeAi has no reconciliation widget (only a notifications hook). | SpaarkeAi widget registry pattern; same `ReconciliationWorkspace` |

---

## 4. Phased approach

### Phase 0 — Foundation (DONE / in this session)
- ✅ BFF backend deployed + endpoints verified live.
- ✅ UX requirements doc authored (`pillar-e-reconciliation-ux-requirements.md`).
- ✅ Prototype remounted for review (`localhost:5176`).

### Phase 1 — Prototype refinement (optional, recommended) → **task 060**
Apply UX items **A4 (larger modal), A5 (thin scrollbar), A6 (50/50 drag-resize)** to the prototype so the owner re-reviews the intended final layout before production build. Small, isolated to the prototype repo. Gate: owner sign-off on the refined prototype.

### Phase 2 — Production composition component → **task 061** (FULL rigor)
Build `ReconciliationWorkspace` in `@spaarke/communication-components`:
- `ReconciliationGrid` (configId, `IDataverseClient`, `membershipResolver`) with `onRecordOpen` → open `BrowseModal` (`size="xl"`/`full`, A4).
- Inside the modal: `PanelSplitter` (A6, default 50/50) — left `ReconciliationBrowseShell` reader (B2/B3, `CitationResolver`), right `TabList` → `FieldUpdateReconcileTab` / `TaskReconcileTab`.
- `ModalScrollArea` thin scroll (A5); footer Save&confirm / Undo (B11/B12); NFR-10 gate + re-scope.
- Unit tests (RTL) for composition + gate + citation nav. Exported from the lib barrel.

### Phase 3 — Dual host → **task 062** (FULL rigor; hot-path SpaarkeAi)
- **Code page**: `src/solutions/CommunicationReconciliation/` (Vite, mirrors `EmailPage`) → mounts `ReconciliationWorkspace` with real `IDataverseClient` (ADR-012) + `initAuth` (ADR-028) + `authenticatedFetch` to the deployed BFF. Web resource `sprk_communicationreconciliation`.
- **SpaarkeAi widget**: register a reconciliation widget in the SpaarkeAi widget registry mounting the same `ReconciliationWorkspace`.
- `/conflict-check` before the SpaarkeAi PR (shared surface).

### Phase 4 — Config seed + deploy → **task 059** (completes the existing deploy task)
- Seed needs-review + per-team `sprk_gridconfiguration` into spaarkedev1 (Web API create); capture the needs-review record id → set `NEEDS_REVIEW_CONFIG_ID`.
- `code-page-deploy` the code page + rebuild/redeploy SpaarkeAi; run `Deploy-AllDataGridConsumers`.
- Verify: grid renders + row opens browse shell on BOTH surfaces; publish/CVE hygiene where BFF is untouched (n/a here).

---

## 5. Task breakdown

| Task | Title | Rigor | Deps | Hot-path | parallel-safe |
|---|---|---|---|---|---|
| 060 | Prototype refinement (A4/A5/A6) for owner re-review | STANDARD | — | prototype repo | true |
| 061 | `ReconciliationWorkspace` composition component (UX A1–A6, B1–B13, NFR-10/11) | FULL | 050–058 ✅ | Communication.Components | false (shared lib) |
| 062 | Dual host — code page `sprk_communicationreconciliation` + SpaarkeAi widget | FULL | 061 | **SpaarkeAi**, code-page | false |
| 059 | Seed gridconfig + `NEEDS_REVIEW_CONFIG_ID` + dual deploy + Deploy-AllDataGridConsumers | STANDARD | 061,062 | SpaarkeAi, Dataverse | false |

Sequence: **060 (review gate) → 061 → 062 → 059**. 061 can start in parallel with 060 if the owner accepts the A4–A6 requirements as specified (they map to existing components, low risk).

---

## 6. Risks & decisions

| Item | Note |
|---|---|
| **Composition contract** | Verify the production component prop contracts compose as the prototype's `App.tsx` implies (grid `onRecordOpen`, shell reader props, tab `regarding`/`onProposalResolved`). Confirm in task 061 step 0. |
| **SpaarkeAi shared surface** | Redeploying SpaarkeAi ships current master to a live shared page. `/conflict-check` + coordinate via `projects/INDEX.md` before the 062/059 PRs. |
| **`NEEDS_REVIEW_CONFIG_ID`** | Chicken-and-egg: seed the config first, capture its id, set the constant, THEN build/deploy the host (Phase 4 ordering is load-bearing). |
| **Owner review gate** | Phase 1 exists so the owner signs off on A4–A6 in the prototype before we bake them into production. If the owner is happy from the description, 060 can be skipped and A4–A6 go straight into 061. |
| **Host decision (settled)** | Owner chose **both** code page + SpaarkeAi widget (2026-08-10). |

---

## 7.5 Reuse existing components — BINDING §11 (UAT round-2 directive, owner 2026-08-11)

**The UAT round-2 behaviors (§E of the UX requirements doc) MUST be built on the EXISTING modals + UI components — NOT re-implemented.** The prototype uses lightweight stand-ins for review only; production wires the real components below. Any new-component proposal for these surfaces fails the §11 gate.

| Behavior (UX §E) | Reuse THIS existing component/surface (do not rebuild) | Path / reference |
|---|---|---|
| **Quick Start chooser** (E1b — "+ New record" opens it) | **`QuickStartModal`** (the shipped SpaarkeAi Quick Start) | `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx` |
| **Record-creation wizards** launched from Quick Start (E1b) | The shipped **`Create*Wizard` code pages** — `CreateMatterWizard`, `CreateProjectWizard`, `CreateWorkAssignmentWizard`, `CreateEventWizard`, `CreateTodoWizard`, `CreateInvoiceWizard`, etc. — launched via the **Assistant surface-launch mechanism** (`consumerType` → `surfaceLaunchRegistry` → `handleSurfaceLaunch`), NOT a bespoke wizard | `src/solutions/Create*Wizard/`; `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`; `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` |
| **`.eml` auto-load into the wizard's upload / AI-pre-populate step** (E1c) | The **existing Assistant file-context pass-through** — `QuickStartModal`'s `fileCtx: { fileIds }` → `launchSurface({ consumerType, fileIds })` → `SurfaceHandoffEnvelope.fileIds` (SPE reference, **never inline binary** — invariant #4); the wizard reads the envelope and fetches content. Reconciliation only supplies the reconciled email's `.eml` SPE `fileId`. Do NOT build a new upload/seed path. | `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx` (L240–279 `fileCtx`→`launchSurface`); `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/{launchSurface,types}.ts` (`SurfaceHandoffEnvelope.fileIds`); `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` §4 hand-off seam |
| **Wizard shell** (if a reconciliation-local wizard surface is ever needed) | **`WizardModal`** preset | `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/WizardModal.tsx` |
| **"Update other fields" record modal** (E2c) | Open the confirmed record's **real form** via OOB **`Xrm.Navigation.navigateTo`** (record form) OR **`RecordNavigationModalShell`** — per `MODAL-DECISION-CRITERIA.md`. NOT a hand-built form. | `docs/standards/MODAL-DECISION-CRITERIA.md`; `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/` |
| **"+ New task" modal** (already shipped) | **`FormModal`** preset (done in 056) — OR, if a full task surface is wanted, the shipped **`CreateTodoWizard`/`CreateEventWizard`** via surface-launch. Decide at build; do NOT hand-roll. | `SprkModal/presets/FormModal.tsx`; `src/solutions/CreateTodoWizard/` |
| **Lookup fields + Assigned-to** (E2b/E3b) | The OOB **advanced-lookup side pane** (`Xrm.WebApi`/`lookupObjects` OOB control), not a bespoke picker; option-sets → Fluent `Dropdown`; dates → date control | `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`; OOB `Xrm.Utility.lookupObjects` |
| **All modal chrome** | **`SprkModal` presets** (`FormModal`/`WizardModal`/`BrowseModal`/`PreviewModal`) — ADR-050 | `docs/standards/MODAL-DESIGN-SYSTEM.md` |

**Modal-on-modal** (E1b + E2c stack a wizard/record surface on the open review modal): use the established stacked-`SprkModal` pattern (the browse shell already stacks a `PreviewModal`); each surface owns its `open` state so the underlying review modal stays open on close.

These reuse targets are carried into the follow-on task POMLs (063+) as explicit `<constraint>` + `<justification>` entries so a literal executor cannot rebuild them.

## 8. Definition of done

- `ReconciliationWorkspace` composes the production components honoring UX A1–A6 + B1–B13 + NFR-10/11; tests green.
- Code page `sprk_communicationreconciliation` **and** SpaarkeAi widget both render the grid and open the browse shell against live data.
- Needs-review + per-team gridconfigs seeded; `NEEDS_REVIEW_CONFIG_ID` points at the seeded record.
- Both surfaces deployed; `Deploy-AllDataGridConsumers` clean (no broken consumer).
- TASK-INDEX updated (060/061/062/059 ✅); deltas doc'd in notes.
