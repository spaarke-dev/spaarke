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
- BEFORE notice (uac-r2, 2026-11-24): the owner gave the go for `20129e985c` in uac-r2's session; the swap from `0362599b94` started. Checks (a)–(c) follow once it is up.
- LIVE notice (uac-r2): dev BFF `20129e985c` live since 21:36:18Z 2026-11-24. Results:
  - hashes 4/4, package 42 MB, `/healthz` 200;
  - signed-in route checks as expected (preview-url 403, unknown route 404);
  - startup clean.
- Checks (a)–(c) were **not run live**. They need an external contact token, which uac-r2's session does not hold. uac-r2 confirmed instead that the served DLLs are hash-identical to the `20129e985c` build, and that build carries 031's `ReadableColumns`, the allow-list refusal and the `threadId` field.
- (a)–(c) move into this task's E2E with a partner sign-in. A 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED` on an allow-listed column, or a missing `threadId`, blocks the SPA: roll it back to `9d08576e63` with owner approval. Report the result to uac-r2.
- **GATE PASS** (uac-r2, window 21:36:18Z–22:07Z). Results:
  - 70 requests. The only 5xx is `/healthz/catalog` 503, a known drift. The other 4xx were uac-r2's own probes or the platform ping.
  - External traffic: 16 requests, all 2xx; 0 `DV_FETCHXML_COLUMN_NOT_PERMITTED`; 0 exceptions on external routes.
  - One Redis lease-renewal blip, pre-existing.
  - uac-r2's side of the deploy-order rule is satisfied.

## Step 4: SPA deploy plan (owner go pending)

- Master HEAD at the check (2026-11-24 ~22:10Z): `fa96792811` (merge of #3073 ebilling-r1).
- External-SPA source since the live SPA `9d08576e63`, one item beyond Step 1:
  - ebilling #3051 `cf1b7027ce` (submission result screen).
  - #3073 changes nothing under `src/client/external-spa` or `Api/ExternalAccess`.
- New shared ui-components commits since the Step 1 list, all in components the SPA does not use:
  - Report Card wizard (`b29bb1f5f5`) and WA wizard (`2156330275`);
  - Decision Wizard (`20129e985c`);
  - `regardingRowStyles` (`a452afe110`);
  - EmailComposer (`525a1dfe10`);
  - SprkChat types (`a83f5b629d`);
  - `mountWizardPage` (`3471c31b7e`).
- Rollback: redeploy `9d08576e63`.
