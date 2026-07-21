# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-20 (by context-handoff, mid-W9 remediation)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | W9 post-UAT remediation — **ALL 4 DEFECTS FIXED/BUILT + committed** (091/092/093, all gates clean). Awaiting owner go-ahead on DEPLOY. |
| **Active** | **DEPLOY** the remediated BFF + hand off PCF/UAT remainder. NOT started (needs owner OK — merge origin/master 25 commits + App Service deploy is outward-facing). |
| **Done (W9)** | 091 #7 `d6d03f6cb`; 092 #4/#5 `070e6e663` (+M2 empty-message-id hardening); 093 #2 PCF `2d56cf8e0`. Bookkeeping `29f6a619c`/`c5e2edc75`/`6e1813d95`. |
| **Deploy plan** | (1) `git merge origin/master` into worktree (resolve conflicts), (2) build+`dotnet test --filter Communication` green, (3) §10 publish-size+CVE, (4) `/merge-to-master` (protected → auto-merge PR), (5) App Service deploy `spaarke-bff-dev` + healthz. THEN owner: import `CommunicationAttachmentsSolution_v1.0.0.zip` + swap OOB subgrid; UAT re-verify (D-1 fresh email proves #5 auto-profile + webhook-KV; #7 subject-PAT/body-REAL→Ambiguous; #4 open archived .eml). |

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
