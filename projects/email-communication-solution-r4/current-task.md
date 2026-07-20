# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-20 (by context-handoff, mid-W9 remediation)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | W9 post-UAT remediation (owner: deliver all 4 defects in-project, no deferral) |
| **Active** | Task **092** (#4 faithful .eml + #5 on-demand profile enqueue) — starting implementation |
| **Just done** | Task **091** (#7 body/attachment reference extraction) — code + tests committed `d6d03f6cb`; 511 Comm tests green; publish 45.68MB (+0.38, ≤60); 0 HIGH CVE. **Step 9.5 code-review+adr-check running in subagent** — read its verdict, apply any must-fix, then mark 091 ✅ in TASK-INDEX. |
| **Next after 092** | Task **093** (#2 Communication Attachments preview PCF) |
| **Then** | Deploy remediated BFF (merge origin/master first per DEPLOYMENT-CHECKLIST), owner UAT re-verify incl. D-1 fresh email; then 090 wrap-up |

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
