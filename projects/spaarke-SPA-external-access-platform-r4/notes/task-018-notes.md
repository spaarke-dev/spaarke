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
