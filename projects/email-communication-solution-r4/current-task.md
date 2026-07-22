# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-20 (W10 complete — all 5 UAT-R2 tasks committed on branch, pre-deploy)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | W9+W10 shipped+deployed. **W11 (round 3) + W12 (round 4) MERGED TO MASTER** (`07f97b99c`; main repo synced). Deploy pending: BFF not yet redeployed for W12-124; PCFs not yet imported. |
| **Active (NEXT STEP)** | **Deploy BFF to `spaarke-bff-dev`** (for 124 reply/forward inheritance) + **import the 3 W12 PCF ZIPs** (paths below). Then UAT re-verify R3+R4. When UAT closes → **090 wrap-up** (HELD). |
| **Merge note (07f97b99c)** | Synced origin/master (26 commits) into worktree, then FF-merged to master. Resolved conflicts: RecipientField.tsx (kept stricter task-123 no-email guard over parallel R6-5 inline fix), de-duplicated `ILookupItem.email` + userLookup (both projects added same field), CommunicationActions version 1.3.0 over master's 1.1.4 + rebuilt bundle. Verified: BFF 0 err, 607 Communication + 138 EmailComposer tests green. |
| **HOTFIX (`9cb8af79c`, on branch — NOT yet merged/imported)** | Recipient picker: selecting a suggestion committed the typed DRAFT ("ralp"), not the email — root cause was mousedown blurring the Input → handleBlur committed the draft + hid the list before onClick could fire (task-123 resolution never ran in-browser). Fixed with preventDefault on the suggestion mousedown. Also: search now matches name OR email. Client-only (no BFF). **Import `CommunicationActionsSolution_v1.3.1.zip`** (`src/client/pcf/CommunicationActions/Solution/bin/`). Shared-lib RecipientField/userLookup + 27 tests green. |
| **W12 (round 4) done** | 121 `b6f497c02` (Connections spacing/matched-reason, v1.5.0) · 122 `c49a12bd4` (Attachments green/red icon + .eml opens modal, v1.3.0) · 123 `3c40ce076` (composer email-resolution, shared lib) · 124 `260f01336` (reply/forward regarding inheritance, **BFF** + client) · Actions rebundle `339eb27f6` (ships 123+124 client, v1.3.0). BFF builds clean at HEAD (0 err); publish 45.74 MB. |
| **W12 PCF ZIPs to import (supersede W11; bundles verified inside)** | Connections **v1.5.0** `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.5.0.zip` · Attachments **v1.3.0** `src/client/pcf/CommunicationAttachments/Solution/bin/CommunicationAttachmentsSolution_v1.3.0.zip` · Actions **v1.3.0** `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.3.0.zip` (Actions bin/ gitignored — pack via Solution/pack.ps1 from the Solution dir). Import unmanaged, publisher Spaarke/sprk_, publish-all, hard-refresh. |
| **W12 needs BFF deploy** | Task 124 changed `CommunicationService` (regarding inheritance) — deploy BFF to `spaarke-bff-dev` for reply/forward inheritance to work. |
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
