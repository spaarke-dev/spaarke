# PCF Orphan Cleanup — Specification

> **Project**: pcf-orphan-cleanup-r1
> **Spec version**: 1.0 (2026-06-22)
> **Authority**: this spec is the binding contract for what gets done. [design.md](design.md) is the operational procedure; [tasks/](tasks/) is the execution decomposition.

---

## 1. Functional Requirements

### FR-01 — Source-tree deletion

Delete three PCF folders that no consumer imports:
- `src/client/pcf/UniversalQuickCreate/` — orphan per ribbon migration (sprk_subgrid_commands.js v4.0.0)
- `src/client/pcf/DrillThroughWorkspace/` — built but never deployed
- `src/client/pcf/SpeDocumentViewer/` — confirmed unused 2026-06-22

After this, the source tree has 11 PCFs (not 14) with `ControlManifest.Input.xml`.

### FR-02 — Dataverse `customcontrol` deletion

Delete 11 published `customcontrol` records from spaarkedev1, then spaarkedev2 (after 7-day soak):

1. `sprk_Spaarke.Controls.UniversalDocumentUpload` (v3.15.2)
2. `sprk_Spaarke.SpeDocumentViewer` (v1.0.27) ← MANDATORY FormXML grep before deletion
3. `sprk_Spaarke.Controls.AnalysisBuilder` (v2.9.2)
4. `sprk_Spaarke.Controls.AnalysisWorkspace` PCF (v1.3.5)
5. `sprk_Spaarke.Controls.DueDatesWidget` (v1.0.8)
6. `sprk_Spaarke.Controls.EventAutoAssociate` (v1.0.0)
7. `sprk_Spaarke.Controls.EventCalendarFilter` (v1.0.5)
8. `sprk_Spaarke.Controls.EventFormController` (v1.0.6)
9. `sprk_Spaarke.Controls.FieldMappingAdmin` (v1.1.0)
10. `sprk_Spaarke.Controls.RegardingLink` (v1.1.6)
11. `sprk_Spaarke.LegalWorkspace` PCF (v1.0.1)

### FR-03 — Canvas-app deletion

Delete the orphan canvas apps that host the deleted PCFs (4 confirmed + 2 verify):

- `sprk_documentuploaddialog_e52db` (hosted UQC)
- `sprk_analysisbuilder_40af8` (hosted AnalysisBuilder)
- `sprk_analysisworkspace_8bc0b` (hosted AnalysisWorkspace PCF)
- `sprk_visualizationdrillthroughworkspace_e48a5` (hosted DrillThroughWorkspace — itself never deployed)
- (verify) `sprk_eventspage_786f4` (canvas-app predecessor of the EventsPage Code Page)
- (verify) `sprk_playbookbuilder_eb5ad` ← OUT OF SCOPE — PlaybookBuilderHost triage required first

### FR-04 — Shared lib `@types/react` peerDep declaration

In [`src/client/shared/Spaarke.UI.Components/package.json`](../../src/client/shared/Spaarke.UI.Components/package.json):

- Add `@types/react` and `@types/react-dom` to `peerDependencies` with range `>=16.14 || >=17 || >=18 || >=19`.
- Keep them in `devDependencies` for the lib's self-build.

Pre-merge audit: zero React 18+ runtime API usage in shared lib source (`useId`, `useTransition`, `useDeferredValue`, `useSyncExternalStore`, `useInsertionEffect`, `createRoot`, `hydrateRoot`, `startTransition`). Any hits → replace or feature-gate before merge.

### FR-05 — VisualHost React-16 realign

In [`src/client/pcf/VisualHost/package.json`](../../src/client/pcf/VisualHost/package.json):

- `react`: `^18.2.0` → `^16.14.0`
- `@types/react`: `^18.2.0` → `^16.14.60`

Re-verify zero React 18-only API usage in VisualHost source (already verified 2026-06-22; defensive re-run pre-merge). Deploy and smoke-test full feature surface (charts, drill-through, MetricCard, recent enhancements per b5b3118b1).

