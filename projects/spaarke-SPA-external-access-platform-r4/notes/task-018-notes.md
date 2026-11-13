# Task 018 — P1 deploy + E2E: prep (2026-11-12)

Status: **dev SPA deployed 2026-11-12 (run 43324493710); E2E not started (needs owner accounts).** Rigor STANDARD; prescriptive steps; Sonnet @ high per POML.

## Gates, in order (POML + `docs/procedures/dev-bff-deploy-coordination.md`)

| # | Gate | State 2026-11-12 |
|---|------|-----------------|
| 1 | Tasks 002–007 **merged to master** (POML gate; the BFF deploy is of a master SHA) | ✅ PR #2681 merged 2026-11-12 18:51Z as merge commit **`c53452bb0c`** (owner: "Merge after CI"; 45 checks pass, 0 fail). ebilling-r1 and uac-r2 told the SHA. |
| 2 | `85fbba3f64` in the deployed SHA | ✅ in master already (and in the branch) |
| 3 | **UAC review of task 002** (FR-08 routes, list `canCreate`, message gate refactor, delete-409 body) | ✅ uac-r2 PASS (FR-08, FR-24), 2026-11-12, after F4-1 (`notes/task-002-notes.md` "For uac-r2"). Merge order with ebilling-r1: whoever merges second re-runs the external-access and route-ledger arch tests |
| 4 | Owner **"go BFF deploy &lt;master sha&gt;"** given directly in uac-r2's session | ✅ Not needed: live dev BFF **`4e64670ea0`** (uac-r2 deploy ~19:40Z 2026-11-12) already contains `c53452bb0c` (ancestry verified locally, `merge-base --is-ancestor` exit 0). ebilling-r1 confirmed it covers #2689/#2708/#2724/#2727 |
| 5 | Peer OKs, Router green, content checks, ebilling schema verify, app settings | uac-r2 runs these (orchestrator); R4 gives its peer OK |
| 6 | uac-r2 read-back + 30-minute gate PASS, 0 new 5xx on external routes | ✅ read-back passed per uac-r2 (/healthz 200, /ping 200, anon MCP 401); uac-r2 watches external routes again after the SPA deploy |
| 7 | Owner approval of the SPA deploy with rollback stated | ✅ owner "Yes deploy spa" 2026-11-12 → run **43324493710** (dev, `--ref master` = `2ef4a17915`, success ~23:00Z). Verified: index + deep path 200; live `/assets/app.js` contains `invoiceRecord` (R4 code); baked BFF host `spaarke-bff-dev` `/healthz` 200. uac-r2 + ebilling-r1 told; uac-r2 watches external routes next gate |
| 8 | E2E (POML steps 6–9) | ❌ needs owner-supplied accounts (below) |

## SPA deploy plan (for the owner's approval at step 4)

- Workflow `.github/workflows/deploy-external-spa.yml`, `workflow_dispatch`, environment `dev`, ref = the merged master SHA.
- `workflow_dispatch --ref` takes a branch or tag, not a SHA: deploy with `--ref master` (contains `c53452bb0c`); a
  rollback needs a temporary tag at the rollback commit first.
- **Rollback:** re-run the same workflow on **`b083d62183`** — the last dev SPA deploy (run 41233021759, 2026-10-31,
  success). Earlier: `96f1f6b675` (run 40989384010).
- ⚠️ This is the **first SPA deploy since 2026-10-31**. It ships everything merged to master's `src/client/external-spa`
  since then, not only R4 — including ebilling #2689's SPA changes (invoice submission page, task 130/134 work). Their
  SPA features need their BFF routes live; confirm with ebilling-r1 and uac-r2 that the dev BFF SHA covers them.
- BFF host: confirm the dev BFF host is unchanged since `b083d62183`'s build (Vite bakes it in; project gotcha).

## Post-SPA-deploy gate (uac-r2, 23:00–23:30Z 2026-11-12)

PASS, no faults: 45 BFF requests, 0 5xx, 1 4xx, **0 on external routes** (new matter/WA event routes and `canCreate` not
yet exercised). Tell uac-r2 when the E2E is done; it re-reads the external routes.

**Mid-E2E BFF redeploy (uac-r2, owner's go):** E2E paused; dev BFF now `9411f604bb` (local merge of master
`e9208abc5e` + mcp-r1 spike `d7369fecf1`; contains `c53452bb0c`, verified locally). Read-back 02:49:36Z all as expected;
rollback snapshot `4e64670ea0`. No external-access files in the spike. SPA unchanged (same BFF host).

## E2E results — part 1, write-capable partner (owner driving, 2026-11-12)

