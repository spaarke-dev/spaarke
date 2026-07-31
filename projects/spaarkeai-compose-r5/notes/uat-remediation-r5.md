# R5 Post-Deploy UAT — Findings, Root Causes, Remediation Plan

> **Deployed**: 2026-07-30 (task 042, operator) to `sprk_spaarkeai` + `spaarke-bff-dev`.
> **UAT by**: operator (owner). 11 findings reported.
> **Triage owner**: this session. Root causes established via 3 read-only code investigations (Word-lock, redline/numbering, banner/profile).
> **Decision (owner, 2026-07-30)**: fix all 5 R5-owned defects now (Phase 5 wave 050–054); scope 1B (persist numbering) IN; capture out-of-scope items here (no GitHub issues).
> **Wrap-up gate**: 090 does NOT run until 050–054 done + re-verified (byte-diff + build) + re-deployed + re-UAT'd.

---

## R5-owned defects → remediation tasks

### 050 — #1A Redline not flowing to Word (SEV-1, genuine regression)
**Symptom**: user made a tracked edit in Compose on an imported NDA, saved; Word shows NO tracked changes.
**Root cause**: the dirty save was routed to `ComposeDocumentRenderer.SynthesizeDocument` (ContentModel path — authors PLAIN untracked runs by design, `ComposeDocumentRenderer.cs:102`,`:153-155`) instead of the tracked `ComposeShadowPatchEngine.Apply(trackChanges:true)` op-log path. Selection is by `request.ContentModel is not null` (`ComposeService.cs:1157-1160`, op-log block skipped at `:782`). Compounded by origin mis-stamp: `origin = ContentModel present ? Authored : Imported` (`ComposeService.cs:707`) durably marks the imported NDA `Authored` (`:1018`,`:1821`), so every later op-log save then takes `cleanApply=true`/`trackChanges:false` (`:718-727`,`:808`) — permanently clean.
**The engine tracked path itself is correct** (`ComposeShadowPatchEngine.cs:404-415`,`:2371-2392`,`:2677-2679`). This is a **routing** defect. The `notes/g2-clean-apply-decision.md` 2026-07-29 operator resolution already required imported/reopened saves to route back to op-log+clean; that is **not holding** for this flow.
**Fix**: (1) route imported/reopened dirty edits through the op-log (tracked) path, not the renderer; renderer stays for born-in-editor first-save only. (2) Fix the origin discriminant so an imported doc stays `Imported` (do not infer Authored from ContentModel presence). Re-verify byte-diff 24/24 + a NEW seam slice proving an imported-doc edit persists `w:ins`/`w:del` visible to Word.

### 051 — #1B Numbering renders all "1." in Word (fidelity; scoped IN by owner)
**Symptom**: Compose shows 1–6; Word shows all "1.".
**Root cause**: `NumberingComputationEngine` (`ComposeDocxProjectionBuilder.cs:1357`) computes correct numbers for the READ projection only (`data-computed-number`, `:1782-1810`; read-time-only `:1314-1322`) and never writes `numbering.xml`/`numPr`. Save keeps numbering byte-identical (`ComposeShadowPatchEngine.cs:52-56`), so Word renders the source doc's real (restarting) numbering. **NOT corruption** — a known R4.5 read-vs-write boundary.
**Fix (owner scoped in)**: author the computed numbering back into the saved OOXML so Word matches Compose — fidelity-sensitive; MUST NOT regress the R4.5 read path or byte-diff on docs whose numbering is already correct. Approach TBD in the task (candidate: on save, materialize computed sequence into `numPr`/a synthesized `numbering.xml` instance only for paragraphs whose display diverges). Escalate if it risks the R4.5 read engine.