Bump manifest version (1.4.16 → 1.4.17).

### FR-06 — Environment replication

Apply FR-02 + FR-03 deletions to spaarkedev1 first. After **7-day soak** with no regression reports, replicate to spaarkedev2. Production replication (if/when applicable) follows the standard release procedure ([deploy-new-release](../../.claude/skills/deploy-new-release/SKILL.md)).

### FR-07 — Cleanup log

Per-control deletion log filed at `notes/dataverse-cleanup-log.md` (one entry per FR-02 / FR-03 deletion). Entry shape:
```
- YYYY-MM-DD HH:MM — {control name} v{x.y.z}
  - §1.2 results: refs = {none | list}
  - Hosting canvas: {name | none}
  - Solutions touched: {list}
  - Deletion confirmed: PCF GUID {GUID}; canvas GUID {GUID}
  - Smoke test: PASS / FAIL ({details})
```

### FR-08 — Inventory refresh

Update [`pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) with a completion footnote when this project lands. Update [`pcf-bundle-sizes.md`](../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) to remove the UQC + DTW + SDV entries from the baseline.

---

## 2. Non-Functional Requirements

### NFR-01 — Baseline backups are MANDATORY

Before ANY Dataverse-side deletion (FR-02 / FR-03), capture unmanaged solution-export ZIPs of every solution that contains a control slated for deletion. Park them in `projects/pcf-orphan-cleanup-r1/backups-2026-06-22/`. Without these, recovery from an over-deletion goes from "5-minute import" to "afternoon of forensic git+rebuild."

### NFR-02 — Pre-flight 4-check is MANDATORY

Per [design.md §1.2](design.md), before deleting any `customcontrol` or `canvasapp`:
1. Solution memberships (Dataverse MCP `read_query` on `solutioncomponent`)
2. **FormXML grep** (PAC CLI `pac solution export` + grep customizations.xml) ← **load-bearing**; Dataverse MCP cannot fetch FormXML body
3. Ribbon JS grep across `src/solutions/*` + `src/client/pcf/*` + `scripts/*`
4. Canvas-app dependencies (Dataverse MCP `read_query` on canvasapp)

A control is safe to delete ONLY when all four return clean. SpeDocumentViewer is explicitly flagged "do not skip FormXML grep" because PCF form-bindings are the highest-risk drift mode.

### NFR-03 — 7-day soak between environments

After spaarkedev1 cleanup completes, wait 7 calendar days of business use. Monitor for regression reports. Do NOT batch the spaarkedev2 cleanup into the same week.

### NFR-04 — No re-drift during execution

Source folders deleted in FR-01 must not be re-introduced by parallel work. If another in-flight project adds back a reference to UQC / DTW / SDV during this project's execution window, that takes precedence — escalate and pause.

### NFR-05 — Audit re-verification gate

Before merging the VisualHost re-pin PR (FR-05), re-run the React-18-API grep. The 2026-06-22 audit found zero hits; this gate catches any post-audit code change that would have invalidated the verdict.

### NFR-06 — One PR per workstream

- PR 1: source deletion (FR-01)
- PR 2: shared lib peerDep (FR-04)
- PR 3: VisualHost re-pin (FR-05)

Dataverse-side work (FR-02, FR-03, FR-06) is NOT a PR — it's a managed maker-portal + PAC CLI session, logged per FR-07.

### NFR-07 — Resurrection path documented + tested

[design.md §3.5](design.md) documents the resurrection playbook (source revert, customcontrol re-import, canvas app re-import). Before the Dataverse session runs in production, spot-test the recovery flow for one control in spaarkedev1: delete → verify gone → re-import baseline → verify restored. This proves the safety net works.

---

## 3. Success Criteria

| SC | Verification |
|---|---|
| SC-01 — Source tree drops 14 → 11 PCFs | `Glob src/client/pcf/*/ControlManifest.Input.xml` returns 4 files; `Glob src/client/pcf/*/*/ControlManifest.Input.xml` returns 7 files; total = 11 |
| SC-02 — spaarkedev1 customcontrol count drops appropriately | Dataverse MCP query: `SELECT COUNT(*) FROM customcontrol WHERE (name LIKE 'sprk_Spaarke%' OR name LIKE 'sprk_Sprk%') AND componentstate = 0` returns 13 (was 24) |
| SC-03 — Each FR-02 control returns zero results when re-queried | Dataverse MCP query per control name |
| SC-04 — Each FR-03 canvas app returns zero results when re-queried | Dataverse MCP query per canvas app name |
| SC-05 — `@spaarke/ui-components/package.json` has multi-major `@types/react` peerDep | Read package.json; confirm peerDeps block matches FR-04 |
| SC-06 — VisualHost deployed at v1.4.17 with React 16 type pins | Dataverse MCP query: VisualHost version = 1.4.17; smoke-test of charts + drill-through passes |
| SC-07 — Bundle-size baseline updated | [`pcf-bundle-sizes.md`](../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) no longer lists UQC / DTW / SDV |
| SC-08 — No TS errors in build matrix | `npm run build:prod` in each of 11 PCFs and `npm run build` in each of 4 code-pages: zero new TS2322 / TS2305 / TS2307 |
| SC-09 — spaarkedev2 mirrors spaarkedev1 post-soak | Re-run SC-02/03/04 against spaarkedev2 |
| SC-10 — Cleanup log complete | `notes/dataverse-cleanup-log.md` has one entry per FR-02 / FR-03 deletion across both environments |
| SC-11 — Backup ZIPs archived | `backups-2026-06-22/` contains one ZIP per touched unmanaged solution |
| SC-12 — Inventory refresh footnote present | [`pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) has "as of YYYY-MM-DD: cleanup complete" |

