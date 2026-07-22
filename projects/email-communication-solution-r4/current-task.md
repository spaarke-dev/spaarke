# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-22 (deferred-items batch worked — item 2 committed on worktree; items 1 & 3 found already done; NOT merged, NOT deployed)
> **Recovery**: Read "Quick Recovery" first.

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