| # | Check | Result |
|---|---|---|
| 1 | Sign-in (already-bound contact; #2761 not hit) | PASS |
| 2 | Projects grid | PASS |
| 3 | Project PRJ-000046 opens in the template (title, toolbar, sections, "Your access" = Collaborate) | PASS — but see defect E-1 |
| 4 | Upload document on the project | PASS |
| 5 | Submit invoice from the project → record preselected → invoice created, linked to the project, amount + date extracted | PASS |
| 6 | Submitted invoice appears in the Invoices grid (INV-123456.7, In Review) | PASS |
| 7 | Refresh; Copy link (opens the same record in a new tab) | PASS |
| 8 | New message, both directions SPA ↔ model-driven app | PASS |
| 9 | Add event | PASS |
| 10 | Console free of errors | PASS |
| 11 | Matter and work assignment: "Your access" = "Can contribute"; upload, messages both ways, add event | PASS (no 403) |
| — | Add to-do; #2658 snippet | skipped: to-do is being removed; Submit invoice becomes a modal (owner 2026-11-12) |

Not R4: the internal model-driven app's Create New Event wizard → Send Email failed with a raw Polly TimeoutPolicy message
on BFF `9411f604bb` (possibly a cold start after the restart) → **#2778**, reported to uac-r2 for its gate.

**Defect E-1 = D-12 #2769 (R4):** the record's Documents section overlaps columns at the record-modal width — the name runs into the
Type badge and Date Added (screenshot 2026-11-12).

**Owner feedback 2026-11-12 — decided: finish the E2E, then ONE rework task (R4 items + the Submit-invoice modal,
R4 builds, ebilling reviews), redeploy, re-test. ebilling's own items filed as #2770. To-do BFF routes stay for now;
SPA stops using them; retirement is a filed follow-up.**
- R4 record template: move each create action into its section — Upload → Documents, New message → Messages,
  Submit invoice → Invoices (the header toolbar keeps record-level actions).
- R4: **no To Do in the SPA.** "Add to-do" creates `sprk_todo` (labelled "Task" in the UI — wrong). External users work
  only with `sprk_event` of type **Task** (resolves D-6 #2703 towards "send the type").
- R4/ebilling: Submit invoice should be a **modal over the record modal** (same size and position), not a page, and the
  record should be fixed (no record step) when launched from a record. A modal also removes the #2658 navigation risk.
- R4/ebilling: on the Invoices grid, Submit invoice belongs in the grid toolbar (with Refresh / Export), not its own button.
- ebilling: invoice files beyond PDF (Excel / data files); "Submit other files" (supporting files not treated as the
  invoice); several invoices in one submission.

## FR-33 evidence so far

- (2) Held-invoice Review detail: **deferred to task 028** (R4 P1 does not render `sprk_reviewdetail`; FR-11 is P2 and
  the column stays out of every read until ebilling 082c-2 is live — project gotcha).
- (3) `85fbba3f64` "not set up" text: **deployed** — `OWNER_UNCONFIGURED_TITLE` "Sign-in is not set up yet" is in the
  live `/assets/app.js` (checked 2026-11-12). Rendering needs an organization whose sign-in owner is unconfigured, which
  dev does not have; covered by `tests/workforce-home-auth.test.ts`.
- (1) WorkforceHome invite 409 and (4) external-route counts: in part 2 / from uac-r2's gate.

## E2E (steps 6–9) — accounts and records needed (owner to supply/confirm)

- ⚠️ **Watch-item #2761 (uac-r2's code, not R4):** on dev the identity store's contact-binding readability proof faults
  on WhoAmI (403) every 5 min since 2026-11-02 and fails closed, so an **unbound** contact's first sign-in, invite
  redemption or registration may be refused or stall. Prefer already-bound test contacts; if a first-time contact
  fails, attribute it to #2761 and tell uac-r2 (they will prioritize it).

- A partner with **Collaborate/Create** on a matter, a project and a work assignment; a **View-only** partner on the same.
- A **firm-linked** partner (Submit invoice) and a **no-firm** caller.
- A WorkforceHome contact on a project (FR-33(1) invite 409 `sdap.access.invite.workforce_home_contact`) + the read-only
  no-collision-flag query, shown to the owner before it runs.
- A held invoice for FR-33(2) only if ebilling 082c-2 is live; else "deferred to task 028".
- Browser checks carried from earlier tasks: 004 ui-tests + D-8 #2704 (child opened &lt; 250 ms aria-hidden); 006
  ui-tests in light/dark; 007 ui-tests + #2658 aria-hidden after Submit invoice from a **grid-opened** record (steps in
  `notes/task-007-notes.md` fix round 1). Teams host per POML step 6 (Teams package itself is not redeployed: task 058).
