# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-26 ~18:15Z. 139 and 140 DONE. 142 DONE (owner re-check PASS, #3194 closed). 141 server + pane slices committed in `C:\wt141` (not pushed), verification next. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task 139** | **MERGED** #3204 → master **03213a90fd** (owner go "Yes, merge when green"). Add-in deploy run 45750570047 success; the live bundle carries the new strings (checked by fetching). Review fixes f738a841e8; CI-only test fixes 57ad4d8a1a (`AuthService.naaRefusedToken` msal resolve; a `BrowserMsalStrategy.dataverseUser` exp clock flake); gate list now 149/149 suites. Owner live check in Outlook on the web PASS (2026-11-26). **CLOSED** (POML + TASK-INDEX). |
| **Task 140 = ISS-043 (#3194)** | DONE 2026-11-26 (PR #3214, 152397ab11), but ISS-043 was REOPENED the same day → task 142. Open gap: `deploy-spaarke-ai.yml` paths omit `src/client/shared/Spaarke.Communication.Components/**` (not yet filed or fixed; 142 reached the Console only because it also touched `Spaarke.AI.Widgets`). |
| **Task 142 = ISS-043 reopened (#3194 open)** | The reconciliation page's `buildResolveReview` never passed the written regarding; the Console widget had its own inline copy (review finding). **MERGED** PR #3254 → 177b0ebb7c (owner go "Yes, merge and deploy"). **LIVE**: `sprk_communicationreconciliation` 18:04Z (byte-identical to the 177b0ebb7c build), Console by CI from 166a941b18 17:56Z. ontology-r1 told. Owner re-check PASS 2026-11-26; #3194 closed; 142 ✅, ISS-043 Done. |
| **Next Action** | **Task 141**: server slice `3db0053f3b` + pane slice `fc43d1b1c9` on `fix/word-addin-141` in `C:\wt141` (not pushed). Server: `email.savedDocumentId` on `POST /api/office/save` (adds ticked attachments under that `.eml`, write check on it, no prior job needed); `savedAttachmentNames` on `resolve-email-identity` (per-child read check, ADR-008 A1). Pane: compact "Saved to Spaarke" section, saved/unsaved attachment marks, warning, icon-only Generate Profile, no Document type for email. Implementer gates: BFF 0 warnings, ArchTests 2845, +14 KB publish, pane jest 44/373, typecheck baseline 68. **Next**: verify on the committed tree (BFF affected classes + ci-gated-suites additions), adversarial review ×2 (authorization), uac-r2 review of the add path + child read, PR, owner merge go, BFF deploy via uac-r2 (owner go in uac-r2's session), add-in deploy, owner check. Then 090. |
| **Deploy rule (uac-r2, 2026-11-26)** | A dev BFF deploy go must be given by the owner IN uac-r2's session, naming the exact SHA; a relayed go does not count. Code-page / add-in deploys stay with this session under the owner's go here. |
| **Owner questions open** | (1) The pane message for ebilling's 413 `too_large` (now live on dev, unreachable at the default 25 MB)? A small separate PR. (2) Confirm ISS-043 was meant (not ISS-034). |
| **Done** | 2026-11-25: #3134 (137), #3139 (136), #3157 (138) merged; dev BFF aa933c09e6; 136–138 CLOSED. 2026-11-26: uac-r2 deployed dev BFF 1e64e94168 (no Office changes; gate PASS 02:27Z). #3204 (139) merged 03213a90fd and deployed. ISS-044 #3202 filed (upload capture on an auto-file-off tenant is created Suggested; communication owner). |
| **Dev BFF** | uac-r2 deployed d307d67e36 at ~18:00Z (no Office / Finance / document-save change over 140c01219a; only the global AuditEnrichmentMiddleware from #2622 reaches Office routes; no Office traffic yet to check). Rollback 140c01219a. uac-r2's #3245 Assign audit: no Office feature needs the Office Add In User Assign grants (answered). |
| **Worktrees** | `C:\wt141` (active, task 141). `C:\wt142` (detached at 177b0ebb7c with dist builds). After use, remove with Remove-WorktreeSafe.ps1: `C:\wt136`, `C:\wt137`, `C:\wt138`, `C:\wt139`, `C:\wt140`, `C:\wt142`. `C:\wt137pub` needs manual deletion (tell the owner). |

## Still with the owner
- Test-diet deletions (`notes/test-diet-report.md`); #996 blocking gate (ruleset choice); 125/135 checks; ISS-001 has no GitHub issue; F-g leftover `event` entry in EntityAccessFilter; task 065 leftover checks.
- Mention: three stray python processes spinning since 11/23–24 (PIDs 41460, 84288, 43772); quality alerts #1821 and #1906 (do not fix).
- Prod Entra: the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`. Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`). Excel/PowerPoint add-in (no go yet).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Ontology task 189 builds `GET /api/communications/templates`. Then this project adds the pane side as a new task. Trigger: ontology sends the master SHA + contract.

## Submit Invoice
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.
- ebilling task 123 upload contract agreed (`projects/ebilling-r1/notes/123-upload-contract.md` on `task/ebilling-123`); the pane's supporting-files choice stays unwired until 123 merges.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys (owner go per SHA); review of task 140's new confirm write.
- `field-mapping-r1`: G7 guard in their task 035 PR. `ebilling-r1`: #3155 live. `ontology-r1`: task 189 templates route.
