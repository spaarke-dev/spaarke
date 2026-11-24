# Task 038 — P3 deploy record (dev)

## Step 1: Phase 3 merges (checked 2026-11-24 against origin/master `20129e985c`)

| Task | PR / merge | BFF change | On the live dev BFF `0362599b94`? |
|---|---|---|---|
| 031 Information fields + allow-list (FR-18) | #3033 `bd40b57b03` | Yes: 12 `ReadableColumns` (UAC: 11 approved by uac-r2, `sprk_regardingmatter` accepted by the owner in uac-r2's session 2026-11-23; task-031-notes) | Yes (`git merge-base --is-ancestor bd40b57b03 0362599b94` = 0) |
| 032 Grid settings (FR-19) | #3033 `bd40b57b03` | No | n/a |
| 034 Messages refresh (FR-21) | #3033 `bd40b57b03` | No | n/a |
| 033 Money sort/export (FR-20) | #3048 `b64d988625`, follow-up #3055 `8dd44cf528` | No | n/a |
| 035 Message threads (FR-22) | #3048 `b64d988625` | Yes: `threadId` on external messages (UAC APPROVED with conditions, final diff approved 2026-11-24; task-035-notes) | **No** (ancestor check = 1) |
| 036 Matter events type/assignee (FR-23) | Cancelled (superseded by 014/015, owner 2026-11-15) | — | — |

The order gate (the BFF with 031's columns before any SPA build carrying 031) is met: the BFF has 031, the SPA does not yet.
For threads to group, the BFF must carry `b64d988625`. Without it the SPA shows the messages ungrouped (no failure).

## Live state before this task

- SPA: master `9d08576e63`, run 45260396182 (task 028). This is the rollback target for the P3 SPA deploy.
- BFF: `0362599b94`, live ~05:21:45Z 2026-11-24 (uac-r2).

## SPA content since the live SPA (external-spa source)

- #3033 `bd40b57b03` (R4 031/032/034), #3048 `b64d988625` (R4 033/035), #3055 `8dd44cf528` (R4 #3045 export);
- ebilling `075b298676` (iss048: submission result title/icon follow the outcome).
- Shared ui-components commits from other projects (decision wizard, console, wizard bootstrap, ai-widgets fold) are also on master; the served-bundle check at deploy time confirms what reached the SPA.

## Step 2: request to uac-r2

Sent 2026-11-24 (~15:45Z). The request:
- the BFF candidate must contain `b64d988625`, citing 035's UAC approval;
- 031 is already live and 036 is cancelled;
- verification asks:
  - (a) a P3 column is readable;
  - (b) an unlisted column is refused with `DV_FETCHXML_COLUMN_NOT_PERMITTED`;
  - (c) messages carry `threadId`;
- the SPA follows only after uac-r2's after-notice and GATE PASS.

uac-r2 had already said (earlier that day) that `b64d988625` will be in its next dev BFF candidate. The owner gives the BFF go in uac-r2's session.

## Step 3: uac-r2 after-notice

- uac-r2 reply (2026-11-24): candidate **master `20129e985c`** (built in its worktree). It contains `b64d988625`, sits on top of the live `0362599b94` (031 stays), and adds ebilling ISS-029/042/043, ontology 207 and a signals test. CI shards are running. uac-r2 names the SHA to the owner in its own session once it is green. After the deploy it runs checks (a)–(c) and then sends the after-notice and the GATE result.
- After-notice: pending.
