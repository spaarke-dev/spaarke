# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-24 (context-handoff — composer UAT arc v1.3.10→v1.3.14 MERGED to master @ `85126811c`; v1.3.14 imported by owner)
> **Recovery**: Read "Quick Recovery" first.

## Quick Recovery (READ THIS FIRST) — 2026-07-24

| Field | Value |
|-------|-------|
| **Active work** | EmailComposer redesign + reply/forward behavior, driven by owner UAT (rounds 6→10). **All MERGED to master** and worktree fully synced. |
| **Git state** | branch `work/email-communication-solution-r4` == `origin/master` == main-repo master == **`85126811c`**; 0 ahead / 0 behind / clean. Everything pushed + merged. |
| **Latest PCF** | **CommunicationActions v1.3.14** — **imported by owner** (confirmed). ZIP: `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.3.14.zip` (bundle sha `30a1867e4d13`). |
| **Deploy state** | v1.3.14 PCF **imported**. BFF deployed at the `a58c0b5cc`-era (no BFF code changed since; master advanced only in TS shared libs). SpaarkeAi code page deployed at `a58c0b5cc` — master since advanced (notification-spine TS changes flow to SpaarkeAi via aliased source) → **could be rebuilt/redeployed from `85126811c` if those matter**. Other PCFs (CommunicationConversationPanel v1.5.0) owner-imported earlier. |
| **Next Action** | Await owner UAT of v1.3.14 (reply/forward quoted original + inherited "Related to"). **Still owed**: `code-review` + `adr-check` on the composer redesign (long-standing). Optional: re-deploy BFF + SpaarkeAi from new master; fast-forward the other active worktrees (compose-r4, messaging-r3) which are now behind master. |

### Composer UAT arc — rounds 6→10 (all merged to master; PCF v1.3.10 → v1.3.14)
- **v1.3.10** `f018b6741` — UAT r6: `Dialog modalType="alert"` stops the composer auto-closing when the OOB record-lookup pane takes focus.
- **v1.3.11** `ab0ff19fe` — UAT r7: `modalType="non-modal"` (no backdrop scrim) so the OOB lookup pane is clickable. *(Fluent modal/alert = browser top-layer, can't go below; non-modal honors z-index. Owner: sub-15" overlap is WON'T-FIX — see memory `ui-resolution-support-baseline`.)*
- **v1.3.12** `c76f9016e` — UAT r8: forward-mode "add record" now inserts into the body. Root cause = shared `RichTextEditor` `InitialContentPlugin` initialized content ONCE; made it **properly controlled** (re-syncs on external value change, skips typing echoes). Also confirmed the send-502 was a transient post-deploy cold-start (App Insights: zero send exceptions).
- **v1.3.13** `05e9c3d38` — UAT r9 (9-item redesign): actions moved INTO the RTF toolbar via new **`RichTextEditor.toolbarSlot`** (context-agnostic, additive) — paperclip menu (Add files / Link documents) + divider + record-search (Document excluded) + **connector** (`Connector20Regular`); record link inserts **at cursor**; Attachments now display-only + **default collapsed**; new **"Related to"** collapsible section (AssociationChips); section labels → Segoe 14px semibold +4px; To/Cc/Bcc **label box** opens OOB people picker (contact+systemuser) additively; connector `onAddRelationship` = OOB lookup across regarding types (**Option B** — persists via the send `associations` payload, correct since the composer creates a NEW communication). Reducer: new `ADD_ASSOCIATION`. `TimelineComposeBox` migrated to the display-only AttachmentList.
- **v1.3.14** `5c7e678d4` — UAT r10: reply/reply-all/forward **load the quoted original** (From/Sent/Subject header + `<blockquote>`; no `<hr>` — editor has no HR node); reply/reply-all leave 2 blank lines above; **inherit the parent's filed "Related to"** (PCF reads each `sprk_regarding*` lookup + annotations → composer `associations` → written to child on send).

### Verification (each round)
- Shared-lib: **209 EmailComposer/RichTextEditor jest green + tsc 0 errors**; PCF `composerPrefill` **6 green**. Every PCF ZIP: version in all 5 locations + embedded bundle SHA verified. 3 pre-existing `TimelineComposeBox` recipient-test failures are UNRELATED (stale since a prior BodyEditor toggle redesign; fail on committed baseline).