### 052 — #10/#11 Word lock persists; no Unlock (SEV-1)
**Symptom**: "checked out or open in Word" save error persists after Word closed + browser refresh; no release affordance.
**Root cause**: it is a SharePoint/WOPI co-authoring lock created by Word-for-Web, server-side in SPE — Spaarke never checks the doc out and has NO release primitive. Save reads it live (Graph 423 on the driveItem PUT, `UploadSessionManager.cs:454-461`; typed `DocumentLockedByWordException` → 423 copy `ComposeEndpoints.cs:1489-1501`). Browser refresh only resets the React client; the lock is server-side. The Dataverse `DocumentCheckoutService` discard/sweeper only flips Dataverse flags — never releases the SPE/WOPI lock.
**Fix**: add a lock-release primitive to the SPE facade (driveItem `checkin`/`discardCheckout`); expose an endpoint next to Save; add a distinct **423 branch** in the client save handler (`ComposeWorkspace.tsx:1256-1261`, today falls through to generic) + an **"Unlock & Save"** action in `ComposeBannerStack.tsx` (mirror `useComposeCheckoutLifecycle.ts:308 forceCloseAndAcquire`). **Honest caveat**: Graph has no universal WOPI unlock; a pure transient co-auth lock may still need to time out — Unlock reliably clears a genuine checkout, and the button should message clearly when it cannot. Design refs: `projects/spaarkeai-compose-r4/tasks/052-http-423-lock-protocol.poml`, `projects/spaarkeai-compose-r2/notes/spikes/spike-7-checkout-collision.md`.

### 053 — #5 External-change banner never fires; no manual reload (SEV-2)
**Symptom**: Word-web edits landed in SPE but Compose showed no banner/changes; no manual refresh.
**Root cause**: the change-check trigger listens on `window` `focus` only (`ComposeWorkspace.tsx:970`); Compose runs embedded in an iframe, where returning from the Word tab fires `document.visibilitychange` (not wired) → check never runs. Webhook delivery leg unprovisioned in dev (`Compose:Webhook:NotificationUrl` unset → polling fallback; deferred), and no client push exists anyway. No unconditional reload button (banner Reload gated on dirty; toolbar sync icon is Refresh-Profile).
**Fix (client-only, small)**: add a `document.visibilitychange` listener beside the `focus` one; add an always-available "Reload from source" toolbar button that dispatches the existing `requestLoad` (pattern `ComposeWorkspace.tsx:2554-2562`) to pull latest SPE bytes.

### 054 — #9 Profile re-run button hidden (SEV-2)
**Symptom**: user can't find the manual refresh-profile button or the auto re-trigger.
**Root cause**: button gated until the doc has a `sprk_document` id (`ComposeWorkspace.tsx:2648`); transient mounts (Browse/Upload/AI-draft before first Save) have none → hidden. All legs otherwise wired (`ComposeService.RefreshProfileAsync:1353`, endpoint `ComposeEndpoints.cs:652`, toolbar `ComposeFormatToolbar.tsx:842`). Auto re-trigger is silent-by-design + eTag-storm-guarded.
**Fix (client-only, small)**: relax the gate so the button appears once `sprkDocumentId` exists (after first Save); optional lightweight "profiling…" status when the auto re-trigger fires.

---

## OUT of R5 scope — captured here per owner decision (no GitHub issues filed)

These belong to other projects (the operator was testing the whole integrated SpaarkeAi surface). Route to owning teams when convenient.

| # | Finding | Likely owner |
|---|---------|--------------|
| **2** | Assistant follow-on cards grayed out; "More"→Quick Start opens but the file is NOT loaded into the selected wizard | SpaarkeAi Assistant / analysis-hub-r1 (wizard file hand-off) |
| **3** | Context pane / Execution Trace does not list all steps incl. when revisions are written | SpaarkeAi Context pane / execution-trace surface |
| **4** | Browser refresh (soft) loses the Compose tab + document; Assistant history does not reload the conversation or the file | analysis-hub-r1 (session↔Analysis persistence / reopen-restore) — partial Compose-tab persistence borderline |
| **7** | "run NDA analysis on this file" → 2 min+ → "No issues found" (suspiciously long + generic) | analysis-hub-r1 / ai-advanced-capabilities-agreements-r1 (NDA capability) |
| **8** | Follow-on "Provide a clause-by-clause explanation" → "technical error processing the NDA structure"; expected an Agreement Analysis tab to open | analysis-hub-r1 / agreements (clause analyzer + surface launch) |

**#6 is not a bug** — "reopen in Word from Compose shows latest" confirms SPE holds the correct/latest bytes (corroborates #5's client-refresh gap).

---

## Execution
Per root CLAUDE.md §4, each 05x task runs via `task-execute` (FULL rigor — all are `.cs`/`.tsx`, bff-api/pcf tags, fidelity-critical save path). Serialization-heavy (shared `ComposeService.cs` / `ComposeWorkspace.tsx`) → run serially. `/conflict-check` before the BFF PR. Byte-diff 24/24 + BFF build + publish ≤60 MB re-verified after 050/051/052. Then re-deploy (operator) → re-UAT → 090 wrap-up.
