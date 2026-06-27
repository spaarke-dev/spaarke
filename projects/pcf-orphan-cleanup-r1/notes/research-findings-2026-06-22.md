# Research Findings — 2026-06-22

> **Purpose**: Capture the chain of investigation that scoped this project. This is the "WHY" record — future-you returns here to understand why specific decisions were made.
> **Style**: chronological-ish, evidence-first.

---

## 1. The originating report

On 2026-06-22, the chat-routing-redesign-r1 project filed a report flagging TypeScript drift in `@spaarke/ui-components` consumers. The report identified 4 buckets:

| Bucket | Symptom | Affected |
|---|---|---|
| A | Mixed `@spaarke/ui-components/dist/...` + `/src/...` imports breaking when `dist/` not built | UQC only |
| B | Broken SSE type re-export stub (`SseStreamStatus` / `SseDataChunk` / `UseSseStreamOptions` / `UseSseStreamResult` — names that don't exist in shared lib) | UQC only |
| C | TS1323 dynamic-import error caused by PCF tsconfig `module: es2015` not matching shared-lib usage | UQC only (surfaced via UQC's `src/` deep-imports) |
| D | `ReactElement` type identity collision between PCF `@types/react ^18` and shared lib `@types/react ^19` | 3 PCFs (DTW, SDV, VisualHost) + UQC |

Initial assessment: A, B, C are UQC-specific; D is the only workspace-wide issue.

## 2. The "is UQC even used?" question

User asked: "I thought we moved away from UQC — does that PCF still matter?"

Verified:
- UQC source folder exists at `src/client/pcf/UniversalQuickCreate/`
- Most recent source commit: `ec1d188e7` (chat-routing-redesign-r1 wave 0-A — the very project that filed the bucket report)
- Bundle.js exists on disk at `solution/src/WebResources/sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js` but **`git log --all -- {path}` returns empty** → file is local-only / gitignored, never committed
- 27 files reference `UniversalQuickCreate` or `UniversalDocumentUpload` across the repo (verified by Grep)

Conclusion at this point: source is alive but uncertain about Dataverse-side use.

## 3. Hunting for a prior PCF inventory

User recalled a project named "PCF-code-page-inventory." Verified: no such project exists.

Adjacent inventory artifacts found:

| Artifact | Date | Scope | Verdict on UQC |
|---|---|---|---|
| [`projects/ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md`](../../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) | 2026-05-14 | Bundle-size baseline; PCF folder enumeration + build:prod correctness | "Built but not deployed yet" (no committed bundle in solution) |
| [`projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) | 2026-06-01 | Test-rot audit per surface | `clean` (3 test files, ~65 tests, all running) |

Neither audited deployment status — both were lens-specific. Decision: build a third inventory focused on deployment.

## 4. The 2026-06-22 deployment inventory

Methodology:
- Glob `src/client/pcf/*/ControlManifest.Input.xml` (4 hits at top level) + `src/client/pcf/*/*/ControlManifest.Input.xml` (10 hits one level deep) → 14 source-tree PCFs
- Read each manifest for `namespace.constructor.version`
- Dataverse MCP query: `SELECT * FROM customcontrol WHERE (name LIKE 'sprk_Spaarke%' OR name LIKE 'sprk_Sprk%') AND componentstate = 0` (with 20-record cap → 2 batches)
- Read [`src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js`](../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js) for ribbon evidence

Results captured in [`pcf-deployment-inventory-2026-06-22.md`](../../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md). Key data points:

**Source tree (14 PCFs with manifests)**: AssociationResolver, DocumentRelationshipViewer, DrillThroughWorkspace, EmailProcessingMonitor, RegardingResolver, RelatedDocumentCount, ScopeConfigEditor, SemanticSearchControl, SpaarkeGridCustomizer, SpeDocumentViewer, ThemeEnforcer, UniversalQuickCreate, UpdateRelatedButton, VisualHost.

**Dataverse-deployed (24 PCFs)**: 13 of the 14 source PCFs (DrillThroughWorkspace missing — never deployed) + 11 "ghost" PCFs deployed without any source-tree backing:
- `sprk_Spaarke.Controls.AnalysisBuilder` v2.9.2 — predates AnalysisWorkspace Code Page
- `sprk_Spaarke.Controls.AnalysisWorkspace` v1.3.5 — superseded by AnalysisWorkspace Code Page
- `sprk_Spaarke.Controls.DueDatesWidget` v1.0.8
- `sprk_Spaarke.Controls.EventAutoAssociate` v1.0.0
- `sprk_Spaarke.Controls.EventCalendarFilter` v1.0.5 — superseded by `@spaarke/events-components` CalendarFilterPane
- `sprk_Spaarke.Controls.EventFormController` v1.0.6
- `sprk_Spaarke.Controls.FieldMappingAdmin` v1.1.0
- `sprk_Spaarke.Controls.PlaybookBuilderHost` v2.25.0 — source layout non-standard, NOT triaged as orphan
- `sprk_Spaarke.Controls.RegardingLink` v1.1.6 — predecessor of RegardingResolver
- `sprk_Spaarke.LegalWorkspace` v1.0.1 — superseded by LegalWorkspace Code Page
- `sprk_Spaarke.UI.Components.UniversalDatasetGrid` v2.3.1 — built from shared lib, not standalone PCF

**6 canvas apps (Custom Pages, type=2)**: documentuploaddialog (UQC's host), eventspage, analysisworkspace (PCF host), visualizationdrillthroughworkspace (DTW's host — but DTW PCF isn't deployed!), playbookbuilder, analysisbuilder.

## 5. The UQC smoking gun

[`src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js`](../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js) lines 582-585 — version-history comment block:

```
4.0.0: Code Page (webresource) dialog approach with React 18 (sprk_documentuploadwizard)
3.0.x: Custom Page dialog approach with PCF control (sprk_documentuploaddialog_e52db)
2.1.0: Form Dialog approach using sprk_uploadcontext utility entity
2.0.0: Initial release for Custom Page architecture (deprecated)
```

The ribbon command that drove "Add Documents" on every Documents subgrid migrated from UQC's Custom Page → Code Page in v4.0.0. The PCF and its hosting Custom Page (`sprk_documentuploaddialog_e52db`) are deployed but **no traffic reaches them** — verified by reading both `openDocumentUploadDialog` call paths at lines 361 and 512, both of which target `sprk_documentuploadwizard` (the Code Page).

This is the foundational evidence that made UQC retirement actionable.

## 6. The Dataverse version drift detail

- Source manifest: UQC v3.15.3 ([`UniversalQuickCreate/control/ControlManifest.Input.xml:3`](../../../src/client/pcf/UniversalQuickCreate/control/ControlManifest.Input.xml#L3))
- Dataverse-deployed: UQC v3.15.2 (modified 2026-03-24)
- Local bundle.js exists but uncommitted

Interpretation: a fleet rebuild pass (likely `spaarke-auth-v2-and-hardening` task 028 "verify-rebuild-pcfs") bumped UQC's manifest along with everything else but never deployed because traffic had already migrated. Local bundle is dead-code from that uncompleted rebuild.

## 7. DTW + SDV — confirming the suspicion

User stated: "I'm not sure DrillThroughWorkspace or SpeDocumentViewer are used."

Verified by source-tree grep:
- **DrillThroughWorkspace**: 11 hits — all inside `src/client/pcf/DrillThroughWorkspace/` itself. Zero external importers.
- **SpeDocumentViewer**: 6 hits — all inside `src/client/pcf/SpeDocumentViewer/` itself (manifest, package, solution files). Zero external importers.

Cross-referenced with Dataverse modifiedon:
- DTW: not deployed (consistent with 2026-05-14 bundle audit's "Built but not deployed yet")
- SDV: deployed v1.0.27, last modified 2026-05-25 (so deployed AND somewhat recent — but no source-tree references, suggesting it was bound to forms via Dataverse-side configuration that left no source-tree footprint)

User confirmed 2026-06-22: both unused. Folded into Part B (source delete) + Part C (Dataverse delete for SDV; canvas-app cleanup for DTW).

## 8. VisualHost — the can-we-downversion question

User concern: "VisualHost is used and we made enhancement recently — does downversioning React 18 → React 16 break those?"

Critical test: grep `src/client/pcf/VisualHost/control/**/*.{ts,tsx}` for React 18-only APIs (`useId`, `useTransition`, `useDeferredValue`, `useSyncExternalStore`, `useInsertionEffect`, `createRoot`, `hydrateRoot`, `startTransition`).

**Result: zero hits in source.** Only match anywhere is a historical comment in [`control/index.ts:4`](../../../src/client/pcf/VisualHost/control/index.ts#L4) — "Migrated from ComponentFramework.StandardControl (imperative ReactDOM.render)" — describing what it migrated AWAY from.

All earlier noise (storybook-static + bundle.js hits) was build artifacts:
- `storybook-static/` — Storybook docs site, separate React 18 runtime
- `bundle.js` (SpeDocumentViewer) — bundled third-party Fluent + Griffel polyfilled hooks, not the PCF's own code

Foundational fact: VisualHost's manifest declares `<platform-library name="React" version="16.14.0" />` ([line 148](../../../src/client/pcf/VisualHost/control/ControlManifest.Input.xml#L148)). The PCF runtime is ALWAYS React 16.14.0 regardless of `@types/react` pin. If VisualHost source had used React 18 runtime APIs, it would already be broken in production. It isn't. So it doesn't.

Conclusion: safe to re-pin DOWN from `@types/react ^18` to `^16.14.x`. Recent enhancements use React 16-compatible APIs only.

## 9. The "is recovery real?" question

User: "If we run into an area where we inadvertently deleted a PCF that was actually needed can we resurrect OR recreate?"

Three deletion types, three recovery paths:

| Deletion | Recovery | Cost | GUID stable? |
|---|---|---|---|
| Source-tree | `git revert {sha}` or `git checkout {sha}~1 -- path/` | seconds (local) | n/a |
| Dataverse customcontrol | `pac solution import --path baseline-{X}-pre-cleanup-2026-06-22.zip` | 5-10 min | **NO — new GUID** |
| Dataverse canvas app | Same as above | 5-10 min | **NO — new GUID** |

The GUID-change gotcha is the only real risk: if any form/ribbon hardcoded the old GUID (rare — most are name-based), re-wire is needed.

The §1.1 backup ZIPs are the safety net. Without them, recovery for the 9 already-source-less PCFs means digging through git history to find the last commit that had their source, which gets expensive months after the fact.

## 10. Scope finalization

Final project scope established 2026-06-22:

| Workstream | Count | What |
|---|---:|---|
| Source delete (1 PR) | 3 | UQC, DTW, SDV |
| Dataverse customcontrol delete (2 environments) | 11 | UQC, SDV, AnalysisBuilder, AnalysisWorkspace PCF, DueDatesWidget, EventAutoAssociate, EventCalendarFilter, EventFormController, FieldMappingAdmin, RegardingLink, LegalWorkspace PCF |
| Canvas app delete (2 environments) | 4-6 | documentuploaddialog, analysisbuilder, analysisworkspace, visualizationdrillthroughworkspace (DTW's empty host); verify eventspage and playbookbuilder |
| Shared lib peerDep (1 PR) | 1 | `@spaarke/ui-components` package.json |
| VisualHost re-pin (1 PR) | 1 | VisualHost package.json + manifest version bump |
| Explicitly excluded | 1 | PlaybookBuilderHost — separate owner triage |

Net effect:
- Source tree: 14 PCFs → 11 PCFs
- Dataverse (per env): 24 deployed PCFs → 13 deployed PCFs
- Canvas apps (per env): 6 Spaarke-owned → 0-2 Spaarke-owned (playbookbuilder + eventspage pending verification)

## 11. Why this needed to be a project, not a procedure doc alone

The procedure doc ([`orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md`](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md)) is operationally complete. Reasons to wrap it in project structure anyway:

1. **Production blast radius** — Dataverse mutations are not GUID-stable for restoration. Project structure forces explicit gating (current-task.md, TASK-INDEX.md, per-task POMLs).
2. **Multi-week timeline** — 7-day soak between environments means the work spans calendar weeks. Project structure provides recovery anchors after compaction or session breaks.
3. **Multiple workstreams that must sequence correctly** — source delete → Dataverse cleanup → React-types fix → environment replay. Encoding the dependency graph in `tasks/TASK-INDEX.md` is cheaper than re-deriving it from memory.
4. **Audit trail** — the cleanup-log per FR-07 is the post-execution audit artifact. Project structure makes that log a first-class deliverable rather than ad-hoc note.

## 12. Open questions resolved at project setup

| Question | Answer | Resolved |
|---|---|---|
| Is the only PCF being retired UQC, or are there more? | 3 source + 11 Dataverse, not just UQC | §10 |
| Is VisualHost re-pinning safe? | YES — zero React 18 APIs in source | §8 |
| Is recovery possible after deletion? | YES, with §1.1 backups; GUID changes on re-import | §9 |
| Where does PlaybookBuilderHost fit? | OUT of scope — needs separate triage | §10 |
| Should source delete and Dataverse delete be one PR or separate? | One PR for source (FR-01); Dataverse is NOT a PR (logged session per FR-07) | spec.md / D-01 / NFR-06 |
| Should VisualHost re-pin go UP to React 19 or DOWN to React 16? | DOWN to React 16 to match platform-library runtime per ADR-022 | D-04 |