### Blast radius now on master (watch at deploy)
- Shared `RichTextEditor` (`toolbarSlot` + controlled-sync) + EmailComposer redesign flow to **SpaarkeAi, code pages, TimelineComposeBox** on their next build. All additive/verified, but worth a light regression glance when those surfaces redeploy.

### Prior arc detail (v1.3.4 → v1.3.9, UAT rounds 1→5) — superseded by the above, kept below for history

### Composer redesign — commit trail (all on worktree branch, NOT merged)
- `d94781eaa` provenance quote-escape (items 1-3 deferred batch: item 2 done; 1 & 3 already done) + `35cc41127` checkpoint.
- `e3609af53` SendEmailDialog → 720×70vh · `cb5249a00` CommunicationActions 720×70vh v1.3.2 · `4e3eecf51` v1.3.3 cache-bust.
- `535ebe5cf` **EmailComposer redesign to mockup** (v1.3.4): attachments-above-body, inline To/Cc labels, subject placeholder, split Send (send-from in caret), Bcc toggle, header X.
- `d516a2f07` **UAT r2** (v1.3.5): landscape size, chips-in-field, toggle-top-right, no Message label, resizeable, Cancel-left.
- `daa0c3bad` **UAT r3** (v1.3.6): fixed-layout dialog (pinned header/footer), attachment collapsible + header toolbar + no source pills + files-no-checkbox/docs-Attach-Link + 1-line summary, single-icon rich/plain toggle.
- `2d01ed6c1` **UAT r4** (v1.3.7): fill-container attempt + single-scroll + plain-text HTML-strip + right-align count + (first) document-lookup overlay modal.
- `d2c7b5002` **UAT r5** (v1.3.8): **fixed left-clip** via `box-sizing:border-box` on composer base; **replaced doc-only overlay with RECORD lookup (RegardingResolver pattern)** — search icon → entity menu (Document/Matter/Project/Event/Communication/WorkAssignment/Invoice/Budget/Analysis/Organization/Contact) → host `Xrm.Utility.lookupObjects`; Document→attach (Attach/Link), other record→body link. Removed `DocumentLookupDialog` + `onSearchDocuments`; added `recordLookupCatalog` + `onLookupRecord` (host owns Xrm + URL, ADR-012). `a08e3f441` v1.3.9 fresh rebuild.

### Composer redesign — files touched (shared lib `@spaarke/ui-components`, all committed)
- `EmailComposer.tsx` (layout restructure, box-sizing, handleRecordPicked, escapeHtml), `EmailComposer.types.ts` (IRecordLookupTarget/IPickedRecord + recordLookupCatalog/onLookupRecord props), `subcomponents/RecipientField.tsx` (inline boxed labels + chips-in-field), `BodyEditor.tsx` (single-icon toggle, htmlToPlainText, single scroll), `AttachmentList.tsx` (collapsible + header toolbar + search-icon record-lookup menu + files/docs checkbox rules + 1-line summary), `ComposerActionBar.tsx` (Cancel-left + split Send w/ send-from menu), `wrappers/SendEmailPage.tsx` + `SendEmailDialog.tsx` (720→1040×72vh, prop forwarding), `EmailComposer/index.ts` (barrel). Deleted `subcomponents/DocumentLookupDialog.tsx`.
- PCF: `src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx` (dialogSurface 1040×72vh, `handleLookupRecord` via window.Xrm, `RECORD_LOOKUP_CATALOG`) + `authInit`/version files.
- **142 EmailComposer jest tests green; shared-lib `tsc --noEmit` clean** at HEAD (verified each round).

