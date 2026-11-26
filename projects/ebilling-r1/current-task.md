# Current Task State

> **Last Updated:** 2026-11-26 ~03:15Z (checkpoint after 095d-3 merged into work)
> History: `notes/handoff-history/current-task-2026-11-26a.md` (the previous state, verbatim), plus `-2026-11-24a`, `-20a`, `-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Now

- **No task running.** Work tip is pushed; branch `work/ebilling-r1`.
- **On work, not yet in a master PR (the next batch):**
  - 095d-3 Billing firm field in the Invoice Review header (merged 9ad5c99e02; one review pass, fixes 7bc2910398; jest 696/0). The header firm is an editable dropdown only while the invoice has no firm, firmNeeded is true and the caller can write. It lists the core record's linked firms (`GET /api/finance/core-records/{type}/{id}/firms`) and saves through `POST /api/finance/invoices/{id}/firm`. A 403 stays in the field (it is about the chosen firm).
  - Reader-role script accepts the 9 platform-added privileges (e714592cb2; owner yes; uac-r2 cleared). **Owed:** tell provisioning-r1 when it reaches master; it then rewords the S29 refusal text and the rejection-code doc. Until then ACME gets `-Verify` only.
  - Docs: 095c and 095d closed; checklist Y; current-task.
- **Dev:** BFF 1e64e94168 live since 2026-11-26 01:56:50Z (uac-r2, owner go; carries batch 5; rollback aa933c09e6). Invoice Review page from the same SHA since 01:59:31Z (sha256 49a8243a…9502; rollback 70c9d772…8960). uac-r2's 30-minute gate PASSED 02:27Z (146 requests, 0 5xx). `sprk_apexport`, `sprk_invoicefiles` and `sprk_rulereviewpage` are not redeployed (refactor-only, our next page deploy). `sprk_recordprocedures` has never been on dev: its first rollout is task 112.
- **SpaarkeMaster 1.3.3.0 published** 2026-11-25 (run 45708424019, buildId 2026.11.25-350; rollback = copy `dataverse-solutions-latest.previous.json` over latest). Dev unmanaged SpaarkeMaster is 1.3.3.0. provisioning-r1 owns the ACME upgrade. Before the NEXT export, remove `sprk_DocumentDelete.js` from the dev solution (runbook).
- **Dev reader-role finding:** sent to uac-r2 privately; owner: leave it. Dev `-Verify` on the Decision Record Reader role exits 1 because of it, which is expected.

## Tasks left (10)

- In progress: **079** (dev writes and source sync done; step 10/11 data go, step 9 signed-in half and step 12 UI tests need the owner), **161** (live runs S1/S2 need the owner's OK).
- Not started: **112** (Deploy WP4, including the first `sprk_recordprocedures` rollout), **136**, **153**, **160**, **162**, **163** (UAT), **190** (wrap-up); **121** waits for uac-r2 191.

## Owner-gated, waiting

- Checklists W, X, Y (dev ready); T (iss051 backfill `-Apply`, typed OK + uac-r2 notices); U (waits for spa-r4's SPA deploy).
- MAT-000113 resubmission; 117 Apply; ISS-042 dev apply; a live full run of `Deploy-EbillingFormsViews.ps1` (DEF-046).
- Stays off: `Ebilling:StoredFileReconcileEnabled` false; 117 Apply off.

## Housekeeping (not urgent)

- Drift check lists 25 index sub-rows without a POML (pre-existing) and 028, 040, 050a, 084d, 093, 113 for evidence review.
- Agent worktrees locked by the desktop app (pid 39572), all pushed: agent-a31ddd127a439f9dd, agent-a73718461abbfd6a5, agent-afe7e9fefac5f933f, agent-accb6a7742c6a7c1b, and the batch-3 agent worktrees (a38ba0b796285b0ce, a81991fa0126f70aa, ab8be5f18e499ec7d, af1fc20670038b2c3, ad31105130a993f0c). Remove with `scripts/worktree/Remove-WorktreeSafe.ps1` once unlocked. A human should delete C:\wt131a and C:\wt131b.
- Found and left: RuleReview `assignmentPanelFirmPicker.test.ts` fails to load = ISS-033 #2885 (ontology-r1). Stray python.exe processes 41460/84288/43772/46984 (not this session's) reported to the owner.

## Standing reminders

- **Live Dataverse writes or role changes:** the owner's OK typed in THIS session; uac-r2 notice before and after; private access findings to uac-r2 by message only, never into the public repo.
- **Deploys:** dev BFF only on the owner's exact-SHA go in uac-r2's session; dev SPA via spa-r4; solution imports only via `scripts/Import-SolutionScoped.ps1`, never into ACME.
- **Commands:** never `python`, `python3` or `python -`; no `git stash`, no `--no-verify`; master PRs merge as merge commits; auto-merge only on the owner's ask; never run the whole `Sprk.Bff.Api.Tests` or ArchTests (filter, `-- xUnit.MaxParallelThreads=4`); Pester on each file's declared version (5.7.1 / 3.4.0); `MSYS_NO_PATHCONV=1` for `git show ref:.github/...`; .NET `[IO.File]` calls need full paths (they ignore `Set-Location`); `R` is the PowerShell alias for Invoke-History.
- **Agents:** at most 4, each with a named model; worktree agents get a named base sha; reviewer briefs forbid checkout/switch/reset/merge/stash in this worktree; remove worktrees only with `Remove-WorktreeSafe.ps1`.