---

## 4. Explicitly out of scope

- **PlaybookBuilderHost** retirement — non-standard source layout + recent Dataverse activity; needs separate owner triage before any deletion action.
- **AIMetadataExtractor / SpeFileViewer / shared scaffolds** — folders that exist in `src/client/pcf/` but were never standalone PCFs per the 2026-05-14 bundle-size audit; not in Dataverse, no action needed.
- **Code Pages / Vite SPAs cleanup** — `src/solutions/*` and `src/client/code-pages/*` are out of scope. The 2026-06-01 test-rot audit identified 2 rot pockets (SemanticSearch test runner not wired; AnalysisWorkspace deprecated tests) — those are tracked separately as the RB-CLIENT-001 / RB-CLIENT-002 r3-scope candidates.
- **Shared lib React-18 API removal beyond what FR-04 audit surfaces** — if the pre-merge audit finds React 18+ APIs, they get fixed. If it finds none, no preemptive refactoring.
- **Production environment deployment** — this project ends at spaarkedev2. Production replication follows the standard release procedure as a separate execution.

---

## 5. Decisions made (binding for this project)

| ID | Date | Decision | Rationale |
|---|---|---|---|
| D-01 | 2026-06-22 | UQC + DTW + SDV deleted from source as a single PR | All three have zero external importers; bundling reduces review overhead; per-folder revert is still trivial if any one is challenged. |
| D-02 | 2026-06-22 | SpeDocumentViewer FormXML grep is MANDATORY (not advisory) | SDV is the highest form-binding risk among the orphans; treat the grep as a hard gate. |
| D-03 | 2026-06-22 | PlaybookBuilderHost EXPLICITLY out of scope | Non-standard source layout + recent Dataverse activity (2026-01-20); requires separate owner triage. |
| D-04 | 2026-06-22 | Bucket D fix re-pins VisualHost to React 16 (not VisualHost up to 19) | Matches platform-library runtime per ADR-022; VisualHost source has zero React 18-only APIs (verified). |
| D-05 | 2026-06-22 | 7-day soak between spaarkedev1 and spaarkedev2 | Match the production-release procedure cadence; allows business-hours regression discovery. |
| D-06 | 2026-06-22 | Project lifespan ends at spaarkedev2 completion | Production replication is a separate work item under the standard release procedure. |