### Composer redesign — key decisions + open follow-ups
- Owner chose **wizard = "Full match (chips + rich text)"** across the 8 wizard email surfaces (shared `EmailStep` feeds create-record wizards via `SendEmailFollowOnStep`, DocumentUploadWizard, DocumentEmailWizard) — **NOT YET STARTED** (deferred until composer confirmed).
- RTF toolbar left AS-IS (owner: don't adjust the shared RichTextEditor); circle-down-arrow scroll = the shared editor's own scroll.
- Deferred/noted: documents attached via the picker show `0 B` (OOB lookup returns only id+name — could enrich with a `sprk_filesize` fetch); the mockup's header "expand/pop-out" icon left out (needs a host handler).
- `code-review` + `adr-check` on the redesign **still owed** before final merge.
- The composer redesign flows to OTHER surfaces (SpaarkeAi Assistant, LegalWorkspace FilePreview, SummarizeFilesWizard) via the shared lib — they need their own solution rebuilds at deploy time (other projects' surfaces).

---


## Deferred-items batch status (2026-07-22, on `work/email-communication-solution-r4` branch — commit `d94781eaa`, NOT pushed/merged/deployed)

- **Item 1 — FR-03 `sprk_servicerequest` as regarding target**: ✅ **ALREADY COMPLETE** (task 003, prior wave). Verified: `CommunicationService.RegardingLookupMap`:1819, `RegardingFieldMap.All`:18, `IncomingAssociationResolver.GetPrimaryNameField`:499 (→`sprk_name`), `docs/data-model/sprk_servicerequest.md`. `TODO_REGARDING_CATALOG` deliberately NOT touched (it's a `sprk_todo` catalog using `sprk_regardingcontact`, not a communication catalog — documented in the doc §4.1). No number-field convention for servicerequest → correctly absent from `GetReferenceNumberField`. **No work needed.**
- **Item 2 — escape embedded `"` in name-match provenance**: ✅ **DONE + committed `d94781eaa`**. New shared `RungProvenanceFormat.EscapeValue` (server-only quote→apostrophe neutralize) applied to the user-controlled `name`/`number` in BOTH `RecordNameMatchRung` (3.5) and `ContactNameMatchRung` (3.6). Keeps the shipped PCF `provenance.ts` parser (`key="([^"]*)"`, no unescape) uncorrupted by a quote in a record/contact name — **no PCF rebundle needed**. Real display name still on `Target.Name`. +1 regression test; 571 Communication tests green; build 0 errors; code-review + adr-check CLEAN.
- **Item 3 — `_recordTypeRefCache` → `ConcurrentDictionary`**: ✅ **ALREADY DONE** (task 018; `IncomingAssociationResolver.cs`:346 is already `ConcurrentDictionary` with the race-condition comment). **No work needed.**

**HELD for owner input** (unchanged): (a) FR-21 wizard email-step migration — needs the "standard composer form" decision. (b) #7 split-index test relocate — recommend it rides task-050 KEEP-path reorg.

**Deploy note**: master's undeployed BFF (124+132) + this worktree's `d94781eaa` (item 2) are all BFF — one `/bff-deploy` covers them once merged. PCF imports unchanged (Connections v1.6.1 / Actions v1.3.1 / Attachments v1.3.0). Item 2 needs NO PCF change.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | **ALL UAT rounds (W9–W13 / R1–R5) COMPLETE + MERGED TO MASTER** (`f077d89b7`; main repo synced). **NOT deployed** — owner directive 2026-07-22: merge only, no deploy yet. Only open task: **090 wrap-up** (HELD until deploy/UAT closes). |
| **Active (NEXT — owner-approved 2026-07-22)** | Do items 1–3, commit, then merge (NO deploy): **(1) `sprk_servicerequest` as a regarding target (FR-03)** — author schema doc + add `sprk_regardingservicerequest` lookup + `RegardingLookupMap` + `TODO_REGARDING_CATALOG` + `RegardingFieldPriority` entries (extend, don't add a new mechanism — §11). **(2) escape embedded `"` in RecordNameMatch provenance** payload (BFF: `RecordNameMatchRung` — unescaped `"` in matched text breaks the provenance JSON the review UI parses). **(3) `_recordTypeRefCache` → `ConcurrentDictionary`** in `IncomingAssociationResolver` (plain Dictionary on a singleton; concurrent inbound can throw/torn-read — best-effort so low sev, but harden). |
| **Held for owner input** | (a) **FR-21 wizard email-step migration** — shared `SendEmailStep` (CreateProject/Event/Todo/WorkAssignment + CreateMatter fork), SummarizeFilesDialog, LegalWorkspace FilePreviewDialog, DocumentEmailWizard → canonical composer. PENDING owner's "standard composer form" discussion. Also where the recipient-lookup + reply/forward-inheritance fixes would extend to those wizard hosts. (b) **#7 split-index test relocate** — recommend it rides task-050 test-path reorg (they're boundary-mocked *unit* rung tests; `tests/integration/regression/` is for WebApplicationFactory integration per ADR-038). (c) Connections lookup icon opens the rich modal (could swap to a RegardingResolver-style record-picker). |
| **DEPLOY when owner ready (NOT done)** | Master carries UNDEPLOYED **BFF** changes: **124** (reply/forward regarding inheritance) + **132** (inbound denorm name/number fix). Deploy via `/bff-deploy` (`pwsh -File scripts/Deploy-BffApi.ps1`, hash-verify 4 files, healthz). Import latest PCF ZIPs: Connections **v1.6.1** `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.6.1.zip`; Actions **v1.3.1** `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.3.1.zip` (bin/ gitignored — pack via `Solution/pack.ps1` from the Solution dir); Attachments **v1.3.0**. Import unmanaged, publisher Spaarke/sprk_, publish-all, Ctrl+Shift+R. NOTE: existing INBOUND records keep the bad denorm until re-processed; new ones correct after 132 deploys. |
| **Deferred / re-homed (owner-decided 2026-07-22)** | RI auto-actions + create-from-email full **create-and-link** → **`spaarke-notification-spine-r1`** (email-r4 re-homed W5 = its proving producer). Outbound engine auto-association = defer/remove (already a documented no-op). Outlook add-in (W7/FR-25; Office auth filters stubbed `// TODO: Task 033`) → separate focused project. Auth popup (`@spaarke/auth` `ssoSilent`→`acquireTokenPopup`, MSAL iframe timeout in MDA sandbox) = **handled elsewhere**. Email triage / Work IQ / inbox-thread UI / override-reason learning loop = out of scope. 2 pre-existing shared-lib RichFilePreview test failures (contentEditable keydown; Tags "NDA" dup) = **`spaarke-ai-platform-unification` (#345)**, not ours. |
| **R1–R5 commit trail (all on master)** | R5: 131 `04e0e3741` (Connections↔RegardingResolver parity v1.6.0) → 132 `f0be7cf9c` (inbound denorm fix + card-from-filed-assoc, Connections v1.6.1, BFF). Recipient hotfix `9cb8af79c` (Actions v1.3.1). Two master syncs `07f97b99c` + `785b5f888`/`f077d89b7`. |
| **W11 PCF ZIPs to import (bundles verified inside each ZIP)** | Attachments **v1.2.0** `src/client/pcf/CommunicationAttachments/Solution/bin/CommunicationAttachmentsSolution_v1.2.0.zip` · Connections **v1.4.0** `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.4.0.zip` · Actions **v1.2.0** `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.2.0.zip`. Import unmanaged, publisher Spaarke/sprk_, publish-all; hard-refresh (Ctrl+Shift+R). |
| **W11 done** | 111 `5d999e6a0` (Attachments: shared-lib showTitle double-header fix + nav restored, green/red SPE upload icon, ATTACHMENTS title, v1.2.0) · 112 `eee9a6f64` (Connections: RELATED RECORDS title, full-modal + Save footer, v1.4.0) · 113 `f802abee6` (Actions: 20x20 right-aligned icons, create-as-modal via launchCreate seam, v1.2.0). |
| **W11 interpretation flags for owner UAT** | (112 B11-4) the "open" icon WAS the modal-opener; the card header row is now the click-to-open (confirm OK). (112 B11-5) on-form collapsed-card match-reason kept (only modal-row subtitles removed) — easy to strip if wanted. (111 A11-2) SPE status uses Cloud✓/Cloud✗ green/red (no SharePoint brand glyph in Fluent icons). (113 C11-3) OOB navigateTo target:2 behind launchCreate seam (custom Fluent dialog swap = edit one file). |
| **Known pre-existing reds (NOT W11 — from the 62-commit master merge)** | 2 shared-lib RichFilePreview tests fail (contentEditable keydown; Tags "NDA" duplicate) — another project's change; unrelated to the double-header fix. |
| **Done (W10, branch commits)** | 101 `7c51d17e3` (Attachments PCF nav+styling+UI-DESIGN-STANDARDS.md, v1.1.0) · 102 `22db1909f` (ContactNameMatchRung, Suggest-only) + `863bc4e57` (§6.5 Path A mapper trace fix — surface BOTH named contacts) · 105 `085c4ac9e` (Connections group-by-action UX, v1.3.0) · 103 `c55838518` (composer contact autocomplete via Xrm.WebApi, CommunicationActions v1.1.2) · 104 `423e9c7b8` (Reply/Forward attach+link, CommunicationActions v1.1.3). Shared-lib @spaarke/ui-components tsc build clean at HEAD. |
| **Done (W9)** | 091 #7 `d6d03f6cb`; 092 #4/#5 `070e6e663` (+M2 hardening); 093 #2 PCF `2d56cf8e0`. Synced origin/master `dd57cb659`. Merged PR #663 `9000fe2c3`. Deployed BFF (hash-verified). Verified in prod (#7 body-ref → Ambiguous; #2 PCF imports after XSD fix `f35d29077`). |
| **Deploy W10 (when owner ready)** | Merge origin/master → `/merge-to-master` (auto-merge PR) → deploy BFF from worktree (for 102 ContactNameMatchRung; publish 50.68 MB, under 60 ceiling). PCF imports (owner maker): CommunicationAttachments **v1.1.0**, CommunicationConnections **v1.3.0** (ZIPs tracked in `Solution/bin/`), CommunicationActions **v1.1.3** (bin/ gitignored per that control — pack via `Solution/pack.ps1`). |
| **Owner UAT re-verify W10** | A1/A2: attachment preview prev/next + styling. B1: email body naming 2 existing contacts → BOTH surface as Suggested (user picks primary). C1: Connections modal grouped Needs-decision/Filed/Suggested with plain-language actions. D3: To/CC/BCC contact autocomplete. D1/D2: Reply/Forward carry source attachments (Forward auto-includes; Reply opt-in). Plus still-open W9 owner items: #4 open .eml, #5+webhook-KV via D-1 fresh email, Tier 2/3 PCF UI, B-4/B-5, H-8. |
| **Pre-existing reds — FIXED** (`55dd98f85`) | (1) `CommunicationConnections/provenance.test.ts` stale test replaced with 2 written-precedence tests (suite 28/28 green). (2) `CommunicationPage` code-page `ConnectionsEditor.tsx` — removed 5 unused imports + griffel `paddingBlock` token fix; `build:prod` now succeeds (103's EmailComposerSlot verifies in this host too). |
| **Open decisions** | Cert 170c98e1 pfx in %TEMP% → KV or leave (owner). Whether wizard compose flows / CommunicationMessageActions warrant the same contact typeahead (103 note) + SPE external-share links for 104 attachment links (deferred, code note). |
| **Held** | Task 090 wrap-up until W10 UAT closes. |

### W9 remediation tasks (POMLs + decisions locked)
- **091** #7 — Suggest-only (body refs never auto-file; email-1 → Ambiguous). ✅ implemented (gate pending).
- **092** #4 embed attachments in .eml + .eml display-name/content-type; #5 add missing on-demand `EnqueueDocumentAnalysisAsync` in `ArchiveExistingAsync` (after CommunicationService.cs:195). NOT a task-007 regression.
- **093** #2 new `CommunicationAttachments` PCF reusing `RichFilePreviewDialog` + BFF `/api/documents/{id}/preview-url`; dataset-bound; mirror CommunicationConnections host + SemanticSearchControl wiring.

### Key files / facts
- #7 fix: `RecordNameMatchRung.cs` extraction + exact `referenceNumbers` filter (RecordSearchService.cs:393 builds `referenceNumbers/any(r: r eq …)`).
- #4/#5: `CommunicationService.cs` `ArchiveToSpeAsync` (~line 1772, calls `GenerateEml` WITHOUT attachments), `ArchiveExistingAsync` (line 109; .eml Document created at 195 lacks profile enqueue), `EmlGenerationService.GenerateEml` (3-arg overload embeds attachments), `InferContentType` already exists (~line 2192). SPE upload via `UploadSessionManager.UploadSmallAsync` (no content-type set; Graph infers from `.eml` path).
- Attachments: `sprk_communicationattachment` (sprk_graphitemid, sprk_graphdriveid, sprk_document); `ArchiveExistingAttachmentsAsync` already profiles attachment Docs.

### UAT status (pre-remediation)
1A–1E + F all verified/PASS; archive fix (F-1/F-2) done; kill-switch mechanism verified + restored. Owner-gated remaining: D-1 fresh email, Tier 2/3 PCF UI, B-4/B-5, H-8. Defects log: `notes/UAT-DEFECTS.md`.

### Loose ends
- Renewed owner cert 170c98e1 pfx in %TEMP% (not KV; login lacked cert-import RBAC).
- Session commits on branch, not yet on origin/master. master ~25 ahead of branch (merge origin/master before any deploy/merge).

---

## Full State

**Branch**: `work/email-communication-solution-r4`. **Rigor**: FULL for all W9 tasks. **Model tier**: sonnet@xhigh (POML) — running on Opus main session.

**Commits this session**: 1E UAT (`407a0c07d`), defects log (`e886d725a`), W9 plan (`ae6941f6f`), 091 fix (`d6d03f6cb`).
