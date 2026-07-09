# Current Task State — spaarke-daily-update-service-r5

> **Last Updated**: 2026-07-09 (context-handoff, pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-daily-update-service-r5`.

---

## Quick Recovery (READ THIS FIRST)

> **2026-07-09 update — Email-share feature COMPLETE (not deployed).** Operator chose A + system-sender + build-now. Built + reviewed + tested; **DEPLOY IS ON HOLD** pending cross-project coordination approval (operator instruction). 3 new commits on top of the prior 7: `5c3c1a9ee` (BFF /email colleague recipient + internal-only egress guard, 8/8 contract tests), `21e5ad3dd` (client #2 Email Briefing via shared SendEmailDialog + #3 Email Item draft activity; deterministic body/link helpers; 11/11), `2807b4237` (Step-9.5 review fixes). Design/decisions/review: `notes/email-share-feature-plan.md`. Reply to redesign-r2 E-12: `notes/REPLY-to-redesign-r2-E12-consumer-response.md` (ready to send). Pre-existing unrelated test failures on branch: `legalWorkspaceSectionRegistry` + `ActivityNotesSection.callbacks` (onKeep ttl) — 090 /defer candidates. **When deploy is approved:** merge branch→master, re-deploy BFF (picks up 002 @odata.bind fixes + /email change), deploy SpaarkeAi widget, UAT (incl. Email Briefing + Email Item), close 017/024/038/022, then 090 wrap (+ prompt-shape-parity test defer, + note-passthrough fast-follow for #2).



| Field | Value |
|---|---|
| **Project** | spaarke-daily-update-service-r5 — **21/26 tasks done; CODE-COMPLETE**. Remaining are deploy/operator/wrap only. |
| **Branch** | `work/spaarke-daily-update-service-r5`. Merged to master once (PR #597, merge `1355a830a`). **Since then 7 new local commits, PUSHED-then-DIVERGED — see below; NOT re-merged to master.** |
| **Status** | Awaiting operator decision on the **Email Briefing / Email item feature (#2/#3)** — see Next Action. UI polish + follow-up hardening all committed locally. |
| **Next Action** | Operator was deciding how to build the **"Email Briefing / Email item" share feature**. I recommended: **(A) extend `/email` to accept a recipient + reuse `SendEmailStep`; system-sender; build now.** Awaiting their pick of: A-vs-B body source, user-mailbox-vs-system sender, build-now-vs-follow-up. THEN build it, then merge+deploy+UAT. |

### Unpushed/undiverged local commits since master merge `1355a830a` (newest first)
- `8546b5e6b` DigestHeader vertical dots (MoreVertical)
- `432ae5ce6` StatTiles Overdue/New-matters count hardening (exact category-key match)
- `9b32c1c8a` **8 @odata.bind nav-prop bugs fixed** (task-002 metadata verification)
- `c1e658850` formalize 021/023 complete + UI polish bookkeeping
- `9834e0e16` StatTiles "Open items" → "Updates" relabel
- `0980bfc09` DigestHeader "Last updated {date} at {time}" (dropped "N items")
- (earlier this session, already on master via #597: 002/012/036/037/015/031/032/016)

> **NOTE**: I last pushed the branch during the PR #597 flow; the 6 UI/hardening commits above were made AFTER and are **local-only** (not pushed, not merged). Push + re-merge them with the email feature at deploy time.

### Critical context
- **BFF IS DEPLOYED to spaarkedev1** (`spaarke-bff-dev`, hash-verified, `/healthz` 200, briefing endpoints 401) — but deployed BEFORE the 6 local commits above, so the **002 @odata.bind fixes + UI changes are NOT live yet**. Re-deploy BFF at final deploy to pick up the 002 fixes.
- **Daily Briefing UI is NOT deployed** — the redesign + all UI tweaks live in `@spaarke/daily-briefing-components`, hosted by the **SpaarkeAi code page** (operator confirmed: only surface). Needs a **client/widget deploy** for the visuals to go live.
- **Dataverse MCP is authorized** (spaarkedev1). Also an **`az` Dataverse token works**: `az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`. Metadata verification recipe: `GET {org}/api/data/v9.2/EntityDefinitions(LogicalName='X')/ManyToOneRelationships?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName` → the nav prop IS the `@odata.bind` key.
- **012 live retirement DONE**: Action `BRIEF-NARRATE-CHANNEL` (`dc3533c0-…`) deactivated (statecode 1). **032 restore DONE**: node `sprk_playbooknode=0fa4e8db-…` has `sprk_documenttype` restored as `type:"choice"` + 13-option map (data-verified; end-to-end EXECUTION round-trip is a UAT step — see #3 in the UAT checklist).

---

## Email Briefing / Email item feature (#2/#3) — READY TO BUILD (awaiting operator go)

**Operator UAT feedback (2026-07-09):** (1) ✅ done: main-toolbar dots → vertical. (2) want **"Email Briefing"** ("how do I share this with a colleague") = email a **link + HTML summary**. (3) same for an **individual item**. Operator suggested reusing the "email wizard shared component."

**Investigation result (grounded):**
- **Reuse `SendEmailStep`** — `src/client/shared/Spaarke.UI.Components/src/components/EmailStep/SendEmailStep.tsx` — the shared "recipients → subject → body → send" step. Precedent: **`DocumentEmailWizard`** (`Spaarke.UI.Components/DocumentEmailWizard/`) wraps it for "email these documents" (builds `{to, subject, body}`). Also exists: `EmailComposeWidget` (`Spaarke.AI.Widgets/.../EmailComposeWidget.tsx`). Do NOT use `ComposeWorkspace` (TipTap doc editor — wrong tool).
- **Server already 80% there**: `POST /api/ai/daily-briefing/email` → `composite.EmailAsync(systemUserId, tenantId, recipientEmail, ct)` (`DailyBriefingCompositeService.cs:170`) **generates the briefing HTML + sends via the Communication service**, but hardcodes recipient = caller (`DailyBriefingEndpoints.cs` HandleEmail ~line 174-193). Gap: accept a colleague recipient + a UI to pick them.

**Recommended plan (per §11 default-to-reuse):**
- **#2 Email Briefing**: DigestHeader ⋮ menu item "Email Briefing" → dialog hosting `SendEmailStep`, prefilled subject "Daily Briefing — {date}" + body = briefing HTML summary + **deep link to the SpaarkeAi Daily Briefing**. **Option A (recommended)**: extend `/email` to take a `recipient` → server sends its existing HTML. Option B: client-composes the body.
- **#3 Email item**: item ⋮ menu "Email" → same `SendEmailStep` dialog, body = single item's summary + **deep link to the record**. No server "email-one-item" leg → build body client-side.
- **New scope flag**: this is a feature beyond R5's accuracy/appearance/hardening charter (reuses existing components, but it's a feature). Batch into the pending deploy.
- **Open decisions**: A vs B (rec A) · user-mailbox vs system sender (EmailAsync uses Communication/system today) · build-now vs follow-up.

---

## Full State

### Tasks (21/26 ✅ — see tasks/TASK-INDEX.md)
✅ 001,002,010,011,012,013,014,015,016,020,021,023,030,031,032,033,034,035,036,037,040
🔲 **017** (Phase A deploy+UAT), **022** (operator harness sign-off), **024** (Phase D deploy+UAT), **038** (Phase B deploy+UAT), **090** (wrap: /test-diet + /defer). **ALL need deploy + browser UAT on spaarkedev1 + operator.**

### UAT checklist (provided; operator runs after final deploy)
- **G-R5-A accuracy**: row provenance (Open record opens the row's OWN record); TL;DR only names real shown items; tile counts true.
- **G-R5-C hardening**: **document-type round-trip** (profile a doc → open sprk_document → Document Type set, no 500 — this is the 030+032 proof); collaborator scope (only own items).
- **G-R5-D appearance**: header "Last updated {date} at {time}"; tiles "Updates/Overdue/Critical/New matters" (Critical can exceed Updates — OK); Fluent v9 light+dark, Segoe UI 20px #242424.

### Deploy runbook (when operator says go)
1. Merge branch → master (sync origin/master first to catch conflicts; PR + auto-merge, master is protected → Path A).
2. **Re-deploy BFF**: `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (app `spaarke-bff-dev`, rg `rg-spaarke-dev`). Hash-verify + `/healthz`. Picks up the 002 fixes.
3. **Deploy the SpaarkeAi client widget** (host of Daily Briefing) — need the SpaarkeAi code-page deploy path/skill.
4. Operator runs the UAT checklist → close 017/024/038/022.
5. **090 wrap**: `/test-diet` (reconcile tests added this project) + `/defer` (Monitored-For D-3, EventDetailSidePane, StatTiles category-key coupling, any remaining).

### Key deferred/tracked items (for 090 /defer)
- Monitored-For schema (D-3) — future round.
- `EventDetailSidePane/TodoSection.tsx:233` `sprk_assignedto` casing — deferred (side pane not in use); should be `sprk_AssignedTo`.
- 002 `@odata.bind`: 8 fixed, 2 confirmed correct; EventDetailSidePane still deferred. Report: `notes/odata-bind-audit.md`.

### Coordination
r2-core (`spaarke-ai-architecture-redesign-r2`) owns `Services/Ai/` engine internals; r5 owns `Narrators/DailyBriefing*` + `Nodes/UpdateRecordNodeExecutor`. Registered in `projects/INDEX.md`.
