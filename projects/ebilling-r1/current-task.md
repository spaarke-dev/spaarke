# Current Task State

> **Last Updated:** 2026-11-26 ~17:30Z (checkpoint: task 195 fix round 1 running)
> History: `notes/handoff-history/current-task-2026-11-26a.md` (the previous state, verbatim), plus `-2026-11-24a`, `-20a`, `-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Now

- **ACTIVE: task 195** (owner "Yes, both together", 2026-11-26): Invoice Review opens in-app on SprkModal (ADR-050) with "N of M" browse over the Worklist's billing items; cross-frame dirty check; compact one-row count cards. POML `tasks/195-invoice-review-shared-modal-browse.poml`.
  - Built by an implementer agent on branch `task/ebilling-195` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-a15b3dc87b2a84c85`), tip fa956e7c76, base 9581114231. jest/builds green; notes `notes/195-invoice-review-shared-modal-browse.md` (on the task branch).
  - One Opus adversarial pass done: F1 DailyBriefing mock lacks getClientUrl (suite fails); F1 dark Console shows a light page (navbar walk is one frame; pass theme in the frame URL); F1 iframe src change pushes history (key={url}); F1 focus lost at the ends (local fix; shell bug filed ISS-055 #3262); F2 filter test applies no filter. Extras: read-only "View decision" not dirty; unsaved SetFirmField choice dirty; uiScale.
  - **Fix round 1 is running** (SendMessage to the same implementer). NEXT: check its report and run a scoped re-check of the fix diff, not a full second pass. Then merge `task/ebilling-195` into work (merge commit), push, and open a master PR (no BFF change). Deploys afterwards are owner gos: the Console bundle (`sprk_spaarkeai`, Deploy-SpaarkeAi.ps1) for the modal, and the Invoice Review page (uac-r2, owner names the SHA in its session) for the cards and the responder. Add a checklist section for the owner's browser checks (the implementer's list of 6 plus the reviewer's: Back button, focus at the ends, dark mode with no stored theme).
  - The reviewer's throwaway worktree C:\tmp\rev195 was being removed with Remove-WorktreeSafe (background); check that it is gone.
- **#3211 MERGED** 2026-11-26 04:37:54Z at 1459a14916 (merge commit; owner "Yes, merge when green", auto-merge). Carried 095d-3 (Billing firm field in the Invoice Review header, client only) and the reader-role script accepting the 9 platform-added privileges, plus a CI fix (d60656689f: the Ontology script tests job's sparse checkout now includes the S29 folder; uac-r2 found the failure). provisioning-r1 told (it now rewords the S29 refusal text and the rejection-code doc); uac-r2 told.
- **Page deployed:** `sprk_invoicereviewpage` live on dev since 2026-11-26 12:15:10Z from 1459a14916 (uac-r2, owner go in its session; 2,010,333 B, sha256 e124c6d25e83c9f27d21e775e46b9069324db4625592079f474934dcb2701306, read-back equals the build; web resource d1f2a11a-59e8-f111-84ea-7ced8dda31f8; rollback 49a8243a…9502). No BFF change. Owner checks it via checklist Z.
- **Dev:** BFF d307d67e36 being deployed by uac-r2 ~17:00Z (owner go; adds #2622 #3236 #3243 #3247 + the unmerged spike MCP tools; rollback 140c01219a; LIVE notice pending). Before that, BFF 140c01219a live since 2026-11-26 15:53:48Z (uac-r2, owner go; adds #3193 ISS-010 part 1 over 6a2a569ac9, which was live from 12:58Z; rollback 6a2a569ac9, then 1e64e94168; 30-minute gate PASSED 16:24Z, 147 requests, 0 5xx). Batch 5 BFF 1e64e94168 went live 01:56:50Z. Invoice Review page was from the same SHA 01:59:31Z (sha256 49a8243a…9502), replaced 12:15:10Z by 1459a14916. uac-r2's 30-minute gate PASSED 02:27Z (146 requests, 0 5xx). `sprk_apexport`, `sprk_invoicefiles` and `sprk_rulereviewpage` are not redeployed (refactor-only, our next page deploy). `sprk_recordprocedures` has never been on dev: its first rollout is task 112.
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
