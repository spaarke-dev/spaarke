# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-20 (W10 complete — all 5 UAT-R2 tasks committed on branch, pre-deploy)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | W9 shipped. **W10 (UAT round 2) COMPLETE + DEPLOYED.** Merged to master (`010678595`, via PR #668 then FF); BFF deployed to `spaarke-bff-dev` (47.08 MB, 4/4 hash-verified, healthz OK — `ContactNameMatchRung` live). Main repo master synced. |
| **Active (NEXT STEP)** | **Owner: import 3 PCF solution ZIPs into Dataverse** (paths below), then UAT re-verify W10 items. When W10 UAT closes → run **090 wrap-up** (HELD). |
| **PCF ZIPs to import (rebuilt from post-merge HEAD; bundles verified)** | Attachments **v1.1.0** `src/client/pcf/CommunicationAttachments/Solution/bin/CommunicationAttachmentsSolution_v1.1.0.zip` · Connections **v1.3.0** `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.3.0.zip` · Actions **v1.1.3** `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.1.3.zip`. Import unmanaged, publisher Spaarke/sprk_, publish-all; hard-refresh (Ctrl+Shift+R) to bust PCF cache. |
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
