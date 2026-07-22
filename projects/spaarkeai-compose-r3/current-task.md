# Current Task State — spaarkeai-compose-r3

> **Last Updated**: 2026-07-22 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Deep architecture record: [`notes/compose-clean-slate-architecture.md`](notes/compose-clean-slate-architecture.md). Phase-1 design: [`notes/design-server-side-docx-html-conversion.md`](notes/design-server-side-docx-html-conversion.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | UAT-driven fixes + strategic architecture discussion (NOT a POML task run) |
| **Deployed & live** | Phase-1 mammoth removal (BFF) + Bug A fix (SpaarkeAi) on **spaarkedev1** |
| **Status** | Awaiting user **Bug A re-UAT**; strategic framing just re-grounded |
| **Next Action** | Write the **FOCUSED, NARROW plan** for the byte-preserving + ID-anchored OOXML↔TipTap mapping fix (drop the WOPI/commercial/market sprawl), and treat **Bug A re-UAT** (AI redline at an interior location must save) as the first checkpoint. |

### The agreed problem framing (re-grounded 2026-07-22 — DO NOT re-drift)
- **Feature set is RIGHT** — this was never a feature problem.
- **TipTap provides the editing** (typing/marks/selection/undo) and works fine — **not at fault.**
- **We are NOT building Word** — "full Word fidelity" is a **non-goal / red herring.**
- **The problem is PRESERVATION fidelity** (don't lose/corrupt the original on parts we didn't edit; map accurately so AI/edits land right) — the defect lives in **OUR OOXML↔TipTap translation + text-search save**, not TipTap.
- **The fix is narrow:** server keeps the original `.docx` as source of truth, **only reinterprets edited spans (byte-preserving)**, **anchors by stable ID (never text-search)**. TipTap schema stays "the basics"; fidelity is preserved **server-side**.

### Files Modified This Session
- Server (new): `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjection.cs`, `ComposeDocxProjectionBuilder.cs`
- Server (mod): `ComposeService.cs`, `IComposeService.cs`, `Infrastructure/DI/ComposeModule.cs`, `Api/ComposeEndpoints.cs`
- Client (mod): `Spaarke.Compose.Components/src/types/compose-contracts.ts`, `widgets/ComposeWorkspace.tsx`, `ComposeWorkspace.types.ts`, `ComposeEditor.tsx`
- Client (new test): `widgets/ComposeEditor.projection.test.tsx`
- Tests (new): `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` (12); mod: `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs`
- Notes (new): `notes/compose-clean-slate-architecture.md`, `notes/design-server-side-docx-html-conversion.md` (+ user's `CLAUDEREVIEW-*.md`, `GPT-*.md`)

### Critical Context
Two save-failure ROOT CAUSES were fixed this session; a third is open. All three are the SAME theme: the OOXML↔editor mapping was lossy + text-anchored. Phase-1 (server single-walk projection, byte-preserving paraId map, no mammoth) fixed the paraId-drift class. Bug A fixed a leftover text-search path for AI redlines. Bug B (eTag/concurrency) is unfixed and is the same family as the co-authoring-vs-programmatic-write question.

---

## Full State (Detailed)

### Deploy state (spaarkedev1)
- **BFF `spaarke-bff-dev`**: at the **58-merge base `0e42024fa`** + Phase-1 projection. Deployed via `Deploy-BffApi.ps1` — 47.11 MB, 4 files SHA-256-verified, health passed.
- **SpaarkeAi `sprk_spaarkeai`**: at **`14e76ef54`** (58-merge base + Bug A fix). Built from a **detached checkout of `14e76ef54`** and deployed — because tip-of-master has a build break (see Notifications below). Published to Dataverse.
- **They are a consistent pair** (both at ~58-merge + the two compose fixes). The later 13 master commits are NOT in the deployed artifacts (other projects' work; their owners deploy).

### Git state
- Branch `work/spaarkeai-compose-r3`, HEAD = **`6505e60d3`** (merge; branch merged to master, main repo synced, worktree back on branch tip, clean tree).
- Key commits: **`74165c856`** Phase-1 mammoth removal · **`14e76ef54`** Bug A fix · merges `0e42024fa` (58) and later 13.
- Pushed + merged to master. (LFS note: pushing required `git lfs push origin <branch>` first — 5 sample-doc `.docx` objects were unpushed.)

### What was fixed
- **Phase 1 — mammoth removal (root cause of paraId drift).** New `ComposeDocxProjectionBuilder`: ONE server-side walk assigns each paragraph's `w14:paraId` AND emits its editor block from the SAME `Paragraph` instance (reference-keyed, never `map[index]`). `LoadAsync` returns a `Projection {status,canEdit,html,warnings}`; client mounts `projection.html` (paraId extension parses `data-paraid`), drops mammoth + `stampParaIds` on the stored-Load path; fail-closed. Mammoth retained ONLY as fallback for projection-less browse/transient mounts. 12 builder unit tests + 2 client mount tests; 480 BFF compose + 15 contract + 404 client green. Publish 45.75 MB (<60). Zero new deps.
- **Bug A — AI redline anchored-annotations double-sent via text-search.** `anchoredAnnotationsToDocxAnnotations` emits insertion/deletion suggestions as text-searched track-changes; round-4b removed the *marks* source but not this one. Those redlines already persist position-based → sending them again via `DocxAnnotationWriter.LocateTarget` 422'd at interior locations (tab/whitespace drift). Fix: at the save site (`ComposeWorkspace.tsx`), keep ONLY `DocxTrackChangeKind.Comment` on the annotation path. Push-to-Word still sends redlines (intended). 69/69 workspace+word-shuttle tests green. Client-only; BFF unchanged.

### Open items
1. **Bug A re-UAT** (awaiting user): open a doc → AI edit at an **interior** location → Save should succeed. First checkpoint.
2. **Bug B — eTag mismatch after create-on-save** (NOT fixed): `ReplaceFileContentAsUserAsync` precondition goes stale when a follow-up write advances the item's eTag. Pre-existing, intermittent. **Same family as the co-authoring / Office-lock vs. programmatic-OOXML-write (HTTP 423) concurrency question** — needs a defined protocol; carry into any spike.
3. **Notifications build break (cross-project):** `spaarke-notification-spine-r1` (commit `ba705a2af`) added `@spaarke/notifications` (`file:` dep) + `SpaarkeAi/src/services/notificationsBootstrap.ts`, but the dep isn't `npm install`ed on tip-of-master → **SpaarkeAi Vite build fails from tip** (`tsc-surface-gate`). Blocks the next SpaarkeAi deploy from tip until their dep is wired (likely just `npm install` + lib build). Flag to that project.

### Strategic architecture discussion (this session)
- Wrote **`notes/compose-clean-slate-architecture.md`** — a from-scratch derivation. Core (invariants I-1…I-7) is sound: **one authoritative model = the real OOXML, server-authoritative, stable addressing, edits as operations (not text-search), one byte-author, client is a view.**
- **Re-grounding correction (IMPORTANT):** I over-rotated into WOPI/Office-delegation, commercial SDKs (Syncfusion pricing = five-figure/quote-only), "full Word fidelity" trilemma — the user corrected that **"full Word" was never a goal** and we were **regressing in framing**. Those are RED HERRINGS. The WOPI/Office-launch material is an **optional convenience Spaarke already has** (`SpeDocumentViewer` → open in Office web/desktop), NOT the fix. Doc §4.0 now records this correction.
- Research memos produced (accurate, cited): browser-docx-editing market patterns; legal-AI comparison (Harvey/Legora delegate heavy edit to Office); SuperDoc (AGPL/commercial), Eigenpal `docx-editor` (Apache-2.0, ProseMirror, **byte-preserving "only reinterpret what you edit" invariant** — the reference for our fix, but archived); SPE+WOPI+Office GA facts; commercial SDK pricing (five-figure/quote-only — **off the table** per user: no per-seat/commercial component).

### IMMEDIATE NEXT (do on resume)
1. If user confirms the narrow framing: **write the focused plan** — byte-preserving + ID-anchored OOXML↔TipTap mapping fix, scoped to our translation/save layer only (no WOPI, no commercial, no editor rebuild, no feature change). Prove on the CIPO doc.
2. **Confirm Bug A re-UAT** result as the first checkpoint.
3. Bug B / concurrency protocol as a follow-up (not blocking).
