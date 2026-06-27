# Orphan PCF Cleanup + React Types Alignment — Procedure

> **Date**: 2026-06-22
> **Companion to**: [pcf-deployment-inventory-2026-06-22.md](pcf-deployment-inventory-2026-06-22.md)
> **Trigger**: chat-routing-redesign-r1 raised TypeScript drift in `@spaarke/ui-components` consumers (bucket A/B/C/D in the original report); inventory pass confirmed UQC and 10 other PCFs are orphans.
> **Status**: APPROVED 2026-06-22 — owner confirmed DTW + SDV are unused; expanded scope; bucket D shrunk to VisualHost-only.
> **Scope**: source-tree cleanup (UQC + DTW + SDV) + Dataverse-side cleanup (12 PCFs, ~6 canvas apps) + shared-lib @types/react alignment + VisualHost re-pin.
> **Estimated effort**: 3–5 days end-to-end across 4 PRs + 1 Dataverse cleanup session.
>
> **Revision 2026-06-22 (post owner confirmation)**:
> - DrillThroughWorkspace + SpeDocumentViewer confirmed unused. Folded into Part B (source delete) and Part C (Dataverse delete).
> - PR-D2 shrunk from 3 PCFs to 1 (VisualHost only) — DTW + SDV go away in Part C instead of being re-pinned.
> - Added §3.5 explicit resurrection / recovery playbook.

---

## 0. Why this is one procedure, not three

Three distinct fix-streams emerged from the chat-routing-redesign-r1 audit:

1. **Source-tree drift** — UQC has source but no live deployment use.
2. **Dataverse drift** — 10 published `customcontrol` records have no source backing.
3. **Type-system drift** — shared lib `@types/react ^19` collides with PCF `@types/react ^16/^18` pins; ribbon-of-elements typing errors (bucket D in the original report).

They share one root cause (no system has been the authoritative inventory of which PCFs exist + which are alive) and one fix vehicle (one focused project that cleans both source and Dataverse and re-pins the type contract). Doing them separately invites re-drift between PRs.

---

## 1. Pre-flight (binding, before ANY destructive action)

### 1.1 Capture a baseline backup

```powershell
# Replace {ENV} with spaarkedev1, etc.
pac auth select --name {ENV}
pac solution list --output table > baseline-solution-list-2026-06-22.txt
# For each solution that contains an orphan control, export it unmanaged BEFORE editing
pac solution export --name {SolutionName} --path baseline-{SolutionName}-pre-cleanup-2026-06-22.zip --managed false --include general
```

Park the ZIPs in `projects/ai-procedure-quality-r1/notes/inventory/backups-2026-06-22/`. These are the rollback source if any cleanup step proves wrong.

### 1.2 Per-control "still-referenced?" check

