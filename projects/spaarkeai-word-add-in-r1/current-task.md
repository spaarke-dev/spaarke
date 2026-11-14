# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-14: #2815 and #2820 merged and live (942b2889bf), and ebilling was told. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active task** | **134 🔄** Invoice tab: firm required, "Open in Spaarke", firm on the banner, Correction Note, inline firm assign (FULL, sonnet/high; POML `tasks/134-…poml`; owner go 2026-11-13). Done: **A+B1** (#2789, c534bc0b82), "Check again" (#2793, c2f366bf52), the **095e adaptation** (#2815, 759dc4b1f6), and **B2 Correction note** (#2820, 942b2889bf). **C** (inline firm assign) waits for ebilling 095g's contract (uac-r2 approved its design; it waits on the owner's answers). |
| **Open PR** | None. #2815 and #2820 were merged 2026-11-14 on the owner's go ("Yes, merge both"); every check passed on both. SWA deploy run 43552913411 (942b2889bf) succeeded, and the live bundle contains `correction_note_required` and `firm_name_unreadable`. ebilling-r1 was told the live SHA, and their 095f dev-deploy hold is released. Worktrees C:\wt134c and C:\wt134d were removed (check that `git worktree list` has no wt134d). |
| **Next Action** | (1) Owner check on dev: a record with no firm shows the message, the link and Check again; Open in Spaarke works; the banner names the firm (once 095e is on the dev BFF); a correction asks for a note, and after 095f is on dev the note is saved on the new invoice. (2) Wait for ebilling's 095e and 095f master SHAs, then the 095g contract (slice C); uac-r2 deploy notices (range check, reply); ontology's task 189 SHA and contract (Word Email tab). |
| **Uncommitted** | Nothing in this worktree after this checkpoint commit. |
| **Live dev BFF** | **b100effac3** (live since 2026-11-13T20:39Z): master **c2f366bf52** + mcp-r1 spike **d7369fecf1**. Before it: 0c06f464e3. Every later dev deploy must include the spike. After-notice check: no add-in concern. `FlaggedOfficeEdit` is still unset; uac-r2 will say when it is on. |
| **Owner checks** | All done. Word passed on 9411f604bb. On 0c06f464e3 the Outlook checks passed: email with attachments (030b), To Do, Submit Invoice, the correction (INV-0012.2 supersedes .1), and the banner plus its second-submit refusal. Child-BU team ownership is covered. The no-firm submit led to task 134. All of it was reported to uac-r2, who recorded the passes. |
| **Last merge to master** | **#2789** (c534bc0b82, 2026-11-13, owner's go). The first CI run failed on our blocked-pop-up test: `useAnnounce` clears the live region after 1 s, and the suite took 92 s, so the test read it empty. Fixed in e243475993 with a MutationObserver. There was also a one-off Spaarke.Auth `BrowserMsalStrategy.dataverseUser` failure, which passed on the rerun. |

## Task 134 facts (ebilling's answer: owner rulings EB-D-223..226 at 7a307104f1)
- **D-223:** no invoice without a firm in ANY channel. It is refused at intake (400 `firm_invalid`). Invoices already held keep Set firm (095e).
- **D-224:** assign-firm is ebilling 095g, built to uac-r2's conditions. Pane conditions from uac-r2 (already in the POML slice C):
  - show the server's refusal text as given;
  - confirm a replace or clear first, because that revokes the firm's external users;
  - no client-side security filtering.
  The firm link is `sprk_assignedlawfirm1/2`, which also grants external access.
- **095e** (ebilling 7953d052d9, not yet on master):
  - adds a top-level `firmName: string | null` to the 095a view, next to `firmId` (contract §11; NOT `firm {id,name}`, so the pane is adapted in #2815);
  - D-226 naming: "{firm name} - {invoice number}";
  - a new transient Failed code, `firm_name_unreadable`;
  - `firm_invalid` now also means "a firm is required".
- **D-225 / 095f** (ebilling bb28870b76, not on master yet):
  - `correctionNote` is required on Correction: trimmed, at most 2000 characters, \n \r \t allowed; blank gives 400 `correction_note_required`.
  - It is refused on New.
  - A retry of a still-Received Correction must resend it.
  - It is saved on the NEW invoice's `sprk_correctionnote`.
  - Pane: #2820.
- **ebilling's order:** 084d, then 095e, 095f, 095g.
- **Known limits in #2789:**
  - K4: a one-render double link after a record change.
  - K2: a correction prefill with a malformed firm id leaves Submit disabled with no explanation.
- The local add-in build's dev-certs step installed the Office Add-in Developer CA into the machine trust store. The owner has been told.

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Agreed with ontology: their task 189 builds `GET /api/communications/templates` as an impersonated caller read, reviewed by uac-r2, and switches the Console to it.
- This project then does the pane side, as a new task via task-execute added to TASK-INDEX:
  - EmailView passes `onDraftWithAi`, `onListEmailTemplates` and `onRenderEmailTemplate`;
  - add them to the `.d.ts` subset;
  - EmailView tests;
  - owner narrow-width check.
- Trigger: ontology sends the master SHA and the handler contract.

## Submit Invoice (task 133): state
- Banner (#2533) active since 6dbe69b9e2. Backfill for older submissions is ebilling's.
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## Decisions in force (detail in CLAUDE.md "Standing directives")
- **W3 create paths:** 030a/030b live; pane content read OK (CLAUDE.md "Create paths follow fm's W3 direction").
- **Roles:** add-in user = Core User + Office Add In User; service and demo accounts never get the add-in role.

## Recent deploy checks (newest first; older ones in git log)
- 2026-11-13 b100effac3 (master c2f366bf52 + spike d7369fecf1, live 20:39Z; parents verified): range 92d0ca09f7..c2f366bf52 is 19 commits. No Office, Finance, Communications or record-creation/ownership server files. The add-in commits are task 134 only (#2789, #2793, client). No add-in concern; sent to uac-r2.
- 2026-11-13 0c06f464e3 (master 92d0ca09f7 + spike): 77 commits, with no add-in or Office/Finance server files. 197a (unfile/re-file ownership) and 022b (Restricted-source mapping, existing 400 code) need no pane change. No add-in concern.
- 2026-11-12 9411f604bb (master e9208abc5e + spike): no add-in concern.
- 2026-11-12 4e64670ea0, 6dbe69b9e2: no add-in concern. #2666 D-217: a Word Email-tab send filed to a record is archived as `.eml` server-side.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys, roles, review of the assign-firm route.
- `ebilling-r1`: 095e/f/g; sends master and dev SHAs.
- `ontology-r1`: task 189 templates route (Word Email tab).
- `field-mapping-r1`, `provisioning-r1`: nothing open.

## Owner items (open)
- Merge go for #2793; dev check of task 134 after the deploy.
- Prod Entra: register the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`.
- Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`).
- Excel/PowerPoint add-in (no go yet); UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; 090 wrap-up (`/test-diet`).
