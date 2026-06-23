# Preflight Deviations — spaarkedev1

> **Task**: 001 — Pre-flight verification
> **Filed**: 2026-06-22
> **Purpose**: Document any pre-flight check that surfaced an unexpected reference, blocking the corresponding control / canvas app from the ready-to-delete list. Per [procedure §1.2](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#12-per-control-still-referenced-check): "Unexpected matches: STOP and reconcile."

---

## DEV-001 — AnalysisWorkspace PCF + Custom Page have a live ribbon dependency

**Discovered**: 2026-06-22 during procedure §1.2 Check 3 (ribbon-JS grep)

**Affected scope**:
- Customcontrol: `sprk_Spaarke.Controls.AnalysisWorkspace` (GUID `286d7717-5ec3-47eb-87ce-c022cec09f2d`) — v1.3.5
- Canvas app: `sprk_analysisworkspace_8bc0b` (GUID `ff384002-a0ea-4253-a2a0-88ae3c49108b`) — the Custom Page hosting the PCF
- Dedicated solution: `AnalysisWorkspaceSolution` (already backed up at [`backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip`](../backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip))

### Evidence

[`src/client/webresources/js/sprk_analysis_commands.js`](../../../src/client/webresources/js/sprk_analysis_commands.js) defines a ribbon command function `Spaarke_OpenAnalysisWorkspace(analysisId, documentId)` (line 145) that opens the AnalysisWorkspace Custom Page:

```javascript
// Lines 165-169
const pageInput = {
    pageType: "custom",
    name: "sprk_analysisworkspace_8bc0b",
    recordId: dataPayload  // Pass data via recordId (dialog workaround)
};
```

This is a **live runtime call** — if the canvas app is deleted, this ribbon command breaks. Since the canvas app hosts the PCF, deleting the PCF (which forces the canvas app's redeploy) has the same effect.

### Why the inventory missed this

The 2026-06-22 inventory ([pcf-deployment-inventory-2026-06-22.md §3 row 2](../../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md)) classified AnalysisWorkspace PCF as "Superseded by AnalysisWorkspace Code Page (`src/client/code-pages/AnalysisWorkspace/`)" based on the Code Page's existence. The assumption was that the migration to the Code Page had retired the canvas-page path. **The migration is incomplete** — `sprk_analysis_commands.js` still has the legacy navigation function and may still be wired to a ribbon button.

Companion file [`src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js`](../../../src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js) documents at line 578-580:

```
The existing Spaarke_OpenAnalysisWorkspace() function in sprk_analysis_commands.js
navigates to a Custom Page (pageType: "custom", name: "sprk_analysisworkspace_8bc0b").

With the Form OnLoad approach, the subgrid ribbon command for "Open Workspace"
(Spaarke.Analysis.OpenWorkspace.Command) is no longer needed — users click the...
```

The launcher's design comment implies the form-onload approach **superseded** the ribbon command — but the ribbon JS still exists and is still deployed (it's under `src/client/webresources/` which deploys as a `sprk_*.js` webresource). Whether the ribbon button that calls `Spaarke_OpenAnalysisWorkspace()` is still wired is the open question.

### Recommended resolution (before Task 003 acts on this row)

Three steps to verify before deciding:

1. **Maker portal check** — Open Spaarke model-driven app → find any subgrid that historically had "Open Workspace" ribbon button → inspect ribbon command. If `Spaarke_OpenAnalysisWorkspace` is the action, the path is live.
2. **Web resource staleness** — Check if `sprk_analysis_commands` web resource is up-to-date with the source file (or if a newer version has been deployed that removed `Spaarke_OpenAnalysisWorkspace`).
3. **Form-onload completeness** — Confirm `sprk_AnalysisWorkspaceLauncher.js`'s form-onload approach is wired to ALL forms that previously used the ribbon path. If yes, the ribbon function is dead code and the path is safe to break.

### Three possible outcomes from the resolution

| Outcome | Action |
|---|---|
| Ribbon button is still wired + active | **Do not delete** AnalysisWorkspace PCF or canvas app. File as a "blocked — needs migration completion" item. Migration to form-onload must finish first. |
| Ribbon button is unwired OR `sprk_analysis_commands.js` is updated/retired | Reclassify as `ready-to-delete`; resume per procedure §3.2 sequence. Update this deviation to `RESOLVED`. |
| Ambiguous / contested | Owner decision required. Default to "do not delete" until resolved. |

### Scope impact

- 10 of 11 customcontrols remain `ready-to-delete` for Task 003.
- 4 of 6 canvas apps remain `ready-to-delete`.
- AnalysisWorkspace PCF + canvas app held back from Task 003 scope pending resolution.
- AnalysisWorkspace's dedicated solution `AnalysisWorkspaceSolution` IS still backed up (in case future cleanup needs it).

### Status

`RESOLVED — 2026-06-22 — outcome 1 (PERMANENT HOLD)`

### Owner decision (2026-06-22)

**"Leave the command bar ribbon button (even though broken) because we may rewire it to a new analysis code page wizard modal."**

Decision implications:

- **`sprk_Spaarke.Controls.AnalysisWorkspace` PCF (`286d7717-…`) — DO NOT DELETE.** Stays deployed in spaarkedev1 + spaarkedev2 indefinitely.
- **`sprk_analysisworkspace_8bc0b` canvas page (`ff384002-…`) — DO NOT DELETE.** Stays deployed indefinitely. The PCF lives inside it; both must remain together.
- **`Spaarke_OpenAnalysisWorkspace()` ribbon function — LEAVE IN PLACE.** May be broken in steady-state today (form-onload migration incomplete) but is preserved for a planned future rewire to a new analysis Code Page wizard modal.
- **`AnalysisWorkspaceSolution` baseline backup — KEEP** in `backups-2026-06-22/` for future reference / rewire-planning context.

### Future-work flag (for the planned rewire project)

When the rewire happens:
- The new code-page wizard modal should be wired via `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_...' })` (NOT `pageType: 'custom'`) — mirroring the DocumentUploadWizard pattern that drove UQC's retirement (sprk_subgrid_commands.js v4.0.0).
- After the rewire ships, `sprk_analysisworkspace_8bc0b` canvas page + AnalysisWorkspace PCF + `Spaarke_OpenAnalysisWorkspace()` ribbon function become genuinely orphan and can be retired in a follow-on cleanup pass.
- The original 11-control / 6-canvas Task 003 scope can be revisited at that point.

---

## (No further deviations as of 2026-06-22)