For every PCF and canvas app slated for deletion, confirm zero live references BEFORE removing. Run these checks via Dataverse MCP `read_query` (or PAC CLI for the FormXML body, since MCP can't fetch `formxml` directly):

**Check 1 — Solution memberships** (which solutions contain this control?):
```sql
-- objectid = customcontrolid
SELECT TOP 20 sc.solutionid, sc.objectid, s.uniquename
FROM solutioncomponent sc
JOIN solution s ON sc.solutionid = s.solutionid
WHERE sc.objectid = '{CUSTOMCONTROL_GUID}'
```

**Check 2 — Form references** (does any systemform's formxml mention the constructor?):
```powershell
# PAC CLI - export the unmanaged solution containing the suspected forms and grep
# (Dataverse MCP cannot fetch formxml body directly)
pac solution export --name Default --path Default-temp.zip --managed false
Expand-Archive Default-temp.zip -DestinationPath Default-temp/
Select-String -Path Default-temp\customizations.xml -Pattern "Spaarke.Controls.UniversalDocumentUpload"
```

**Check 3 — Ribbon references** (does any ribbon JS or RibbonDefinition reference the canvas app?):
```powershell
Select-String -Path Default-temp\customizations.xml -Pattern "sprk_documentuploaddialog_e52db"
```
Also grep `src/solutions/*/` and `src/client/pcf/*/` source for the same string.

**Check 4 — Canvas app dependencies** (what does each orphan canvas app reference?):
```sql
SELECT TOP 5 name, appcomponentdependencies, cdsdependencies FROM canvasapp WHERE canvasappid = '{GUID}'
```

**Gate**: a control / canvas app is safe to delete ONLY when:
- Check 1 lists only solutions YOU intend to remove it from
- Check 2 returns zero matches in any form definition
- Check 3 returns zero matches in any ribbon JS or `RibbonDefinitions`
- Check 4 confirms no incoming dependencies from other live artifacts

If ANY check returns unexpected results, **stop and reconcile** — do not proceed with deletion. File the finding as an "unexpected reference" note in the project log.

### 1.3 Environment scope

All Dataverse-side operations execute against **spaarkedev1 first**. After successful spaarkedev1 cleanup + 1-week soak with no regression reports, apply to spaarkedev2 and any prod-class envs in turn.

---

## 2. Part B — source-tree deletion (PR 1)

### 2.1 Goal

Delete three orphan PCF folders. After this PR, the source tree has 11 PCFs, not 14:
- `src/client/pcf/UniversalQuickCreate/` — orphan per ribbon migration evidence
- `src/client/pcf/DrillThroughWorkspace/` — built but never deployed (per 2026-05-14 bundle audit; reconfirmed 2026-06-22)
- `src/client/pcf/SpeDocumentViewer/` — confirmed unused 2026-06-22 (no external importers in source; owner confirms)

### 2.2 Steps

1. **Identify residual importers** for all three:
   ```powershell
   # From repo root
   Select-String -Path "src\**\*.ts","src\**\*.tsx","src\**\*.js" -Pattern "UniversalQuickCreate|UniversalDocumentUpload|DrillThroughWorkspace|SpeDocumentViewer|@spaarke/ui-components/src/services/document-upload|@spaarke/ui-components/dist/utils/themeStorage" -SimpleMatch
   ```
   Expected matches (safe to ignore — these are inside the folders being deleted):
   - `src/client/pcf/UniversalQuickCreate/**`, `src/client/pcf/DrillThroughWorkspace/**`, `src/client/pcf/SpeDocumentViewer/**` (all files — going away)
   - `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js` lines 582-585 (the version-history comment block — keep, it's documentation)

   Unexpected matches: STOP and reconcile.

2. **Delete the three folders**:
   ```powershell
   git rm -r src/client/pcf/UniversalQuickCreate/
   git rm -r src/client/pcf/DrillThroughWorkspace/
   git rm -r src/client/pcf/SpeDocumentViewer/
   ```

3. **Verify the deprecated SSE re-export stub is gone**. The original bucket B finding (broken `useSseStream.ts` stub re-exporting nonexistent `SseStreamStatus` / `SseDataChunk` / `UseSseStreamOptions` / `UseSseStreamResult`) lives at `src/client/pcf/UniversalQuickCreate/control/services/useSseStream.ts` — `git rm -r` removes it automatically. Confirm no other file imports from it:
   ```powershell
   Select-String -Path "src\**\*.ts","src\**\*.tsx" -Pattern "from ['""].*UniversalQuickCreate.*useSseStream"
   ```

4. **Update any script that lists PCFs to deploy** if it hard-codes the three controls. Search:
   ```powershell
   Select-String -Path "scripts\**\*.ps1","scripts\**\*.sh","scripts\**\*.js" -Pattern "UniversalQuickCreate|UniversalDocumentUpload|DrillThroughWorkspace|SpeDocumentViewer"
   ```

5. **Build the workspace** to confirm no consumer broke:
   ```powershell
   # Repo root
   dotnet build src\server\api\Sprk.Bff.Api\
   # PCF workspace
   pushd src\client\pcf; npm run build; popd
   ```

6. **Commit**:
   ```
   chore(pcf): retire 3 orphan PCFs (UQC + DrillThroughWorkspace + SpeDocumentViewer)

   - UQC: ribbon migrated to Code Page in sprk_subgrid_commands.js v4.0.0
   - DrillThroughWorkspace: built locally, never deployed (per 2026-05-14 bundle audit)
   - SpeDocumentViewer: no external importers; confirmed unused 2026-06-22

   Dataverse-side cleanup follows in subsequent PR.
   See: projects/ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md §4
   ```

### 2.3 Rollback

`git revert {sha}`. All three folders restored verbatim. No external state changed.

> **Why "all three in one PR" vs three PRs**: each deletion is independent (no cross-folder dependencies), the validation is the same single grep, and one PR keeps the review burden contained. If a reviewer challenges any one of the three, that one can be backed out with a targeted `git restore` before merge.

---

## 3. Part C — Dataverse cleanup (single session, gated)

### 3.1 Cleanup targets

12 `customcontrol` records + 4-6 canvas apps. Ordered by risk (lowest first — the ones with documented evidence of orphan status come first):

| Order | customcontrol | Hosting canvas app | Why orphan | Verify before deleting |
|---:|---|---|---|---|
| 1 | `sprk_Spaarke.Controls.UniversalDocumentUpload` v3.15.2 | `sprk_documentuploaddialog_e52db` | Ribbon migrated to Code Page v4.0.0 (sprk_subgrid_commands.js:582-585) | §1.2 checks 1–4 |
| 2 | `sprk_Spaarke.SpeDocumentViewer` v1.0.27 | (none — verify) | No external importers in source (confirmed 2026-06-22) | §1.2 checks 1–4 **MANDATORY** — SDV was likely bound to forms; verify FormXML grep is clean |
| 3 | `sprk_Spaarke.Controls.AnalysisBuilder` v2.9.2 | `sprk_analysisbuilder_40af8` | Superseded by AnalysisBuilder Code Page | §1.2 checks 1–4 |
| 4 | `sprk_Spaarke.Controls.AnalysisWorkspace` v1.3.5 | `sprk_analysisworkspace_8bc0b` | Superseded by AnalysisWorkspace Code Page (`src/client/code-pages/AnalysisWorkspace/`) | §1.2 checks 1–4 |
| 5 | `sprk_Spaarke.Controls.DueDatesWidget` v1.0.8 | (none — verify) | Retired | §1.2 checks 1–4 |
| 6 | `sprk_Spaarke.Controls.EventAutoAssociate` v1.0.0 | (none — verify) | Retired | §1.2 checks 1–4 |
| 7 | `sprk_Spaarke.Controls.EventCalendarFilter` v1.0.5 | (none — verify) | Superseded by `@spaarke/events-components` CalendarFilterPane | §1.2 checks 1–4 |
| 8 | `sprk_Spaarke.Controls.EventFormController` v1.0.6 | (none — verify) | Retired | §1.2 checks 1–4 |
| 9 | `sprk_Spaarke.Controls.FieldMappingAdmin` v1.1.0 | (none — verify) | Admin UI consolidated | §1.2 checks 1–4 |
| 10 | `sprk_Spaarke.Controls.RegardingLink` v1.1.6 | (none — verify) | Predecessor of RegardingResolver (still deployed) | §1.2 checks 1–4 |
| 11 | `sprk_Spaarke.LegalWorkspace` v1.0.1 | (none — verify; the `sprk_eventspage_786f4` canvas may be related) | Superseded by LegalWorkspace Code Page | §1.2 checks 1–4 |
| 12 | (DrillThroughWorkspace — source delete only; never deployed to Dataverse, so no Part C action) | `sprk_visualizationdrillthroughworkspace_e48a5` (canvas app exists but hosts a control that isn't deployed — verify and delete the empty canvas if confirmed) | Built but never deployed | §1.2 check 4 on the canvas app only |
| (open) | `sprk_Spaarke.Controls.PlaybookBuilderHost` v2.25.0 | `sprk_playbookbuilder_eb5ad` | Source layout non-standard — needs owner triage BEFORE listing as orphan | OWNER DECISION REQUIRED — do NOT delete in this pass |

> **Why SDV comes second**: it has the most pre-existing form-binding risk of the orphans (PCFs bound to document-bearing entity forms). The §1.2 check 2 (FormXML grep) is the load-bearing safety check — do not skip on SDV.
> **Why DTW is in this table at all**: its source goes away in Part B, but its hosting canvas app `sprk_visualizationdrillthroughworkspace_e48a5` is still deployed (2026-03-09). Verify the canvas is truly orphaned (no ribbon button opens it) before deleting; if confirmed, delete the canvas in step 6 of the per-control sequence.

> Order matters: UQC first because it has the cleanest evidence chain (ribbon migration documented in source). PlaybookBuilderHost is NOT in this pass — it needs a separate owner triage (see [pcf-deployment-inventory-2026-06-22.md §6.4](pcf-deployment-inventory-2026-06-22.md)).

### 3.2 Per-control deletion sequence

For each row in §3.1 (in the listed order), execute these steps in `spaarkedev1`:

**Step 1 — Run §1.2 checks**. Document the output in a per-control log. STOP if any check returns unexpected references.

**Step 2 — Remove form references first** (if §1.2 check 2 surfaced any). For each form that includes the PCF as a control:
- In Power Apps Maker: open the form → select the field/section with the control → "Remove custom control" → save → publish.
- OR: edit the form via `pac solution clone --name {SolutionName}` → modify `customizations.xml` → re-pack → import.
- Verify the form opens correctly without the PCF.

**Step 3 — Unpublish the canvas app** (if applicable):
- Power Apps Maker → Apps → find the canvas app by `name` (e.g., `sprk_documentuploaddialog_e52db`) → "..." → Unpublish.

**Step 4 — Remove canvas app from solution**:
- Power Apps Maker → Solutions → open each solution containing the canvas app → Objects → select canvas → Remove → "Remove from this solution" (not "Delete from this environment" — keep that for Step 6).

**Step 5 — Remove customcontrol from solution**:
- Same UI flow: open each owning solution → Objects → select the PCF → Remove → "Remove from this solution".

**Step 6 — Delete the canvas app** (now unsolutioned + unpublished):
- Power Apps Maker → Apps → "..." → Delete. Confirm.

**Step 7 — Delete the customcontrol** (now unsolutioned):
- Easiest path: from a maker session with the customizations area open, find the PCF under "Custom controls" → Delete.
- Alternative (Web API): `DELETE /api/data/v9.2/customcontrols({customcontrolid})` with `Authorization: Bearer {token}`. Requires `System Administrator` role.

**Step 8 — Verify**:
- Re-run §1.2 check 1 for this control's GUID — should return empty.
- `mcp__dataverse__read_query` against `customcontrol` with the deleted name — should return empty.
- Quick smoke test of any nearby live PCFs (e.g., after deleting UQC, open a Matter → confirm Documents subgrid still works; after deleting EventCalendarFilter, open the Events Page Code Page → confirm filtering still works).

**Step 9 — Log**:
Per-control log entry in `projects/ai-procedure-quality-r1/notes/inventory/dataverse-cleanup-log-2026-06-22.md`:
```
- 2026-MM-DD HH:MM — {control name} v{x.y.z}
  - §1.2 results: refs = none (or list)
  - Hosting canvas: {name or none}
  - Solutions touched: {list}
  - Deletion confirmed: PCF GUID {GUID}; canvas GUID {GUID}
  - Smoke test: PASS / FAIL ({details})
```

### 3.3 Rollback

If a deletion turns out to break something:
- Re-import the relevant baseline solution backup from §1.1 (`pac solution import --path baseline-{X}-pre-cleanup-2026-06-22.zip`). This re-creates the PCF and canvas-app records.
- Note: importing a backup will NOT restore form references that were edited in Step 2 — those need manual re-add.

### 3.4 Soak

After all 11 deletions + canvas-app cleanups complete in spaarkedev1, wait **7 days** of business use. If no regression reports, repeat the sequence in spaarkedev2. Do NOT batch-fire across environments.

---

### 3.5 Resurrection / recovery — explicit playbook

If a Part C deletion turns out to have broken something live, recovery is straightforward IF the §1.1 baseline backups exist. Three recovery paths, by what got deleted:

#### 3.5.1 Source-tree deletion (Part B) was wrong

Recovery cost: **seconds**. Either:
```powershell
# Surgical — bring back just one folder
git checkout {pre-deletion-sha}~1 -- src/client/pcf/SpeDocumentViewer/

# OR full PR revert (if all three deletions need to come back)
git revert {part-B-merge-sha}
```
The folder is restored verbatim. Re-run `npm install --legacy-peer-deps` in the workspace; build with `npm run build:prod`; redeploy if Part C also ran on this control (see 3.5.2).

#### 3.5.2 Dataverse `customcontrol` deletion (Part C) was wrong

Recovery cost: **5–10 min**. From the §1.1 baseline backup:
```powershell
pac auth select --name spaarkedev1
pac solution import --path baseline-{SolutionName}-pre-cleanup-2026-06-22.zip --force-overwrite --publish-changes
```

This restores the PCF record with its bundle.js and manifest. **The `customcontrolid` GUID will be different from the deleted one.** Implications:
- Form bindings that reference the control by name (the common case) — work immediately after publish.
- Anything that hardcoded the old GUID (rare — verify by grepping `customizations.xml` from the imported solution for the new GUID and any form that was edited in Part C step 2) — needs re-wire.

After import, if any form had its PCF binding manually removed in Part C step 2, re-add the binding via the Power Apps Maker form designer (it'll resolve the control by name automatically).

#### 3.5.3 Canvas app deletion (Part C) was wrong

Recovery cost: **5–10 min**. Same `pac solution import` as 3.5.2 — the canvas app's `.msapp` blob is preserved in the solution backup. The `canvasappid` GUID will be different from the deleted one; any ribbon JS that hardcoded the old GUID needs to be updated to the new one.

In practice, ribbon JS in this codebase references canvas apps by NAME (see [sprk_subgrid_commands.js:361](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js#L361) — `webresourceName: "sprk_documentuploadwizard"`) — so name-based refs survive. GUID-based refs are the exception; the §1.2 check 3 ribbon grep would have surfaced any during pre-flight.

#### 3.5.4 No baseline backup exists (worst case)

If the §1.1 backup ZIPs were skipped (DO NOT skip them — that's the whole point of §1):

- **Source-deletion recovery still works** — git history is permanent.
- **Customcontrol recovery is harder for the 9 already-source-less PCFs** (AnalysisBuilder, etc.): trace git history for the last commit that had the source folder (`git log --all -- src/client/pcf/{X}`), restore via `git checkout {sha} -- {path}`, rebuild, deploy as a new solution. Hours-to-days depending on how far back the source was removed.
- **Customcontrol recovery for UQC/DTW/SDV without backup**: still doable from current source (or from the Part B `git revert`). Build with `npm run build:prod`, deploy via the PCF deployment guide. ~1 hour per control.

The §1.1 backup is the difference between "5-minute panic" and "afternoon of forensic recovery." Make the backups.

---

## 4. Part D — Shared lib + PCF React-types alignment (PRs 2 + 3)

### 4.1 Current state (post Part B)

After deleting UQC + DrillThroughWorkspace + SpeDocumentViewer in Part B, the remaining 11 PCFs split into two camps:

| Camp | Count | PCFs | Pins |
|---|---:|---|---|
| **React 16** (matches platform-library runtime, ADR-022 canonical) | 9 | AssociationResolver, DocumentRelationshipViewer, EmailProcessingMonitor, RegardingResolver, RelatedDocumentCount, ScopeConfigEditor, SemanticSearchControl, SpaarkeGridCustomizer, UpdateRelatedButton | `react ^16.14.0`, `@types/react ^16.14.x` |
| **React 18** (drift — does not match platform-library runtime) | 1 | VisualHost | `react ^18.2.0`, `@types/react ^18.2.0` |
| **n/a** (shared-lib-published, not a standalone PCF) | 1 | ThemeEnforcer | `react ^16.14.0`, `@types/react ^16.14.x` |

Plus the shared lib at `@types/react ^19.0.0` in `devDependencies` ([`Spaarke.UI.Components/package.json:69`](../../../../src/client/shared/Spaarke.UI.Components/package.json#L69)).

> Owner-confirmed 2026-06-22: VisualHost source contains zero React 18-only API usage (`useId`, `useTransition`, `useDeferredValue`, `useSyncExternalStore`, `useInsertionEffect`, `createRoot`, `hydrateRoot`, `startTransition`) — verified via grep of `src/client/pcf/VisualHost/control/**/*.{ts,tsx}`. Re-pin to React 16 is feature-preserving.

### 4.2 Target state

Canonical contract:
- **PCF runtime**: React 16.14.0 (platform-library, per ADR-022 — unchanged).
- **PCF type pins**: `@types/react ^16.14.x` and `react ^16.14.0` across all 11 PCFs (VisualHost re-pinned down from ^18; DTW + SDV go away in Part B/C instead).
- **Shared lib `@spaarke/ui-components`**:
  - `peerDependencies`: `react: >=16.14.0` (already there), `react-dom: >=16.14.0` (already there), **`@types/react: >=16.14 || >=17 || >=18 || >=19`** (NEW), **`@types/react-dom: >=16.14 || >=17 || >=18 || >=19`** (NEW).
  - `devDependencies`: keep `@types/react ^19.0.0` for the lib's own type-checking. The peerDep tells consumers "bring your own".
- **Code Pages and `src/solutions/*`** (Vite SPAs): continue with their own React 18+ / `@types/react` 18+ pins. The shared lib's peerDep range accepts them.

This is consistent with ADR-022 ("React 16 APIs only" for PCFs — the shared lib must avoid React 18+ APIs to be safe to consume from a React 16 PCF).

### 4.3 PR-D1 — Shared lib peerDep fix

**File**: [`src/client/shared/Spaarke.UI.Components/package.json`](../../../../src/client/shared/Spaarke.UI.Components/package.json)

**Change** — add to `peerDependencies` (keep devDeps as-is for self-build):
```json
"peerDependencies": {
  "@types/react": ">=16.14 || >=17 || >=18 || >=19",
  "@types/react-dom": ">=16.14 || >=17 || >=18 || >=19",
  ... existing react / react-dom peerDeps ...
}
```

**Verify** — before merging, run an audit of the shared lib source for React 18+ APIs that would silently break React-16 consumers:

```powershell
# From src/client/shared/Spaarke.UI.Components
Select-String -Path "src\**\*.ts","src\**\*.tsx" -Pattern "\b(useId|useTransition|useDeferredValue|useSyncExternalStore|useInsertionEffect|createRoot|hydrateRoot|startTransition)\b"
```

If hits — for EACH hit, decide:
- **Replace with a React-16-compatible equivalent** (e.g., `useId` → manual id generator with `useRef`; `useSyncExternalStore` → manual `useEffect` + `useState`).
- **OR feature-gate** the hook behind a runtime check (acceptable for opt-in features used only by React 18+ Code Pages).
- **OR document the surface as React-18-only** and prevent React-16 consumers (PCFs) from importing it.

Run the build matrix to confirm:
```powershell
pushd src\client\shared\Spaarke.UI.Components; npm run build; popd
pushd src\client\pcf; npm run build; popd  # All 13 PCFs
# Plus each Code Page that consumes @spaarke/ui-components — spot-check 3:
pushd src\solutions\SmartTodo; npm run build; popd
pushd src\solutions\SpaarkeAi; npm run build; popd
pushd src\solutions\LegalWorkspace; npm run build; popd
```

**Commit**:
```
fix(ui-components): declare @types/react as peerDep (multi-major)

Allows React 16 PCF consumers and React 18+ Code Page consumers to share
the same lib without duplicate ReactElement type identities (bucket D from
chat-routing-redesign-r1 audit). Shared lib audited for React 18+ APIs;
none in load-bearing components.
```

### 4.4 PR-D2 — Re-pin VisualHost to React 16

**File** — one PCF `package.json`:

| File | Change `react` | Change `@types/react` |
|---|---|---|
| [`src/client/pcf/VisualHost/package.json`](../../../../src/client/pcf/VisualHost/package.json) | `^18.2.0` → `^16.14.0` | `^18.2.0` → `^16.14.60` |

```powershell
pushd src\client\pcf\VisualHost
Remove-Item -Recurse -Force node_modules
Remove-Item -Force package-lock.json
npm install --legacy-peer-deps --no-audit --no-fund
npm run build:prod  # MUST be build:prod, not build (per CLAUDE.md §11 / FAILURE-MODES.md#AP-1)
popd
```

Check the bundle.js size before/after — should NOT grow significantly. If bundle grows >20%, investigate (could mean React 16 isn't being properly externalized via platform-library).

**Re-verify VisualHost source for React 18-only API usage** (defensive — already audited 2026-06-22, but re-run before merging the PR in case any new code lands):
```powershell
Select-String -Path "src\client\pcf\VisualHost\control\**\*.ts","src\client\pcf\VisualHost\control\**\*.tsx" -Pattern "\b(useId|useTransition|useDeferredValue|useSyncExternalStore|useInsertionEffect|createRoot|hydrateRoot|startTransition)\b"
```
Expected: zero hits (confirmed 2026-06-22 grep returned empty). Any hit → STOP and reconcile.

Deploy VisualHost to spaarkedev1 and smoke-test (chart rendering, drill-through, MetricCard matrix layouts — full feature surface). Bump manifest version (e.g., 1.4.16 → 1.4.17).

**Commit**:
```
fix(pcf): re-pin VisualHost to React 16 type pins (match platform-library runtime, ADR-022)

Closes bucket D from chat-routing-redesign-r1 audit. VisualHost was on
@types/react ^18.2.0 while the platform-library runtime is React 16.14.0.
This drift produced duplicate-ReactElement TS2322 errors when consuming
@spaarke/ui-components from VisualHost. Re-pin to ^16.14.x for type/runtime
parity. Shared lib's peerDep range (PR-D1) accepts both 16 and 18+ consumers.

Source audited for React 18-only APIs (2026-06-22, re-verified pre-merge):
zero hits. Feature surface preserved.

(DrillThroughWorkspace + SpeDocumentViewer were initially also slated for re-pin
but were instead deleted in Part B as orphans — owner confirmed unused 2026-06-22.)
```

### 4.5 Test matrix after PR-D2

| Workspace | Build | Smoke test |
|---|---|---|
| `Spaarke.UI.Components` | `npm run build` | n/a (lib) |
| 9 React-16 PCFs | `npm run build:prod` each | spot-check 2-3 in spaarkedev1 |
| VisualHost (newly aligned) | `npm run build:prod` | smoke in spaarkedev1 (charts, drill-through, MetricCard, your recent enhancements) |
| 3 React 18+ Code Pages (spot-check) | `npm run build` each | smoke each as deployed web resource |

---

## 5. Sequencing + acceptance gates

```
Day 1-2:  PR 1 (Part B source deletion: UQC + DTW + SDV) — owner review + merge
Day 3:    Pre-flight §1.1 + §1.2 in spaarkedev1
Day 4:    Part C cleanup session in spaarkedev1 (4-6 hours, one focused block — 11 customcontrols + canvas apps)
Day 4-10: 1-week soak — monitor for regressions
Day 11:   PR-D1 (shared lib peerDep)
Day 12:   PR-D2 (VisualHost re-pin) — deploy, smoke test
Day 13:   Part C cleanup in spaarkedev2 (replay)
Day 14+:  Promote to prod env per the standard release procedure
```

Acceptance gate to call this work complete:

- [ ] UQC + DrillThroughWorkspace + SpeDocumentViewer folders absent from `main`
- [ ] 11 orphan `customcontrol` records absent from spaarkedev1 (Dataverse MCP query returns empty)
- [ ] 4–6 orphan canvas apps absent from spaarkedev1
- [ ] VisualHost re-pinned to React 16 + rebuild + deploy + smoke-test pass (charts, drill-through, MetricCard, recent enhancements)
- [ ] Shared lib peerDep range accepts both React 16 and React 19 consumers (verified by build matrix)
- [ ] No new TS2322 / TS2305 / TS2307 errors in any of the 11 PCFs or 4 code-pages
- [ ] §1.1 baseline solution backup ZIPs filed in `projects/ai-procedure-quality-r1/notes/inventory/backups-2026-06-22/`
- [ ] `pcf-deployment-inventory-2026-06-22.md` updated with "as of YYYY-MM-DD: cleanup complete" footnote
- [ ] `dataverse-cleanup-log-2026-06-22.md` exists with per-control entries

---

## 6. Risk register

| Risk | Likelihood | Severity | Mitigation |
|---|---|---|---|
| §1.2 checks miss a form reference; deleting PCF breaks a form | LOW | HIGH | Always do PAC CLI customizations.xml grep, not just MCP. Test in spaarkedev1 first; 7-day soak. Recovery via §3.5.2 (baseline backup re-import). |
| **SpeDocumentViewer was bound to forms we didn't find** | MED | HIGH | §1.2 check 2 (FormXML grep) is MANDATORY on SDV — flagged as "do not skip" in the §3.1 cleanup-targets table. Recovery via §3.5.2 if the grep missed something. |
| Canvas app is referenced by an unknown ribbon / business process | LOW | MED | Grep all PowerShell scripts + JS resource code for the canvas app name before deletion. |
| Shared lib uses a React 18+ API that breaks React 16 PCFs after PR-D1 | MED | MED | PR-D1 §4.3 audit step is mandatory before merge. Any hit → replace or feature-gate. |
| **Re-pinning VisualHost breaks recent enhancements** | LOW | HIGH | Owner-verified 2026-06-22: VisualHost source has zero React 18-only API usage. §4.4 re-verifies pre-merge. Smoke-test covers chart/drill-through/MetricCard. |
| PlaybookBuilderHost is more "live" than the inventory thinks | MED | HIGH | Explicitly OUT of scope (§3.1). Separate owner triage required. |
| Production env (not spaarkedev1) has different ribbon JS that still calls UQC's canvas | MED | HIGH | Spaarkedev2 / prod replay only AFTER 7-day soak in spaarkedev1. Each env independently runs §1.2 checks. |
| **Baseline backup ZIPs were skipped in §1.1** | LOW | HIGH | Acceptance gate (§5) now requires the backup ZIPs to exist in `backups-2026-06-22/`. §3.5.4 documents the "no backup" recovery path so the impact is bounded but the cost is steep — DO NOT skip §1.1. |

---

## 7. Formalization

This document is a procedure draft. To formalize as a project (recommended given the 3-5 day scope and the production blast radius), spin up a new project folder following the standard pattern:

```
projects/pcf-orphan-cleanup-r1/
├── README.md           # Project overview
├── spec.md             # FRs / NFRs / Success Criteria
├── design.md           # This document, refactored as design.md
├── CLAUDE.md           # Project-scoped AI context
├── current-task.md     # Active task pointer
└── tasks/              # POML decomposition (suggest 7 tasks)
    ├── 001-preflight-backups.poml
    ├── 002-source-deletion-uqc-dtw-sdv.poml
    ├── 003-dataverse-cleanup-spaarkedev1.poml
    ├── 004-shared-lib-peerdep-fix.poml
    ├── 005-visualhost-react16-realign.poml
    ├── 006-dataverse-cleanup-spaarkedev2.poml
    └── 007-cleanup-log-finalize.poml
```

The procedure-vs-project decision is the owner's call. The cleanup itself can run from this doc alone if preferred (the steps are concrete enough); the project pattern is overhead only worth paying if multiple contributors will share the work or if the spaarkedev1/spaarkedev2/prod replay needs to be sequenced over multiple weeks.

---

## 8. Cross-references

- [pcf-deployment-inventory-2026-06-22.md](pcf-deployment-inventory-2026-06-22.md) — the data backing this procedure
- [pcf-bundle-sizes.md](pcf-bundle-sizes.md) — 2026-05-14 bundle-size baseline (still applies to remaining 13 PCFs post-cleanup; UQC entry becomes obsolete)
- [`.claude/adr/ADR-022-pcf-platform-libraries.md`](../../../../.claude/adr/ADR-022-pcf-platform-libraries.md) — binding constraint for "React 16 APIs only" in PCFs
- [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) — full PCF redeploy procedure (referenced from §4.4 / §3.2 step 4)
- [`.claude/FAILURE-MODES.md#AP-1`](../../../../.claude/FAILURE-MODES.md) — `build:prod` not `build` (referenced from §4.4)
